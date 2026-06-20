#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using S1VoiceChat.Capture;
using S1VoiceChat.Codec;
using S1VoiceChat.Network;
using S1VoiceChat.Playback;
using S1VoiceChat.Routing;
using SteamNetworkLib;
using UnityEngine;
using UnityEngine.SceneManagement;

#if MONOMELON
using ScheduleOne.PlayerScripts;
using PlayerType = ScheduleOne.PlayerScripts.Player;
#elif IL2CPPMELON
using Il2CppScheduleOne.PlayerScripts;
using PlayerType = Il2CppScheduleOne.PlayerScripts.Player;
#endif

namespace S1VoiceChat.Runtime;

internal sealed class LiveVoiceRuntime : IDisposable
{
    private const int DefaultLiveSampleRate = 48000;
    private const int DefaultLiveFrameSize = 480;

    private enum CaptureSource
    {
        Microphone,
        Wasapi,
        Tone
    }

    private readonly MelonLogger.Instance _logger;
    private readonly VoiceSettings _settings;
    private readonly VoiceRecipientResolver _recipientResolver;
    private readonly VoiceMuteList _muteList;
    private readonly KeyCode _pushToTalkKey;
    private readonly bool _forceOpenMic;
    private readonly bool _forceDiagnosticLogging;
    private readonly VoiceChannel _voiceChannel;
    private readonly CaptureSource _captureSource;
    private readonly string? _microphoneDevice;
    private readonly PttHudOverlay _hudOverlay;
    private readonly short[] _captureFrame;
    private IVoiceCapture? _capture;
    private IVoiceCaptureDiagnostics? _captureDiagnostics;
    private UnityVoicePlaybackSink? _playback;
    private VoiceSession? _session;
    private SnlVoiceTransport? _transport;
    private int _sentFrames;
    private int _receivedFrames;
    private int _capturedFrames;
    private int _silentFrames;
    private int _lastRecipientCount;
    private long _lastCaptureEnergy;
    private DateTime _nextStatusUtc = DateTime.MinValue;
    private DateTime _nextCaptureDiagnosticUtc = DateTime.MinValue;
    private bool _recording;
    private bool _disabled;
    private bool _disposed;

    private LiveVoiceRuntime(MelonLogger.Instance logger, VoiceSettings settings, VoiceMuteList muteList, KeyCode pushToTalkKey, bool openMic, bool diagnosticLogging, VoiceChannel voiceChannel, CaptureSource captureSource, string? microphoneDevice)
    {
        _logger = logger;
        _settings = settings;
        _recipientResolver = new VoiceRecipientResolver(settings);
        _muteList = muteList;
        _pushToTalkKey = pushToTalkKey;
        _forceOpenMic = openMic;
        _forceDiagnosticLogging = diagnosticLogging;
        _voiceChannel = voiceChannel;
        _captureSource = captureSource;
        _microphoneDevice = microphoneDevice;
        _hudOverlay = new PttHudOverlay(logger, pushToTalkKey);
        _captureFrame = new short[settings.FrameSize * settings.Channels];
    }

    public static LiveVoiceRuntime? TryCreate(MelonLogger.Instance logger)
    {
        var args = Environment.GetCommandLineArgs();
        var enabled = false;
        var openMic = false;
        var voiceChannel = VoiceChannel.Global;
        var captureSource = CaptureSource.Wasapi;
        var key = KeyCode.V;
        string? microphoneDevice = "auto";
        var diagnosticLogging = false;
        var settings = new VoiceSettings
        {
            SampleRate = DefaultLiveSampleRate,
            Channels = 1,
            FrameSize = DefaultLiveFrameSize,
            MaxPacketsPerPeerPerSecond = 80
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--s1vc-live-voice")
            {
                enabled = true;
            }
            else if (arg == "--s1vc-open-mic")
            {
                openMic = true;
            }
            else if (arg == "--s1vc-debug-logs" || arg == "--s1vc-voice-diagnostics" || arg == "--s1vc-diagnostics")
            {
                diagnosticLogging = true;
            }
            else if (arg == "--s1vc-proximity-voice")
            {
                voiceChannel = VoiceChannel.Proximity;
            }
            else if (arg == "--s1vc-voice-channel" && i + 1 < args.Length)
            {
                if (!Enum.TryParse(args[++i], ignoreCase: true, out voiceChannel))
                    voiceChannel = VoiceChannel.Global;
            }
            else if (arg == "--s1vc-ptt-key" && i + 1 < args.Length)
            {
                if (!Enum.TryParse(args[++i], ignoreCase: true, out key))
                    key = KeyCode.V;
            }
            else if (arg == "--s1vc-sample-rate" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleRate) && sampleRate > 0)
                    settings.SampleRate = Math.Min(sampleRate, 48000);
            }
            else if (arg == "--s1vc-mic-device" && i + 1 < args.Length)
            {
                microphoneDevice = args[++i];
            }
            else if (arg == "--s1vc-mic-device-index" && i + 1 < args.Length)
            {
                microphoneDevice = args[++i];
            }
            else if (arg == "--s1vc-capture-source" && i + 1 < args.Length)
            {
                var value = args[++i];
                if (value.Equals("tone", StringComparison.OrdinalIgnoreCase))
                {
                    captureSource = CaptureSource.Tone;
                    microphoneDevice = null;
                }
                else if (value.Equals("wasapi", StringComparison.OrdinalIgnoreCase))
                {
                    captureSource = CaptureSource.Wasapi;
                    microphoneDevice ??= "auto";
                }
                else
                {
                    captureSource = CaptureSource.Microphone;
                    if (string.Equals(microphoneDevice, "auto", StringComparison.OrdinalIgnoreCase))
                        microphoneDevice = null;
                }
            }
            else if (arg == "--s1vc-test-tone")
            {
                captureSource = CaptureSource.Tone;
                microphoneDevice = null;
            }
        }

        settings.FrameSize = Math.Min(Math.Max(160, settings.SampleRate / 100), VoicePacket.MaxPayloadBytes / (settings.Channels * sizeof(short)));

        if (!LiveVoiceRuntimePolicy.CanCreateLiveVoice(enabled, Application.isBatchMode))
        {
            if (enabled)
                logger.Msg("Live voice mode skipped in batch/headless mode.");

            return null;
        }

        var muteList = VoiceMuteList.FromCommandLine(args);
        VoiceChatPreferences.ApplyTo(settings);
        settings.OpenMicEnabled = settings.OpenMicEnabled || openMic;
        settings.PushToTalkEnabled = !settings.OpenMicEnabled;
        settings.DiagnosticLoggingEnabled = settings.DiagnosticLoggingEnabled || diagnosticLogging;

        logger.Msg($"Live voice mode enabled. Capture={captureSource}|Codec=Pcm16|PushToTalkKey={key}|OpenMic={openMic}|Channel={voiceChannel}|SampleRate={settings.SampleRate}|FrameSize={settings.FrameSize}|MicDevice={microphoneDevice ?? "<first>"}|MutedPeers={muteList.Count}");
        return new LiveVoiceRuntime(logger, settings, muteList, key, openMic, diagnosticLogging, voiceChannel, captureSource, microphoneDevice);
    }

    public void Update(SteamNetworkClient client, SnlVoiceTransport transport, ulong localPeerId)
    {
        if (_disposed || _disabled)
            return;

        if (!IsVoiceScene())
        {
            StopRecording();
            DisposeSessionOnly();
            return;
        }

        if (!EnsureSession(transport, localPeerId))
            return;

        VoiceChatPreferences.ApplyTo(_settings);
        _settings.DiagnosticLoggingEnabled = _settings.DiagnosticLoggingEnabled || _forceDiagnosticLogging;
        _playback?.UpdateSpeakerPositions(ResolveRemotePlayerPositions());
        _session?.Update();
        _playback?.Update(_session!);

        var shouldCapture = VoiceCaptureModePolicy.ShouldCapture(_forceOpenMic, _settings, Input.GetKey(_pushToTalkKey));
        UpdateRecording(shouldCapture);
        if (_recording)
            SendCapturedVoice(client, localPeerId);

        LogStatus(client, shouldCapture);
    }

    public void OnGUI()
    {
        if (_disposed || _disabled || !IsVoiceScene())
            return;

        _hudOverlay.Draw(_recording, IsOpenMicEnabled());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopRecording();
        DisposeSessionOnly();
        _hudOverlay.Dispose();
        _disposed = true;
    }

    private bool EnsureSession(SnlVoiceTransport transport, ulong localPeerId)
    {
        if (_transport == transport && _session != null && _capture != null && _playback != null)
            return true;

        if (!transport.IsReady || localPeerId == 0)
            return false;

        DisposeSessionOnly();

        try
        {
            _capture = CreateCapture();
            _captureDiagnostics = _capture as IVoiceCaptureDiagnostics;
            _session = new VoiceSession(
                localPeerId,
                transport,
                new Pcm16Codec(_settings.SampleRate, _settings.Channels, _settings.FrameSize),
                _settings,
                () => new Pcm16Codec(_settings.SampleRate, _settings.Channels, _settings.FrameSize));
            _playback = new UnityVoicePlaybackSink(_settings.SampleRate, _settings.Channels, _settings.FrameSize, _settings);
            _transport = transport;
            _logger.Msg($"Live voice session ready. Capture={_captureSource}|Codec=Pcm16|SampleRate={_settings.SampleRate}|FrameSize={_settings.FrameSize}|Scene={SceneManager.GetActiveScene().name}");
            return true;
        }
        catch (Exception ex)
        {
            DisposeSessionOnly();
            _disabled = true;
            _logger.Error($"Live voice mode disabled: {ex.Message}");
            return false;
        }
    }

    private void UpdateRecording(bool shouldCapture)
    {
        if (shouldCapture == _recording)
            return;

        if (shouldCapture)
        {
            _capture?.Start();
            _recording = _capture?.IsCapturing == true;
            if (_recording && _settings.DiagnosticLoggingEnabled)
                _logger.Msg($"Live voice recording started. Scene={SceneManager.GetActiveScene().name}|Focused={Application.isFocused}|PushToTalkKey={_pushToTalkKey}|OpenMic={IsOpenMicEnabled()}");

            return;
        }

        StopRecording();
    }

    private void StopRecording()
    {
        if (!_recording)
            return;

        _capture?.Stop();
        _recording = false;
        if (_settings.DiagnosticLoggingEnabled)
            _logger.Msg($"Live voice recording stopped. Scene={SceneManager.GetActiveScene().name}|Focused={Application.isFocused}|CapturedFrames={_capturedFrames}|SentFrames={_sentFrames}|SilentFrames={_silentFrames}");
    }

    private void SendCapturedVoice(SteamNetworkClient client, ulong localPeerId)
    {
        if (_session == null || _capture == null)
            return;

        var samples = _capture.ReadFrame(_captureFrame);
        if (samples <= 0)
            return;

        _lastCaptureEnergy = VoiceDiagnostics.SumAbsolutePcm(_captureFrame.AsSpan(0, samples));
        _capturedFrames++;
        if (_lastCaptureEnergy == 0)
            _silentFrames++;

        var recipients = ResolveRecipients(client, localPeerId, _voiceChannel);
        _lastRecipientCount = recipients.Count;
        if (recipients.Count == 0)
        {
            LogCaptureDiagnostic($"Captured frame has no recipients. Samples={samples}|Energy={_lastCaptureEnergy}|{GetCaptureDiagnosticSummary()}|Members={client.GetLobbyMembers().Count}|RemoteMembers={client.GetRemoteMembers().Count}");
            return;
        }

        _session.SendPcmFrame(_captureFrame.AsSpan(0, samples), _voiceChannel, recipients);
        _sentFrames++;
        if (_settings.DiagnosticLoggingEnabled && (_sentFrames == 1 || _sentFrames % 25 == 0))
            _logger.Msg($"Live voice sent frame. Sequence={_sentFrames}|Samples={samples}|Energy={_lastCaptureEnergy}|{GetCaptureDiagnosticSummary()}|Recipients={string.Join(",", recipients)}|SentFrames={_sentFrames}");
    }

    private void LogStatus(SteamNetworkClient client, bool transmitting)
    {
        if (!_settings.DiagnosticLoggingEnabled)
            return;

        if (DateTime.UtcNow < _nextStatusUtc)
            return;

        _nextStatusUtc = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        _receivedFrames = CountReceivedFrames();
        _logger.Msg(
            $"Live voice status. Scene={SceneManager.GetActiveScene().name}|Focused={Application.isFocused}|InLobby={client.IsInLobby}|Members={client.GetLobbyMembers().Count}|RemoteMembers={client.GetRemoteMembers().Count}|Channel={_voiceChannel}|Capture={_captureSource}|SampleRate={_settings.SampleRate}|OpenMic={IsOpenMicEnabled()}|Transmitting={transmitting}|Recording={_recording}|Recipients={_lastRecipientCount}|CapturedFrames={_capturedFrames}|SentFrames={_sentFrames}|ReceivedStreams={_session?.RemoteStreams.Count ?? 0}|ReceivedBufferedSamples={_receivedFrames}|SilentFrames={_silentFrames}|LastCaptureEnergy={_lastCaptureEnergy}|{GetCaptureDiagnosticSummary()}|PushToTalkKey={_pushToTalkKey}");
    }

    private void LogCaptureDiagnostic(string message)
    {
        if (!_settings.DiagnosticLoggingEnabled)
            return;

        if (DateTime.UtcNow < _nextCaptureDiagnosticUtc)
            return;

        _nextCaptureDiagnosticUtc = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        _logger.Msg($"Live voice capture diagnostic. {message}|Scene={SceneManager.GetActiveScene().name}|Focused={Application.isFocused}|Recording={_recording}|CapturedFrames={_capturedFrames}|SentFrames={_sentFrames}|SilentFrames={_silentFrames}");
    }

    private int CountReceivedFrames()
    {
        if (_session == null)
            return 0;

        var buffered = 0;
        foreach (var stream in _session.RemoteStreams.Values)
            buffered += stream.BufferedSamples;

        return buffered;
    }

    private IReadOnlyList<ulong> ResolveRecipients(SteamNetworkClient client, ulong localPeerId, VoiceChannel channel)
    {
        return _recipientResolver.Resolve(localPeerId, channel, ResolvePlayerStates(localPeerId, _muteList), GetRemoteLobbyPeerIds(client), _muteList);
    }

    private static IReadOnlyList<ulong> GetRemoteLobbyPeerIds(SteamNetworkClient client)
    {
        var result = new List<ulong>();
        foreach (var member in client.GetRemoteMembers())
        {
            var peerId = member.SteamId.m_SteamID;
            if (peerId != 0 && !result.Contains(peerId))
                result.Add(peerId);
        }

        return result;
    }

    private static List<VoicePeerState> ResolvePlayerStates(ulong localPeerId, VoiceMuteList muteList)
    {
        var result = new List<VoicePeerState>();
        var players = PlayerType.PlayerList;
        if (players == null)
            return result;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null)
                continue;

            var peerId = ResolvePlayerPeerId(player, localPeerId);
            if (peerId == 0)
                continue;

            var position = player.gameObject.transform.position;
            result.Add(new VoicePeerState(peerId, position.x, position.y, position.z, muteList.IsMuted(peerId)));
        }

        return result;
    }

    private static ulong ResolvePlayerPeerId(PlayerType player, ulong localPeerId)
    {
        if (player.IsLocalPlayer)
            return localPeerId;

        if (ulong.TryParse(player.PlayerCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var peerId))
            return peerId;

        return 0;
    }

    private static IReadOnlyDictionary<ulong, Vector3> ResolveRemotePlayerPositions()
    {
        var result = new Dictionary<ulong, Vector3>();
        var players = PlayerType.PlayerList;
        if (players == null)
            return result;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || player.IsLocalPlayer)
                continue;

            if (!ulong.TryParse(player.PlayerCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var peerId) || peerId == 0)
                continue;

            result[peerId] = player.gameObject.transform.position;
        }

        return result;
    }

    private void DisposeSessionOnly()
    {
        StopRecording();
        _session?.Dispose();
        _capture?.Dispose();
        _playback?.Dispose();
        _session = null;
        _capture = null;
        _captureDiagnostics = null;
        _playback = null;
        _transport = null;
    }

    private bool IsOpenMicEnabled()
    {
        return VoiceCaptureModePolicy.IsOpenMicEnabled(_forceOpenMic, _settings);
    }

    private IVoiceCapture CreateCapture()
    {
        return _captureSource == CaptureSource.Tone
            ? new SyntheticToneCapture(_settings.SampleRate, _settings.Channels, _settings.FrameSize)
            : _captureSource == CaptureSource.Wasapi
                ? new WasapiMicrophoneCapture(_logger, _settings.SampleRate, _settings.Channels, _settings.FrameSize, _microphoneDevice, _settings.DiagnosticLoggingEnabled)
                : new UnityMicrophonePcmCapture(_logger, _settings.SampleRate, _settings.Channels, _settings.FrameSize, _microphoneDevice, _settings.DiagnosticLoggingEnabled);
    }

    private string GetCaptureDevice()
    {
        return _captureDiagnostics?.DeviceLabel ?? _captureSource.ToString();
    }

    private int GetCapturePosition()
    {
        return _captureDiagnostics?.LastReadPosition ?? 0;
    }

    private int GetAvailableSourceSamples()
    {
        return _captureDiagnostics?.LastAvailableSourceSamples ?? 0;
    }

    private float GetCapturePeak()
    {
        return _captureDiagnostics?.LastPeak ?? 0f;
    }

    private string GetCaptureDiagnosticSummary()
    {
        return _captureDiagnostics?.GetDiagnosticSummary() ?? $"CaptureDevice={_captureSource}|CapturePosition=0|AvailableSourceSamples=0|CapturePeak=0.000000";
    }

    private static bool IsVoiceScene()
    {
        return LiveVoiceRuntimePolicy.IsVoiceScene(SceneManager.GetActiveScene().name);
    }
}
#endif
