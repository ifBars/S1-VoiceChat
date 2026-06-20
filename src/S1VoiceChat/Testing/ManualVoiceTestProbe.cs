#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using S1VoiceChat.Network;
using S1VoiceChat.Routing;
using S1VoiceChat.Runtime;
using SteamNetworkLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace S1VoiceChat.Testing;

internal sealed class ManualVoiceTestProbe
{
    private readonly MelonLogger.Instance _logger;
    private readonly KeyCode _sendKey;
    private readonly string _token;
    private readonly string _logPath;
    private SnlVoiceTransport? _transport;
    private uint _sequence;
    private DateTime _nextStatusUtc = DateTime.MinValue;

    private ManualVoiceTestProbe(MelonLogger.Instance logger, KeyCode sendKey, string token)
    {
        _logger = logger;
        _sendKey = sendKey;
        _token = token;
        _logPath = Path.Combine(Environment.CurrentDirectory, "UserData", "S1VoiceChat", "manual-test.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
    }

    public static ManualVoiceTestProbe? TryCreate(MelonLogger.Instance logger, bool isBatchMode)
    {
        var args = Environment.GetCommandLineArgs();
        var enabled = false;
        var key = KeyCode.F8;
        var token = Guid.NewGuid().ToString("N");

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--s1vc-manual-test")
            {
                enabled = true;
            }
            else if (arg == "--s1vc-manual-key" && i + 1 < args.Length)
            {
                if (!Enum.TryParse(args[++i], ignoreCase: true, out key))
                    key = KeyCode.F8;
            }
            else if (arg == "--s1vc-manual-token" && i + 1 < args.Length)
            {
                token = args[++i];
            }
        }

        if (!LiveVoiceRuntimePolicy.CanCreateInteractiveProbe(enabled, isBatchMode))
        {
            if (enabled)
                logger.Msg("[ManualTest] Manual voice test skipped in batch/headless mode.");

            return null;
        }

        var probe = new ManualVoiceTestProbe(logger, key, token);
        probe.Log($"Manual voice test enabled. Key={key}|Token={token}|Log={probe._logPath}");
        return probe;
    }

    public void Update(SteamNetworkClient client, SnlVoiceTransport transport, ulong localPeerId)
    {
        if (_transport != transport)
        {
            if (_transport != null)
                _transport.OnPacket -= OnPacket;

            _transport = transport;
            _transport.OnPacket += OnPacket;
        }

        LogStatus(client, localPeerId);

        if (!Input.GetKeyDown(_sendKey))
            return;

        SendTestPacket(client, transport, localPeerId);
    }

    public void Dispose()
    {
        if (_transport != null)
            _transport.OnPacket -= OnPacket;

        _transport = null;
    }

    private void SendTestPacket(SteamNetworkClient client, SnlVoiceTransport transport, ulong localPeerId)
    {
        if (!client.IsInLobby)
        {
            Log($"Manual send skipped. Not in a lobby. Scene={SceneManager.GetActiveScene().name}|LocalPeer={localPeerId}");
            return;
        }

        var peers = client.GetRemoteMembers()
            .Select(member => member.SteamId.m_SteamID)
            .Where(peerId => peerId != 0)
            .Distinct()
            .ToArray();

        if (peers.Length == 0)
        {
            Log($"Manual send skipped. No remote lobby members. Scene={SceneManager.GetActiveScene().name}|LocalPeer={localPeerId}|Members={client.GetLobbyMembers().Count}");
            return;
        }

        var sequence = _sequence++;
        var payloadText = $"manual|token={_token}|from={localPeerId}|seq={sequence}|utc={DateTime.UtcNow:O}";
        var packet = new VoicePacket
        {
            Version = 1,
            Channel = (byte)VoiceChannel.Global,
            Sequence = unchecked((ushort)sequence),
            CaptureTimeMs = unchecked((uint)Environment.TickCount),
            SenderPeerId = localPeerId,
            Payload = Encoding.UTF8.GetBytes(payloadText)
        };

        var accepted = 0;
        foreach (var peerId in peers)
        {
            if (transport.TrySendTo(peerId, packet))
                accepted++;
        }

        Log($"Manual send. Scene={SceneManager.GetActiveScene().name}|LocalPeer={localPeerId}|Peers={string.Join(",", peers)}|Accepted={accepted}/{peers.Length}|Sequence={sequence}|Token={_token}");
    }

    private void OnPacket(ulong networkSenderPeerId, VoicePacket packet)
    {
        var payload = TryDecodePayload(packet.Payload);
        if (!IsManualPayload(payload))
            return;

        var tokenMatch = payload.Contains("token=" + _token, StringComparison.Ordinal);
        Log($"Manual receive. Scene={SceneManager.GetActiveScene().name}|NetworkSender={networkSenderPeerId}|PacketSender={packet.SenderPeerId}|Sequence={packet.Sequence}|Bytes={packet.Payload.Length}|TokenMatch={tokenMatch}|Payload={payload}");
    }

    private void LogStatus(SteamNetworkClient client, ulong localPeerId)
    {
        if (DateTime.UtcNow < _nextStatusUtc)
            return;

        _nextStatusUtc = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var remotePeers = client.GetRemoteMembers().Select(member => member.SteamId.m_SteamID.ToString(CultureInfo.InvariantCulture));
        Log($"Manual status. Scene={SceneManager.GetActiveScene().name}|InLobby={client.IsInLobby}|Members={client.GetLobbyMembers().Count}|RemotePeers={string.Join(",", remotePeers)}|LocalPeer={localPeerId}|Key={_sendKey}");
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        _logger.Msg("[ManualTest] " + message);
        File.AppendAllText(_logPath, line + Environment.NewLine);
    }

    private static string TryDecodePayload(byte[] payload)
    {
        try
        {
            return Encoding.UTF8.GetString(payload);
        }
        catch
        {
            return "<binary>";
        }
    }

    private static bool IsManualPayload(string payload)
    {
        return payload.StartsWith("manual|", StringComparison.Ordinal);
    }
}
#endif
