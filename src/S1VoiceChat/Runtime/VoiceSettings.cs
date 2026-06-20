namespace S1VoiceChat.Runtime;

using S1VoiceChat.Network;

public sealed class VoiceSettings
{
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 1;
    public int FrameSize { get; set; } = 960;
    public int MaxEncodedBytesPerFrame { get; set; } = VoicePacket.MaxPayloadBytes;
    public float OutputVolume { get; set; } = 1f;

    public float ProximityRangeMeters { get; set; } = 25f;
    public float WhisperRangeMeters { get; set; } = 6f;
    public float ShoutRangeMeters { get; set; } = 45f;

    public int JitterTargetPackets { get; set; } = 3;
    public int JitterMaxPackets { get; set; } = 10;
    public int MaxPacketsPerPeerPerSecond { get; set; } = 40;

    public bool PushToTalkEnabled { get; set; } = true;
    public bool OpenMicEnabled { get; set; } = false;
    public bool VoiceActivityEnabled { get; set; } = false;
    public bool ServerRelayEnabled { get; set; } = true;
    public bool DiagnosticLoggingEnabled { get; set; } = false;
}
