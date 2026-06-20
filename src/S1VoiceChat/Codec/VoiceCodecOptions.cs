namespace S1VoiceChat.Codec;

public sealed class VoiceCodecOptions
{
    public const int DefaultOpusBitrate = 24000;
    public const int DefaultOpusComplexity = 5;
    public const int DefaultOpusExpectedPacketLossPercent = 5;

    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 1;
    public int FrameSize { get; set; } = 480;
    public int OpusBitrate { get; set; } = DefaultOpusBitrate;
    public int OpusComplexity { get; set; } = DefaultOpusComplexity;
    public int OpusExpectedPacketLossPercent { get; set; } = DefaultOpusExpectedPacketLossPercent;
    public bool OpusInbandFecEnabled { get; set; } = true;
    public bool OpusDtxEnabled { get; set; } = false;
}
