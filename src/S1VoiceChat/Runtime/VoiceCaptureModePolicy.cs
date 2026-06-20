namespace S1VoiceChat.Runtime;

public static class VoiceCaptureModePolicy
{
    public static bool IsOpenMicEnabled(bool forcedOpenMic, VoiceSettings settings)
    {
        return forcedOpenMic || settings.OpenMicEnabled;
    }

    public static bool ShouldCapture(bool forcedOpenMic, VoiceSettings settings, bool pushToTalkPressed)
    {
        return IsOpenMicEnabled(forcedOpenMic, settings) || pushToTalkPressed;
    }
}
