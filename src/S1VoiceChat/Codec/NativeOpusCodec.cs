using System;
using System.Runtime.InteropServices;

namespace S1VoiceChat.Codec;

public sealed class NativeOpusCodec : IVoiceCodec
{
    private const int OpusApplicationVoip = 2048;
    private const int OpusOk = 0;

    private IntPtr _encoder;
    private IntPtr _decoder;
    private bool _disposed;

    public int SampleRate { get; }
    public int Channels { get; }
    public int FrameSize { get; }

    public NativeOpusCodec(int sampleRate = 48000, int channels = 1, int frameSize = 960)
    {
        SampleRate = sampleRate;
        Channels = channels;
        FrameSize = frameSize;

        _encoder = OpusNative.opus_encoder_create(sampleRate, channels, OpusApplicationVoip, out var encError);
        if (encError != OpusOk || _encoder == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create Opus encoder. Error: {encError}");

        _decoder = OpusNative.opus_decoder_create(sampleRate, channels, out var decError);
        if (decError != OpusOk || _decoder == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create Opus decoder. Error: {decError}");
    }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> encodedOutput)
    {
        ThrowIfDisposed();
        if (pcm.Length < FrameSize * Channels)
            throw new ArgumentException($"PCM frame must contain at least {FrameSize * Channels} samples.", nameof(pcm));

        var pcmArray = pcm.Slice(0, FrameSize * Channels).ToArray();
        var outputArray = new byte[encodedOutput.Length];
        var result = OpusNative.opus_encode(_encoder, pcmArray, FrameSize, outputArray, outputArray.Length);

        if (result > 0)
            outputArray.AsSpan(0, result).CopyTo(encodedOutput);

        return result;
    }

    public int Decode(ReadOnlySpan<byte> encoded, Span<short> pcmOutput, bool useForwardErrorCorrection = false)
    {
        ThrowIfDisposed();
        if (encoded.IsEmpty)
            return DecodePacketLoss(pcmOutput);

        if (pcmOutput.Length < FrameSize * Channels)
            throw new ArgumentException($"PCM output must contain at least {FrameSize * Channels} samples.", nameof(pcmOutput));

        var encodedArray = encoded.ToArray();
        var pcmArray = new short[FrameSize * Channels];
        var result = OpusNative.opus_decode(_decoder, encodedArray, encodedArray.Length, pcmArray, FrameSize, useForwardErrorCorrection ? 1 : 0);

        if (result > 0)
            pcmArray.AsSpan(0, result * Channels).CopyTo(pcmOutput);

        return result;
    }

    public int DecodePacketLoss(Span<short> pcmOutput)
    {
        ThrowIfDisposed();
        if (pcmOutput.Length < FrameSize * Channels)
            throw new ArgumentException($"PCM output must contain at least {FrameSize * Channels} samples.", nameof(pcmOutput));

        var pcmArray = new short[FrameSize * Channels];
        var result = OpusNative.opus_decode_missing(_decoder, IntPtr.Zero, 0, pcmArray, FrameSize, 0);

        if (result > 0)
            pcmArray.AsSpan(0, result * Channels).CopyTo(pcmOutput);

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_encoder != IntPtr.Zero)
        {
            OpusNative.opus_encoder_destroy(_encoder);
            _encoder = IntPtr.Zero;
        }

        if (_decoder != IntPtr.Zero)
        {
            OpusNative.opus_decoder_destroy(_decoder);
            _decoder = IntPtr.Zero;
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NativeOpusCodec));
    }

    private static class OpusNative
    {
        private const string LibraryName = "opus";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opus_encoder_create(int Fs, int channels, int application, out int error);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encode(IntPtr st, short[] pcm, int frame_size, byte[] data, int max_data_bytes);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opus_encoder_destroy(IntPtr st);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opus_decoder_create(int Fs, int channels, out int error);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_decode")]
        public static extern int opus_decode(IntPtr st, byte[] data, int len, short[] pcm, int frame_size, int decode_fec);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_decode")]
        public static extern int opus_decode_missing(IntPtr st, IntPtr data, int len, short[] pcm, int frame_size, int decode_fec);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void opus_decoder_destroy(IntPtr st);
    }
}
