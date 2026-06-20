using System;

namespace S1VoiceChat.Capture;

public sealed class SyntheticToneCapture : IVoiceCapture, IVoiceCaptureDiagnostics
{
    private readonly int _frameSamples;
    private readonly double _phaseIncrement;
    private double _phase;
    private bool _disposed;

    public SyntheticToneCapture(int sampleRate = 48000, int channels = 1, int frameSamples = 480, double frequencyHz = 440.0, float amplitude = 0.35f)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _frameSamples = frameSamples;
        FrequencyHz = frequencyHz;
        Amplitude = Math.Clamp(amplitude, 0f, 1f);
        _phaseIncrement = Math.PI * 2.0 * frequencyHz / sampleRate;
    }

    public bool IsCapturing { get; private set; }
    public int SampleRate { get; }
    public int Channels { get; }
    public double FrequencyHz { get; }
    public float Amplitude { get; }
    public string DeviceLabel => $"SyntheticTone({FrequencyHz:0.#}Hz)";
    public int LastReadPosition { get; private set; }
    public int LastAvailableSourceSamples { get; private set; }
    public float LastPeak { get; private set; }

    public string GetDiagnosticSummary()
    {
        return $"CaptureDevice={DeviceLabel}|CapturePosition={LastReadPosition}|AvailableSourceSamples={LastAvailableSourceSamples}|CapturePeak={LastPeak:0.000000}";
    }

    public void Start()
    {
        ThrowIfDisposed();
        IsCapturing = true;
        LastReadPosition = 0;
        LastAvailableSourceSamples = _frameSamples;
        LastPeak = 0f;
    }

    public void Stop()
    {
        IsCapturing = false;
        LastReadPosition = 0;
        LastAvailableSourceSamples = 0;
        LastPeak = 0f;
    }

    public int ReadFrame(Span<short> destination)
    {
        ThrowIfDisposed();
        if (!IsCapturing)
            return 0;

        var frames = Math.Min(_frameSamples, destination.Length / Math.Max(Channels, 1));
        var destinationIndex = 0;
        var peak = 0f;
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (float)(Math.Sin(_phase) * Amplitude);
            _phase += _phaseIncrement;
            if (_phase > Math.PI * 2.0)
                _phase -= Math.PI * 2.0;

            peak = Math.Max(peak, Math.Abs(sample));
            var pcm = (short)Math.Round(sample * short.MaxValue);
            for (var channel = 0; channel < Channels; channel++)
                destination[destinationIndex++] = pcm;
        }

        LastPeak = peak;
        LastAvailableSourceSamples = frames;
        LastReadPosition += frames;
        return frames * Channels;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SyntheticToneCapture));
    }
}
