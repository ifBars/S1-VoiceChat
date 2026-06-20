using System;

namespace S1VoiceChat.Codec;

public interface IVoiceCodec : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    int FrameSize { get; }

    int Encode(ReadOnlySpan<short> pcm, Span<byte> encodedOutput);
    int Decode(ReadOnlySpan<byte> encoded, Span<short> pcmOutput, bool useForwardErrorCorrection = false);
    int DecodePacketLoss(Span<short> pcmOutput);
}
