#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

#if MONOMELON
using Steamworks;
#elif IL2CPPMELON
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSteamworks;
#endif

namespace S1VoiceChat.Testing;

internal sealed class SteamVoiceCaptureProbe : IDisposable
{
    private readonly MelonLogger.Instance _logger;
    private readonly KeyCode _pushToTalkKey;
    private readonly byte[] _managedBuffer;
#if IL2CPPMELON
    private readonly Il2CppStructArray<byte> _il2CppBuffer;
#endif
    private DateTime _nextDiagnosticUtc = DateTime.MinValue;
    private bool _recording;
    private int _availableFailures;
    private int _captureFailures;
    private int _availableOk;
    private int _capturedFrames;
    private string _lastAvailableResult = "None";
    private string _lastCaptureResult = "None";

    private SteamVoiceCaptureProbe(MelonLogger.Instance logger, KeyCode pushToTalkKey)
    {
        _logger = logger;
        _pushToTalkKey = pushToTalkKey;
        _managedBuffer = new byte[4096];
#if IL2CPPMELON
        _il2CppBuffer = new Il2CppStructArray<byte>(_managedBuffer.Length);
#endif
    }

    public static SteamVoiceCaptureProbe? TryCreate(MelonLogger.Instance logger)
    {
        var args = Environment.GetCommandLineArgs();
        var enabled = false;
        var key = KeyCode.V;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--s1vc-steam-voice-probe")
            {
                enabled = true;
            }
            else if (arg == "--s1vc-ptt-key" && i + 1 < args.Length)
            {
                if (!Enum.TryParse(args[++i], ignoreCase: true, out key))
                    key = KeyCode.V;
            }
        }

        if (!enabled || Application.isBatchMode)
            return null;

        logger.Msg($"[SteamVoiceProbe] Enabled. Key={key}|SteamLoggedOn={SteamUser.BLoggedOn()}|SampleRate={SteamUser.GetVoiceOptimalSampleRate()}");
        return new SteamVoiceCaptureProbe(logger, key);
    }

    public void Update()
    {
        if (Input.GetKeyDown(_pushToTalkKey))
            StartRecording();

        if (Input.GetKeyUp(_pushToTalkKey))
            StopRecording();

        if (_recording)
            PollVoice();
    }

    public void Dispose()
    {
        StopRecording();
    }

    private void StartRecording()
    {
        if (_recording)
            return;

        SteamUser.StartVoiceRecording();
        _recording = true;
        _logger.Msg($"[SteamVoiceProbe] Recording started. Scene={SceneManager.GetActiveScene().name}|Focused={Application.isFocused}|SteamLoggedOn={SteamUser.BLoggedOn()}|SampleRate={SteamUser.GetVoiceOptimalSampleRate()}");
    }

    private void StopRecording()
    {
        if (!_recording)
            return;

        SteamUser.StopVoiceRecording();
        _recording = false;
        _logger.Msg($"[SteamVoiceProbe] Recording stopped. CapturedFrames={_capturedFrames}|AvailableOk={_availableOk}|AvailableFailures={_availableFailures}|CaptureFailures={_captureFailures}|LastAvailableResult={_lastAvailableResult}|LastCaptureResult={_lastCaptureResult}");
    }

    private void PollVoice()
    {
        var availableResult = SteamUser.GetAvailableVoice(out var availableBytes);
        _lastAvailableResult = availableResult.ToString();
        if (availableResult != EVoiceResult.k_EVoiceResultOK)
        {
            _availableFailures++;
            LogDiagnostic($"GetAvailableVoice={availableResult}|AvailableBytes={availableBytes}");
            return;
        }

        _availableOk++;
        if (availableBytes == 0)
        {
            LogDiagnostic("GetAvailableVoice=OK|AvailableBytes=0");
            return;
        }

        var bufferSize = Math.Min((int)Math.Max(availableBytes * 2, availableBytes), _managedBuffer.Length);
        var captureResult = GetVoice((uint)bufferSize, out var writtenBytes);
        _lastCaptureResult = captureResult.ToString();
        if (captureResult != EVoiceResult.k_EVoiceResultOK)
        {
            _captureFailures++;
            LogDiagnostic($"GetVoice={captureResult}|AvailableBytes={availableBytes}|BufferBytes={bufferSize}|WrittenBytes={writtenBytes}");
            return;
        }

        if (writtenBytes == 0)
        {
            LogDiagnostic($"GetVoice=OK|AvailableBytes={availableBytes}|WrittenBytes=0");
            return;
        }

        _capturedFrames++;
        _logger.Msg($"[SteamVoiceProbe] Captured voice. AvailableBytes={availableBytes}|WrittenBytes={writtenBytes}|CapturedFrames={_capturedFrames}");
    }

    private EVoiceResult GetVoice(uint bufferBytes, out uint writtenBytes)
    {
#if IL2CPPMELON
        return SteamUser.GetVoice(true, _il2CppBuffer, bufferBytes, out writtenBytes);
#else
        return SteamUser.GetVoice(true, _managedBuffer, bufferBytes, out writtenBytes);
#endif
    }

    private void LogDiagnostic(string message)
    {
        if (DateTime.UtcNow < _nextDiagnosticUtc)
            return;

        _nextDiagnosticUtc = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        _logger.Msg($"[SteamVoiceProbe] {message}|Scene={SceneManager.GetActiveScene().name}|Focused={Application.isFocused}|Recording={_recording}|SteamLoggedOn={SteamUser.BLoggedOn()}|CapturedFrames={_capturedFrames}");
    }
}
#endif
