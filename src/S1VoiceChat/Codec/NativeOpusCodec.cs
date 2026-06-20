using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace S1VoiceChat.Codec;

public sealed class NativeOpusCodec : IVoiceCodec
{
    private const int OpusApplicationVoip = 2048;
    private const int OpusOk = 0;
    private const int OpusSetBitrateRequest = 4002;
    private const int OpusSetVbrRequest = 4006;
    private const int OpusSetComplexityRequest = 4010;
    private const int OpusSetInbandFecRequest = 4012;
    private const int OpusSetPacketLossPercRequest = 4014;
    private const int OpusSetDtxRequest = 4016;
    private const int MinOpusBitrate = 6000;
    private const int MaxOpusBitrate = 128000;
    private static bool _loadAttempted;

    private IntPtr _encoder;
    private IntPtr _decoder;
    private bool _disposed;

    public int SampleRate { get; }
    public int Channels { get; }
    public int FrameSize { get; }

    public NativeOpusCodec(int sampleRate = 48000, int channels = 1, int frameSize = 960)
        : this(new VoiceCodecOptions
        {
            SampleRate = sampleRate,
            Channels = channels,
            FrameSize = frameSize
        })
    {
    }

    public NativeOpusCodec(VoiceCodecOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        EnsureNativeLibraryLoaded();
        ValidateOpusFrame(options.SampleRate, options.FrameSize);

        SampleRate = options.SampleRate;
        Channels = options.Channels;
        FrameSize = options.FrameSize;

        _encoder = OpusNative.opus_encoder_create(SampleRate, Channels, OpusApplicationVoip, out var encError);
        if (encError != OpusOk || _encoder == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create Opus encoder. Error: {encError}");

        ConfigureEncoder(options);

        _decoder = OpusNative.opus_decoder_create(SampleRate, Channels, out var decError);
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
        if (result < 0)
            throw new InvalidOperationException($"Opus encode failed. Error: {result}");

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
        if (result < 0)
            throw new InvalidOperationException($"Opus decode failed. Error: {result}");

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
        if (result < 0)
        {
            pcmOutput.Slice(0, Math.Min(pcmOutput.Length, FrameSize * Channels)).Clear();
            return Math.Min(pcmOutput.Length, FrameSize * Channels) / Channels;
        }

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

    private void ConfigureEncoder(VoiceCodecOptions options)
    {
        var bitrate = Math.Clamp(options.OpusBitrate, MinOpusBitrate, MaxOpusBitrate);
        TrySetEncoderControl(OpusSetBitrateRequest, bitrate);
        TrySetEncoderControl(OpusSetVbrRequest, 1);
        TrySetEncoderControl(OpusSetComplexityRequest, Math.Clamp(options.OpusComplexity, 0, 10));
        TrySetEncoderControl(OpusSetInbandFecRequest, options.OpusInbandFecEnabled ? 1 : 0);
        TrySetEncoderControl(OpusSetPacketLossPercRequest, Math.Clamp(options.OpusExpectedPacketLossPercent, 0, 100));
        TrySetEncoderControl(OpusSetDtxRequest, options.OpusDtxEnabled ? 1 : 0);
    }

    private void TrySetEncoderControl(int request, int value)
    {
        var result = OpusNative.opus_encoder_ctl(_encoder, request, value);
        if (result != OpusOk)
            throw new InvalidOperationException($"Failed to configure Opus encoder control {request}. Error: {result}");
    }

    private static void ValidateOpusFrame(int sampleRate, int frameSize)
    {
        if (sampleRate is not (8000 or 12000 or 16000 or 24000 or 48000))
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Opus supports 8000, 12000, 16000, 24000, or 48000 Hz.");

        var validFrameSizes = new[]
        {
            sampleRate / 400,
            sampleRate / 200,
            sampleRate / 100,
            sampleRate / 50,
            sampleRate / 25,
            sampleRate * 3 / 50
        };
        if (Array.IndexOf(validFrameSizes, frameSize) < 0)
            throw new ArgumentOutOfRangeException(nameof(frameSize), frameSize, "Opus frame size must be 2.5, 5, 10, 20, 40, or 60 ms.");
    }

    private static void EnsureNativeLibraryLoaded()
    {
        if (_loadAttempted)
            return;

        _loadAttempted = true;
        foreach (var path in GetNativeLibraryCandidates())
        {
            if (!File.Exists(path))
                continue;

            if (OpusNative.LoadLibrary(path) != IntPtr.Zero)
                return;
        }
    }

    private static string[] GetNativeLibraryCandidates()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        var currentDir = Environment.CurrentDirectory;
        return new[]
        {
            Path.Combine(AppContext.BaseDirectory, "opus.dll"),
            Path.Combine(currentDir, "opus.dll"),
            Path.Combine(currentDir, "UserLibs", "opus.dll"),
            Path.Combine(assemblyDir, "opus.dll"),
            Path.Combine(assemblyDir, "S1VoiceChat", "opus.dll"),
            Path.Combine(currentDir, "Mods", "S1VoiceChat", "opus.dll")
        };
    }

    private static class OpusNative
    {
        private const string LibraryName = "opus";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opus_encoder_create(int Fs, int channels, int application, out int error);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encode(IntPtr st, short[] pcm, int frame_size, byte[] data, int max_data_bytes);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encoder_ctl(IntPtr st, int request, int value);

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

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibrary(string lpFileName);
    }
}
