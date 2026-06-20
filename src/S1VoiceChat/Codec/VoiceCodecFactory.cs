using System;

namespace S1VoiceChat.Codec;

public static class VoiceCodecFactory
{
    public static IVoiceCodec Create(VoiceCodecKind kind, VoiceCodecOptions options)
    {
        return kind switch
        {
            VoiceCodecKind.Opus => new NativeOpusCodec(options),
            VoiceCodecKind.Pcm16 => new Pcm16Codec(options.SampleRate, options.Channels, options.FrameSize),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported voice codec.")
        };
    }

    public static bool TryParse(string? value, out VoiceCodecKind kind)
    {
        if (string.Equals(value, "pcm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "pcm16", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "raw", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceCodecKind.Pcm16;
            return true;
        }

        if (string.Equals(value, "opus", StringComparison.OrdinalIgnoreCase))
        {
            kind = VoiceCodecKind.Opus;
            return true;
        }

        kind = VoiceCodecKind.Opus;
        return false;
    }
}
