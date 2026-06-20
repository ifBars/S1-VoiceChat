#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using S1VoiceChat.Network;
using S1VoiceChat.Routing;
using SteamNetworkLib;
using UnityEngine.SceneManagement;

#if MONOMELON
using ScheduleOne.DevUtilities;
using ScheduleOne.Persistence;
using Steamworks;
#elif IL2CPPMELON
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppSteamworks;
#endif

namespace S1VoiceChat.Testing;

internal sealed class RuntimeSmokeProbe
{
    private enum SmokeState
    {
        WaitingForMenuServices,
        WaitingForLobby,
        WaitingForMainScene,
        Running
    }

    private readonly MelonLogger.Instance _logger;
    private readonly string _resultPath;
    private readonly string _receiverResultPath;
    private readonly string _role;
    private readonly string _token;
    private readonly ulong _peerId;
    private readonly string? _lobbyFile;
    private readonly bool _requireTransport;
    private readonly bool _skipSaveLoad;
    private readonly int _saveSlot;
    private readonly DateTime _startedUtc;
    private readonly DateTime _deadlineUtc;
    private SmokeState _state;
    private DateTime _nextSendUtc = DateTime.MinValue;
    private DateTime _nextProgressLogUtc = DateTime.MinValue;
    private SnlVoiceTransport? _subscribedTransport;
    private Task<ulong>? _createLobbyTask;
    private Task<bool>? _joinLobbyTask;
    private ushort _sequence;
    private int _sendAttempts;
    private int _rawPacketsReceived;
    private bool _loadRequested;
    private bool _lobbyWritten;
    private bool _expectedPeerLogged;
    private bool _completed;

    private RuntimeSmokeProbe(
        MelonLogger.Instance logger,
        string outputDirectory,
        string role,
        string token,
        ulong peerId,
        string? lobbyFile,
        bool requireTransport,
        bool skipSaveLoad,
        int saveSlot,
        TimeSpan timeout)
    {
        _logger = logger;
        _role = role;
        _token = token;
        _peerId = peerId;
        _lobbyFile = string.IsNullOrWhiteSpace(lobbyFile) ? null : lobbyFile;
        _requireTransport = requireTransport;
        _skipSaveLoad = skipSaveLoad;
        _saveSlot = Math.Max(1, saveSlot);
        _startedUtc = DateTime.UtcNow;
        _deadlineUtc = DateTime.UtcNow + timeout;
        _state = !IsPacketExchangeRole
            ? SmokeState.Running
            : skipSaveLoad
                ? SmokeState.WaitingForMainScene
                : SmokeState.WaitingForLobby;
        _resultPath = Path.Combine(outputDirectory, role == "single" ? "result.txt" : $"result-{role}.txt");
        _receiverResultPath = Path.Combine(outputDirectory, "result-receiver.txt");
    }

    public static RuntimeSmokeProbe? TryCreate(MelonLogger.Instance logger)
    {
        var args = Environment.GetCommandLineArgs();
        var enabled = false;
        var requireTransport = false;
        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp",
            "S1VoiceChat.Smoke");
        var role = "single";
        var token = Guid.NewGuid().ToString("N");
        ulong peerId = 0;
        string? lobbyFile = null;
        var skipSaveLoad = false;
        var saveSlot = 1;
        var timeout = TimeSpan.FromSeconds(75);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--s1vc-smoke")
            {
                enabled = true;
            }
            else if (arg == "--s1vc-smoke-require-transport")
            {
                requireTransport = true;
            }
            else if (arg == "--s1vc-smoke-dir" && i + 1 < args.Length)
            {
                outputDirectory = args[++i];
            }
            else if (arg == "--s1vc-smoke-role" && i + 1 < args.Length)
            {
                role = args[++i].Trim().ToLowerInvariant();
            }
            else if (arg == "--s1vc-smoke-token" && i + 1 < args.Length)
            {
                token = args[++i];
            }
            else if (arg == "--s1vc-smoke-peer-id" && i + 1 < args.Length)
            {
                _ = ulong.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out peerId);
            }
            else if (arg == "--s1vc-smoke-lobby-file" && i + 1 < args.Length)
            {
                lobbyFile = args[++i];
            }
            else if (arg == "--s1vc-smoke-no-load")
            {
                skipSaveLoad = true;
            }
            else if (arg == "--s1vc-smoke-save-slot" && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out saveSlot);
            }
            else if (arg == "--s1vc-smoke-timeout" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    timeout = TimeSpan.FromSeconds(seconds);
            }
        }

        if (!enabled)
            return null;

        if (string.IsNullOrWhiteSpace(role))
            role = "single";

        Directory.CreateDirectory(outputDirectory);
        DeleteIfExists(Path.Combine(outputDirectory, "result.txt"));
        DeleteIfExists(Path.Combine(outputDirectory, $"result-{role}.txt"));
        if (!string.IsNullOrWhiteSpace(lobbyFile) && role == "receiver")
            DeleteIfExists(lobbyFile);

        logger.Msg($"S1 VoiceChat smoke mode enabled. Role: {role}. Output: {outputDirectory}.");
        return new RuntimeSmokeProbe(logger, outputDirectory, role, token, peerId, lobbyFile, requireTransport, skipSaveLoad, saveSlot, timeout);
    }

    public void MarkLoaded()
    {
        if (_completed)
            return;

        _logger.Msg($"S1 VoiceChat smoke loaded. Runtime: {RuntimeName}.");
        if (!_requireTransport && !IsPacketExchangeRole)
            Pass($"Loaded|Runtime={RuntimeName}|Role={_role}");
    }

    public void MarkTransportReady(string mode)
    {
        if (_completed)
            return;

        if (IsPacketExchangeRole)
        {
            _logger.Msg($"S1 VoiceChat smoke transport ready for role {_role}. Mode: {mode}.");
            return;
        }

        Pass($"TransportReady|Runtime={RuntimeName}|Role={_role}|Mode={mode}");
    }

    public void UpdateTransport(SteamNetworkClient client, SnlVoiceTransport transport, ulong localPeerId)
    {
        if (_completed)
            return;

        if (_subscribedTransport != transport)
        {
            if (_subscribedTransport != null)
                _subscribedTransport.OnPacket -= OnPacket;

            _subscribedTransport = transport;
            _subscribedTransport.OnPacket += OnPacket;
        }

        if (_state == SmokeState.WaitingForLobby)
        {
            if (!UpdateLobby(client))
            {
                LogProgress(client, "WaitingForLobbyBeforeLoad");
                return;
            }

            _logger.Msg(
                $"S1 VoiceChat smoke lobby ready for save load. Role={_role}|Elapsed={ElapsedSeconds:0.0}s|Peer={_peerId}|Members={client.GetLobbyMembers().Count}");
            _state = SmokeState.WaitingForMenuServices;
            return;
        }

        if (_state != SmokeState.Running)
            return;

        if (IsPacketExchangeRole && !UpdateLobby(client))
        {
            LogProgress(client, "WaitingForLobby");
            return;
        }

        if (_role != "sender" && _role != "receiver")
        {
            LogProgress(client, "ReceiverRunning");
            return;
        }

        if (_peerId == 0)
        {
            Fail($"{_role} role requires --s1vc-smoke-peer-id.");
            return;
        }

        if (DateTime.UtcNow < _nextSendUtc)
            return;

        _nextSendUtc = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        var packet = new VoicePacket
        {
            Version = 1,
            Channel = (byte)VoiceChannel.Global,
            Sequence = _sequence++,
            CaptureTimeMs = unchecked((uint)Environment.TickCount),
            SenderPeerId = localPeerId,
            Payload = Encoding.UTF8.GetBytes(_token)
        };

        _sendAttempts++;
        var accepted = transport.TrySendTo(_peerId, packet);
        if (_sendAttempts == 1 || _sendAttempts % 5 == 0 || !accepted)
        {
            _logger.Msg(
                $"S1 VoiceChat smoke send attempt. Role={_role}|Elapsed={ElapsedSeconds:0.0}s|Attempt={_sendAttempts}|Accepted={accepted}|Scene={SceneManager.GetActiveScene().name}|Peer={_peerId}|LocalPeer={localPeerId}|Sequence={packet.Sequence}");
        }

        if (_role == "sender" && ReceiverPassed())
            Pass($"Role=Sender|Runtime={RuntimeName}|Elapsed={ElapsedSeconds:0.0}s|Scene={SceneManager.GetActiveScene().name}|Peer={_peerId}|Token={_token}|Bytes={packet.Payload.Length}|Sends={_sendAttempts}");
    }

    private bool UpdateLobby(SteamNetworkClient client)
    {
        if (string.IsNullOrWhiteSpace(_lobbyFile))
            return IsExpectedPeerInLobby(client);

        if (_role == "receiver")
        {
            if (client.IsInLobby)
            {
                if (!_lobbyWritten)
                {
                    File.WriteAllText(_lobbyFile!, client.CurrentLobby?.LobbyId.m_SteamID.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    _lobbyWritten = true;
                    _logger.Msg($"S1 VoiceChat smoke lobby ready: {_lobbyFile}.");
                }

                return true;
            }

            _createLobbyTask ??= CreateLobbyAsync(client);
            if (!_createLobbyTask.IsCompleted)
                return false;

            if (_createLobbyTask.IsFaulted)
            {
                Fail($"Failed to create smoke lobby: {_createLobbyTask.Exception?.GetBaseException().Message ?? "unknown error"}");
                return false;
            }

            var lobbyId = _createLobbyTask.Result;
            if (lobbyId == 0)
            {
                Fail("Smoke lobby creation returned no lobby ID.");
                return false;
            }

            File.WriteAllText(_lobbyFile!, lobbyId.ToString(CultureInfo.InvariantCulture));
            _lobbyWritten = true;
            _logger.Msg($"S1 VoiceChat smoke lobby created: {lobbyId}.");
            return true;
        }

        if (_role != "sender")
            return true;

        if (client.IsInLobby)
            return true;

        if (_joinLobbyTask == null)
        {
            if (!File.Exists(_lobbyFile))
                return false;

            var raw = File.ReadAllText(_lobbyFile).Trim();
            if (!ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lobbyId) || lobbyId == 0)
                return false;

            _joinLobbyTask = JoinLobbyAsync(client, lobbyId);
            _logger.Msg($"S1 VoiceChat smoke joining lobby: {lobbyId}.");
        }

        if (!_joinLobbyTask.IsCompleted)
            return false;

        if (_joinLobbyTask.IsFaulted || !_joinLobbyTask.Result)
        {
            Fail($"Failed to join smoke lobby: {_joinLobbyTask.Exception?.GetBaseException().Message ?? "join returned false"}");
            return false;
        }

        return client.IsInLobby;
    }

    private bool IsExpectedPeerInLobby(SteamNetworkClient client)
    {
        if (!client.IsInLobby)
            return false;

        if (_peerId == 0)
            return true;

        var found = client.GetMember(_peerId) != null;
        if (found && !_expectedPeerLogged)
        {
            _expectedPeerLogged = true;
            _logger.Msg($"S1 VoiceChat smoke expected peer visible. Role={_role}|Elapsed={ElapsedSeconds:0.0}s|Peer={_peerId}|Members={client.GetLobbyMembers().Count}");
        }

        return found;
    }

    private static async Task<ulong> CreateLobbyAsync(SteamNetworkClient client)
    {
        var lobby = await client.CreateLobbyAsync(ELobbyType.k_ELobbyTypePrivate, 2);
        return lobby.LobbyId.m_SteamID;
    }

    private static async Task<bool> JoinLobbyAsync(SteamNetworkClient client, ulong lobbyId)
    {
        var lobby = await client.JoinLobbyAsync(new CSteamID(lobbyId));
        return lobby.LobbyId.m_SteamID != 0;
    }

    public void Update()
    {
        if (_completed)
            return;

        if (IsPacketExchangeRole)
            UpdateSaveLoadState();

        LogProgress(null, "Update");

        if (DateTime.UtcNow < _deadlineUtc)
            return;

        var detail = IsPacketExchangeRole
            ? $"Timed out waiting for synthetic voice packet. Runtime={RuntimeName}|Role={_role}|Elapsed={ElapsedSeconds:0.0}s|State={_state}|Scene={SceneManager.GetActiveScene().name}|Peer={_peerId}|Token={_token}|Sends={_sendAttempts}|RawPackets={_rawPacketsReceived}|ReceiverPassed={ReceiverPassed()}"
            : $"Timed out waiting for SteamNetworkLib transport. Runtime={RuntimeName}|Role={_role}";
        Fail(detail);
    }

    private bool IsPacketExchangeRole => _role == "sender" || _role == "receiver";

    private void UpdateSaveLoadState()
    {
        switch (_state)
        {
            case SmokeState.WaitingForMenuServices:
                UpdateWaitingForMenuServices();
                break;
            case SmokeState.WaitingForLobby:
                break;
            case SmokeState.WaitingForMainScene:
                if (SceneManager.GetActiveScene().name == "Main")
                {
                    _logger.Msg($"S1 VoiceChat smoke reached Main scene. Role={_role}|Elapsed={ElapsedSeconds:0.0}s");
                    _state = SmokeState.Running;
                }

                break;
        }
    }

    private void UpdateWaitingForMenuServices()
    {
        if (_loadRequested)
            return;

        if (!Singleton<LoadManager>.InstanceExists || !Singleton<SaveManager>.InstanceExists)
            return;

        var loadManager = Singleton<LoadManager>.Instance;
        if (loadManager.IsLoading)
            return;

        loadManager.RefreshSaveInfo();
        var saveInfo = ResolveSaveInfo();
        if (saveInfo == null)
        {
            Fail($"No save available for smoke test. Requested slot: {_saveSlot}");
            return;
        }

        _logger.Msg($"S1 VoiceChat smoke loading save '{saveInfo.OrganisationName}' from {saveInfo.SavePath}. Role={_role}|Elapsed={ElapsedSeconds:0.0}s");
        _loadRequested = true;
        loadManager.StartGame(saveInfo, allowLoadStacking: false, allowSaveBackup: false);
        _state = SmokeState.WaitingForMainScene;
    }

    private SaveInfo? ResolveSaveInfo()
    {
        var saves = LoadManager.SaveGames;
        if (saves == null || saves.Length == 0)
            return LoadManager.LastPlayedGame;

        var index = Math.Min(Math.Max(_saveSlot, 1), saves.Length) - 1;
        var selected = saves[index];
        if (selected != null)
            return selected;

        if (LoadManager.LastPlayedGame != null)
            return LoadManager.LastPlayedGame;

        foreach (var save in saves)
        {
            if (save != null)
                return save;
        }

        return null;
    }

    private void OnPacket(ulong networkSenderPeerId, VoicePacket packet)
    {
        _rawPacketsReceived++;
        if (_rawPacketsReceived == 1 || _rawPacketsReceived % 5 == 0)
        {
            _logger.Msg(
                $"S1 VoiceChat smoke raw packet observed. Role={_role}|Elapsed={ElapsedSeconds:0.0}s|Count={_rawPacketsReceived}|NetworkSender={networkSenderPeerId}|PacketSender={packet.SenderPeerId}|Bytes={packet.Payload.Length}|Sequence={packet.Sequence}");
        }

        if (_completed || _role != "receiver")
            return;

        var actualSender = packet.SenderPeerId == 0 ? networkSenderPeerId : packet.SenderPeerId;
        if (_peerId != 0 && actualSender != _peerId)
            return;

        var payload = Encoding.UTF8.GetString(packet.Payload);
        if (!string.Equals(payload, _token, StringComparison.Ordinal))
            return;

        Pass($"Role=Receiver|Runtime={RuntimeName}|Elapsed={ElapsedSeconds:0.0}s|Scene={SceneManager.GetActiveScene().name}|From={actualSender}|NetworkSender={networkSenderPeerId}|Token={_token}|Sequence={packet.Sequence}|RawPackets={_rawPacketsReceived}");
    }

    private double ElapsedSeconds => (DateTime.UtcNow - _startedUtc).TotalSeconds;

    private void LogProgress(SteamNetworkClient? client, string source)
    {
        if (DateTime.UtcNow < _nextProgressLogUtc)
            return;

        _nextProgressLogUtc = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        var inLobby = client?.IsInLobby;
        var peerVisible = client != null && _peerId != 0 && client.GetMember(_peerId) != null;
        var memberCount = client?.GetLobbyMembers().Count;
        _logger.Msg(
            $"S1 VoiceChat smoke progress. Source={source}|Role={_role}|Elapsed={ElapsedSeconds:0.0}s|State={_state}|Scene={SceneManager.GetActiveScene().name}|InLobby={FormatNullable(inLobby)}|ExpectedPeerVisible={peerVisible}|Members={FormatNullable(memberCount)}|Sends={_sendAttempts}|RawPackets={_rawPacketsReceived}|ReceiverPassed={ReceiverPassed()}");
    }

    private static string FormatNullable<T>(T? value) where T : struct
    {
        return value.HasValue ? value.Value.ToString() ?? string.Empty : "unknown";
    }

    private bool ReceiverPassed()
    {
        if (!File.Exists(_receiverResultPath))
            return false;

        var text = File.ReadAllText(_receiverResultPath);
        return text.StartsWith("PASS|", StringComparison.Ordinal);
    }

    private static string RuntimeName
    {
        get
        {
#if MONOMELON
            return "Mono";
#elif IL2CPPMELON
            return "Il2Cpp";
#else
            return "Unknown";
#endif
        }
    }

    private void Pass(string detail)
    {
        if (_subscribedTransport != null)
            _subscribedTransport.OnPacket -= OnPacket;

        var text = "PASS|" + detail;
        File.WriteAllText(_resultPath, text);
        _logger.Msg($"[S1VoiceChatSmoke] {text}");
        _completed = true;
    }

    private void Fail(string reason)
    {
        if (_subscribedTransport != null)
            _subscribedTransport.OnPacket -= OnPacket;

        var text = "FAIL|" + reason;
        File.WriteAllText(_resultPath, text);
        _logger.Error($"[S1VoiceChatSmoke] {text}");
        _completed = true;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
#endif
