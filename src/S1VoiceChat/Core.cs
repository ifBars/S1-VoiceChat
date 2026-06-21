#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using MelonLoader;
using S1VoiceChat.Network;
using S1VoiceChat.Runtime;
using S1VoiceChat.Testing;
using SteamNetworkLib;
using UnityEngine;

[assembly: MelonInfo(typeof(S1VoiceChat.Core), "S1 VoiceChat", "1.0.1", "Bars")]
[assembly: MelonColor(255, 74, 144, 226)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace S1VoiceChat;

public sealed class Core : MelonMod
{
    private static readonly MelonLogger.Instance Logger = new("S1VoiceChat");
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private SteamNetworkClient? _networkClient;
    private SteamNetworkLibVoicePacketClient? _voicePacketClient;
    private SnlVoiceTransport? _voiceTransport;
    private RuntimeSmokeProbe? _smokeProbe;
    private ManualVoiceTestProbe? _manualTestProbe;
    private SteamVoiceCaptureProbe? _steamVoiceCaptureProbe;
    private SettingsScreenshotProbe? _settingsScreenshotProbe;
    private LiveVoiceRuntime? _liveVoiceRuntime;
    private VoiceChatSettingsUiInjector? _settingsUiInjector;
    private DateTime _nextInitAttemptUtc = DateTime.MinValue;
    private bool _initialized;

    public override void OnInitializeMelon()
    {
        Logger.Msg("S1 VoiceChat loading.");
        VoiceChatPreferences.Initialize();
        _settingsUiInjector = new VoiceChatSettingsUiInjector(Logger);
        _smokeProbe = RuntimeSmokeProbe.TryCreate(Logger);
        _smokeProbe?.MarkLoaded();
        _manualTestProbe = ManualVoiceTestProbe.TryCreate(Logger, Application.isBatchMode);
        _steamVoiceCaptureProbe = SteamVoiceCaptureProbe.TryCreate(Logger);
        _settingsScreenshotProbe = SettingsScreenshotProbe.TryCreate(Logger);
        _liveVoiceRuntime = LiveVoiceRuntime.TryCreate(Logger);
    }

    public override void OnUpdate()
    {
        if (_initialized)
        {
            if (_voiceTransport != null && _networkClient != null)
            {
                _smokeProbe?.UpdateTransport(_networkClient, _voiceTransport, _networkClient.LocalPlayerId64);
                _manualTestProbe?.Update(_networkClient, _voiceTransport, _networkClient.LocalPlayerId64);
                _liveVoiceRuntime?.Update(_networkClient, _voiceTransport, _networkClient.LocalPlayerId64);
            }

            _voiceTransport?.Poll();
            _steamVoiceCaptureProbe?.Update();
            _settingsUiInjector?.Update();
            _settingsScreenshotProbe?.Update();
            _smokeProbe?.Update();
            return;
        }

        _settingsUiInjector?.Update();
        _steamVoiceCaptureProbe?.Update();
        _settingsScreenshotProbe?.Update();
        _smokeProbe?.Update();

        if (DateTime.UtcNow < _nextInitAttemptUtc)
            return;

        _nextInitAttemptUtc = DateTime.UtcNow + RetryDelay;
        TryInitializeNetworking();
    }

    public override void OnGUI()
    {
        _liveVoiceRuntime?.OnGUI();
    }

    public override void OnApplicationQuit()
    {
        _manualTestProbe?.Dispose();
        _steamVoiceCaptureProbe?.Dispose();
        _liveVoiceRuntime?.Dispose();
        _voiceTransport?.Dispose();
        _voicePacketClient?.Dispose();
        _networkClient?.Dispose();

        _manualTestProbe = null;
        _steamVoiceCaptureProbe = null;
        _settingsScreenshotProbe = null;
        _liveVoiceRuntime = null;
        _voiceTransport = null;
        _voicePacketClient = null;
        _networkClient = null;
        _settingsUiInjector = null;
        _initialized = false;
    }

    private void TryInitializeNetworking()
    {
        _networkClient ??= new SteamNetworkClient();

        if (!_networkClient.TryInitialize(out var error))
        {
            Logger.Warning($"SteamNetworkLib is not ready: {error?.Message ?? "unknown error"}");
            return;
        }

        _voicePacketClient = new SteamNetworkLibVoicePacketClient(_networkClient);
        _voiceTransport = new SnlVoiceTransport(_voicePacketClient);
        _initialized = true;

        Logger.Msg($"SteamNetworkLib voice transport ready. Mode: {_networkClient.SessionMode}.");
        _smokeProbe?.MarkTransportReady(_networkClient.SessionMode.ToString());
    }
}
#endif
