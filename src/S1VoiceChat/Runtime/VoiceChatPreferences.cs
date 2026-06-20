#if S1VOICECHAT_STEAMNETWORKLIB
using MelonLoader;
using UnityEngine;

namespace S1VoiceChat.Runtime;

internal static class VoiceChatPreferences
{
    public const float MinVolumePercent = 0f;
    public const float MaxVolumePercent = 100f;
    public const float DefaultVolumePercent = 100f;

    private static MelonPreferences_Category? _category;
    private static MelonPreferences_Entry<float>? _outputVolumePercent;
    private static MelonPreferences_Entry<bool>? _openMicEnabled;
    private static MelonPreferences_Entry<bool>? _diagnosticLoggingEnabled;

    public static float OutputVolumePercent => ClampPercent(_outputVolumePercent?.Value ?? DefaultVolumePercent);

    public static float OutputVolume => OutputVolumePercent * 0.01f;

    public static bool OpenMicEnabled => _openMicEnabled?.Value ?? false;

    public static bool DiagnosticLoggingEnabled => _diagnosticLoggingEnabled?.Value ?? false;

    public static void Initialize()
    {
        if (_category != null)
            return;

        _category = MelonPreferences.CreateCategory("S1VoiceChat", "S1 Voice Chat");
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
        _diagnosticLoggingEnabled = _category.CreateEntry(
            "DiagnosticLogging",
            false,
            "Diagnostic Logging",
            "Write verbose S1 VoiceChat capture and transport diagnostics to MelonLoader logs.");
        _outputVolumePercent.Value = OutputVolumePercent;
        _category.SaveToFile(false);
    }

    public static void ApplyTo(VoiceSettings settings)
    {
        settings.OutputVolume = OutputVolume;
        settings.OpenMicEnabled = OpenMicEnabled;
        settings.PushToTalkEnabled = !settings.OpenMicEnabled;
        settings.DiagnosticLoggingEnabled = DiagnosticLoggingEnabled;
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
}
#endif
