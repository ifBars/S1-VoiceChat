#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using MelonLoader;
using UnityEngine;

namespace S1VoiceChat.Runtime;

internal static class VoiceChatPreferences
{
    public const float MinVolumePercent = 0f;
    public const float MaxVolumePercent = 100f;
    public const float DefaultVolumePercent = 100f;
    public const int MinOpusBitrate = 6000;
    public const int MaxOpusBitrate = 64000;

    private static MelonPreferences_Category? _category;
    private static MelonPreferences_Entry<bool>? _liveVoiceEnabled;
    private static MelonPreferences_Entry<float>? _outputVolumePercent;
    private static MelonPreferences_Entry<bool>? _openMicEnabled;
    private static MelonPreferences_Entry<string>? _pushToTalkKey;
    private static MelonPreferences_Entry<string>? _voiceChannel;
    private static MelonPreferences_Entry<string>? _captureSource;
    private static MelonPreferences_Entry<string>? _microphoneDevice;
    private static MelonPreferences_Entry<string>? _codec;
    private static MelonPreferences_Entry<int>? _opusBitrate;
    private static MelonPreferences_Entry<float>? _proximityRangeMeters;
    private static MelonPreferences_Entry<float>? _whisperRangeMeters;
    private static MelonPreferences_Entry<float>? _shoutRangeMeters;
    private static MelonPreferences_Entry<bool>? _diagnosticLoggingEnabled;

    public static bool LiveVoiceEnabled => _liveVoiceEnabled?.Value ?? true;

    public static float OutputVolumePercent => ClampPercent(_outputVolumePercent?.Value ?? DefaultVolumePercent);

    public static float OutputVolume => OutputVolumePercent * 0.01f;

    public static bool OpenMicEnabled => _openMicEnabled?.Value ?? false;

    public static string PushToTalkKey => NormalizeText(_pushToTalkKey?.Value, "V");

    public static string VoiceChannel => NormalizeText(_voiceChannel?.Value, "Proximity");

    public static string CaptureSource => NormalizeText(_captureSource?.Value, "Wasapi");

    public static string MicrophoneDevice => NormalizeText(_microphoneDevice?.Value, "auto");

    public static string Codec => NormalizeText(_codec?.Value, "Opus");

    public static int OpusBitrate => ClampInt(_opusBitrate?.Value ?? 24000, MinOpusBitrate, MaxOpusBitrate);

    public static float ProximityRangeMeters => ClampRange(_proximityRangeMeters?.Value ?? 25f, 1f, 200f);

    public static float WhisperRangeMeters => ClampRange(_whisperRangeMeters?.Value ?? 6f, 1f, 200f);

    public static float ShoutRangeMeters => ClampRange(_shoutRangeMeters?.Value ?? 45f, 1f, 200f);

    public static bool DiagnosticLoggingEnabled => _diagnosticLoggingEnabled?.Value ?? false;

    public static void Initialize()
    {
        if (_category != null)
            return;

        _category = MelonPreferences.CreateCategory("S1VoiceChat", "S1 Voice Chat");
        _liveVoiceEnabled = _category.CreateEntry(
            "Enabled",
            true,
            "Enable Voice Chat",
            "Enable live voice chat in Main and Tutorial scenes. Batch/headless processes remain disabled.");
        _outputVolumePercent = _category.CreateEntry(
            "OutputVolume",
            DefaultVolumePercent,
            "Output Volume",
            "Received voice chat playback volume.");
        _openMicEnabled = _category.CreateEntry(
            "OpenMic",
            false,
            "Open Mic",
            "Transmit continuously while live voice is active instead of using push-to-talk.");
        _pushToTalkKey = _category.CreateEntry(
            "PushToTalkKey",
            "V",
            "Push To Talk Key",
            "Unity KeyCode name used for push-to-talk, for example V, Mouse4, or LeftAlt.");
        _voiceChannel = _category.CreateEntry(
            "VoiceChannel",
            "Proximity",
            "Voice Channel",
            "Default voice routing channel: Proximity, Whisper, Shout, Radio, or Global.");
        _captureSource = _category.CreateEntry(
            "CaptureSource",
            "Wasapi",
            "Capture Source",
            "Microphone capture backend: Wasapi, Microphone, or Tone. Wasapi is the most reliable Windows default.");
        _microphoneDevice = _category.CreateEntry(
            "MicrophoneDevice",
            "auto",
            "Microphone Device",
            "Capture device name, index, default, auto, or blank for the first Unity microphone.");
        _codec = _category.CreateEntry(
            "Codec",
            "Opus",
            "Voice Codec",
            "Voice codec: Opus for production or Pcm16 as a compatibility fallback.");
        _opusBitrate = _category.CreateEntry(
            "OpusBitrate",
            24000,
            "Opus Bitrate",
            "Opus voice bitrate in bits per second.");
        _proximityRangeMeters = _category.CreateEntry(
            "ProximityRangeMeters",
            25f,
            "Proximity Range",
            "Maximum distance for Proximity voice.");
        _whisperRangeMeters = _category.CreateEntry(
            "WhisperRangeMeters",
            6f,
            "Whisper Range",
            "Maximum distance for Whisper voice.");
        _shoutRangeMeters = _category.CreateEntry(
            "ShoutRangeMeters",
            45f,
            "Shout Range",
            "Maximum distance for Shout voice.");
        _diagnosticLoggingEnabled = _category.CreateEntry(
            "DiagnosticLogging",
            false,
            "Diagnostic Logging",
            "Write verbose S1 VoiceChat capture and transport diagnostics to MelonLoader logs.");
        _outputVolumePercent.Value = OutputVolumePercent;
        _opusBitrate.Value = OpusBitrate;
        _proximityRangeMeters.Value = ProximityRangeMeters;
        _whisperRangeMeters.Value = WhisperRangeMeters;
        _shoutRangeMeters.Value = ShoutRangeMeters;
        _category.SaveToFile(false);
    }

    public static void ApplyStartupTo(VoiceSettings settings)
    {
        settings.OpusBitrate = OpusBitrate;
        ApplyRuntimeTo(settings);
    }

    public static void ApplyTo(VoiceSettings settings)
    {
        ApplyRuntimeTo(settings);
        settings.OpenMicEnabled = OpenMicEnabled;
        settings.PushToTalkEnabled = !settings.OpenMicEnabled;
        settings.DiagnosticLoggingEnabled = DiagnosticLoggingEnabled;
    }

    private static void ApplyRuntimeTo(VoiceSettings settings)
    {
        settings.OutputVolume = OutputVolume;
        settings.ProximityRangeMeters = ProximityRangeMeters;
        settings.WhisperRangeMeters = WhisperRangeMeters;
        settings.ShoutRangeMeters = ShoutRangeMeters;
    }

    public static void SetOutputVolumePercent(float value)
    {
        Initialize();
        if (_outputVolumePercent == null)
            return;

        _outputVolumePercent.Value = ClampPercent(value);
        _category?.SaveToFile(false);
    }

    public static void SetOpenMicEnabled(bool value)
    {
        Initialize();
        if (_openMicEnabled == null)
            return;

        _openMicEnabled.Value = value;
        _category?.SaveToFile(false);
    }

    public static void SetDiagnosticLoggingEnabled(bool value)
    {
        Initialize();
        if (_diagnosticLoggingEnabled == null)
            return;

        _diagnosticLoggingEnabled.Value = value;
        _category?.SaveToFile(false);
    }

    private static float ClampPercent(float value)
    {
        return Mathf.Clamp(value, MinVolumePercent, MaxVolumePercent);
    }

    private static float ClampRange(float value, float min, float max)
    {
        return Mathf.Clamp(value, min, max);
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
#endif
