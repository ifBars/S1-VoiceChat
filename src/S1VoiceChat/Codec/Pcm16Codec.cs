using System;
using System.Buffers.Binary;

namespace S1VoiceChat.Codec;

public sealed class Pcm16Codec : IVoiceCodec
{
    private bool _disposed;

    public Pcm16Codec(int sampleRate = 16000, int channels = 1, int frameSize = 480)
    {
        SampleRate = sampleRate;
        Channels = channels;
        FrameSize = frameSize;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int FrameSize { get; }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> encodedOutput)
    {
        ThrowIfDisposed();

        var sampleCount = Math.Min(pcm.Length, FrameSize * Channels);
        var byteCount = sampleCount * sizeof(short);
        if (encodedOutput.Length < byteCount)
            throw new ArgumentException($"Encoded output must contain at least {byteCount} bytes.", nameof(encodedOutput));

        for (var i = 0; i < sampleCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(encodedOutput.Slice(i * sizeof(short), sizeof(short)), pcm[i]);

        return byteCount;
    }

    public int Decode(ReadOnlySpan<byte> encoded, Span<short> pcmOutput, bool useForwardErrorCorrection = false)
    {
        ThrowIfDisposed();

        var sampleCount = Math.Min(encoded.Length / sizeof(short), Math.Min(pcmOutput.Length, FrameSize * Channels));
        for (var i = 0; i < sampleCount; i++)
            pcmOutput[i] = BinaryPrimitives.ReadInt16LittleEndian(encoded.Slice(i * sizeof(short), sizeof(short)));

        return sampleCount / Channels;
    }

    public int DecodePacketLoss(Span<short> pcmOutput)
    {
        ThrowIfDisposed();

        var sampleCount = Math.Min(pcmOutput.Length, FrameSize * Channels);
        pcmOutput.Slice(0, sampleCount).Clear();
        return sampleCount / Channels;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Pcm16Codec));
    }
}
