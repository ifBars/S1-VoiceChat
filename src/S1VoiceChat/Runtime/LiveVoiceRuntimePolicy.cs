namespace S1VoiceChat.Runtime;

public static class LiveVoiceRuntimePolicy
{
    public static bool IsVoiceScene(string? sceneName)
    {
        return sceneName == "Main" || sceneName == "Tutorial";
    }

    public static bool CanCreateLiveVoice(bool enabled, bool isBatchMode)
    {
        return enabled && !isBatchMode;
    }

    public static bool CanCreateInteractiveProbe(bool enabled, bool isBatchMode)
    {
        return enabled && !isBatchMode;
    }
}
