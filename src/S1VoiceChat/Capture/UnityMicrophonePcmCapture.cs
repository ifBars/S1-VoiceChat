#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Globalization;
using System.Linq;
using MelonLoader;
using UnityEngine;

namespace S1VoiceChat.Capture;

public sealed class UnityMicrophonePcmCapture : IVoiceCapture, IVoiceCaptureDiagnostics
{
    private const int ClipLengthSeconds = 1;

    private readonly MelonLogger.Instance _logger;
    private readonly int _frameSamples;
    private readonly string? _preferredDevice;
    private readonly bool _diagnosticLoggingEnabled;
    private AudioClip? _clip;
    private string? _device;
    private bool _useDefaultDevice;
    private float[] _floatFrame;
    private int _sourceChannels = 1;
    private int _lastPosition;
    private int _lastReadPosition;
    private int _lastAvailableSourceSamples;
    private float _lastPeak;
    private bool _disposed;

    public UnityMicrophonePcmCapture(MelonLogger.Instance logger, int sampleRate = 16000, int channels = 1, int frameSamples = 480, string? preferredDevice = null, bool diagnosticLoggingEnabled = false)
    {
        _logger = logger;
        SampleRate = sampleRate;
        Channels = channels;
        _frameSamples = frameSamples;
        _preferredDevice = preferredDevice;
        _diagnosticLoggingEnabled = diagnosticLoggingEnabled;
        _floatFrame = new float[Math.Max(1, frameSamples * channels)];
    }

    public bool IsCapturing { get; private set; }
    public int SampleRate { get; }
    public int Channels { get; }
    public string DeviceLabel => GetDeviceLabel();
    public int LastReadPosition => _lastReadPosition;
    public int LastAvailableSourceSamples => _lastAvailableSourceSamples;
    public float LastPeak => _lastPeak;

    public string GetDiagnosticSummary()
    {
        return $"CaptureDevice={DeviceLabel}|CapturePosition={LastReadPosition}|AvailableSourceSamples={LastAvailableSourceSamples}|CapturePeak={LastPeak:0.000000}";
    }

    public void Start()
    {
        ThrowIfDisposed();

        if (IsCapturing)
            return;

        _device = ResolveDevice(out _useDefaultDevice);
        if (!_useDefaultDevice && string.IsNullOrEmpty(_device))
        {
            _logger.Warning("Unity microphone capture could not start because no microphone devices are available.");
            return;
        }

        Microphone.GetDeviceCaps(_device, out var minFrequency, out var maxFrequency);
        var requestedFrequency = ResolveFrequency(minFrequency, maxFrequency);
        _clip = Microphone.Start(_device, loop: true, lengthSec: ClipLengthSeconds, frequency: requestedFrequency);
        _sourceChannels = Math.Max(1, _clip.channels);
        _floatFrame = new float[Math.Max(_floatFrame.Length, _frameSamples * _sourceChannels)];
        _lastPosition = 0;
        _lastReadPosition = 0;
        _lastAvailableSourceSamples = 0;
        _lastPeak = 0f;
        IsCapturing = true;

        if (_diagnosticLoggingEnabled)
            _logger.Msg($"Unity microphone capture started. Device={GetDeviceLabel()}|Devices={string.Join(",", Microphone.devices)}|RequestedSampleRate={requestedFrequency}|ClipFrequency={_clip.frequency}|DeviceCaps={minFrequency}-{maxFrequency}|Channels={_sourceChannels}|FrameSamples={_frameSamples}");
    }

    public void Stop()
    {
        if (!IsCapturing)
            return;

        var device = _device;
        if (Microphone.IsRecording(device))
            Microphone.End(device);

        _clip = null;
        _device = null;
        _lastPosition = 0;
        _lastReadPosition = 0;
        _lastAvailableSourceSamples = 0;
        _lastPeak = 0f;
        IsCapturing = false;
        if (_diagnosticLoggingEnabled)
            _logger.Msg("Unity microphone capture stopped.");
    }

    public int ReadFrame(Span<short> destination)
    {
        if (!IsCapturing || _clip == null)
            return 0;

        var position = Microphone.GetPosition(_device);
        if (position < 0)
            return 0;

        _lastReadPosition = position;
        var sourceSamplesAvailable = GetAvailableSourceSamples(position);
        _lastAvailableSourceSamples = sourceSamplesAvailable;
        var destinationFrames = destination.Length / Math.Max(Channels, 1);
        if (sourceSamplesAvailable < destinationFrames)
            return 0;

        if (sourceSamplesAvailable > destinationFrames * 2)
            _lastPosition = (position - destinationFrames + _clip.samples) % _clip.samples;

        ReadSourceSamples(destinationFrames);
        ConvertToPcm(destination, destinationFrames);

        _lastPosition = (_lastPosition + destinationFrames) % _clip.samples;
        return destinationFrames * Channels;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }

    private string? ResolveDevice(out bool useDefaultDevice)
    {
        useDefaultDevice = false;
        if (string.Equals(_preferredDevice, "default", StringComparison.OrdinalIgnoreCase))
        {
            useDefaultDevice = true;
            return null;
        }

        if (int.TryParse(_preferredDevice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceIndex) &&
            deviceIndex >= 0 &&
            deviceIndex < Microphone.devices.Length)
        {
            return Microphone.devices[deviceIndex];
        }

        if (!string.IsNullOrWhiteSpace(_preferredDevice) && Microphone.devices.Contains(_preferredDevice))
            return _preferredDevice!;

        return Microphone.devices.FirstOrDefault() ?? string.Empty;
    }

    private string GetDeviceLabel()
    {
        return _useDefaultDevice ? "<Unity default>" : _device ?? string.Empty;
    }

    private int ResolveFrequency(int minFrequency, int maxFrequency)
    {
        if (minFrequency <= 0 && maxFrequency <= 0)
            return SampleRate;

        if (maxFrequency > 0 && SampleRate > maxFrequency)
            return maxFrequency;

        if (minFrequency > 0 && SampleRate < minFrequency)
            return minFrequency;

        return SampleRate;
    }

    private int GetAvailableSourceSamples(int position)
    {
        if (_clip == null)
            return 0;

        return position >= _lastPosition
            ? position - _lastPosition
            : position + _clip.samples - _lastPosition;
    }

    private void ReadSourceSamples(int frames)
    {
        if (_clip == null)
            return;

        var values = frames * _sourceChannels;
        if (_floatFrame.Length < values)
            _floatFrame = new float[values];

        if (_lastPosition + frames <= _clip.samples)
        {
            _clip.GetData(_floatFrame, _lastPosition);
            return;
        }

        var firstFrames = _clip.samples - _lastPosition;
        var firstValues = firstFrames * _sourceChannels;
        var secondFrames = frames - firstFrames;
        var secondValues = secondFrames * _sourceChannels;

        var first = new float[firstValues];
        var second = new float[secondValues];
        _clip.GetData(first, _lastPosition);
        _clip.GetData(second, 0);
        Array.Copy(first, 0, _floatFrame, 0, firstValues);
        Array.Copy(second, 0, _floatFrame, firstValues, secondValues);
    }

    private void ConvertToPcm(Span<short> destination, int frames)
    {
        var destinationIndex = 0;
        var peak = 0f;
        for (var frame = 0; frame < frames; frame++)
        {
            var sourceIndex = frame * _sourceChannels;
            var sample = _floatFrame[sourceIndex];
            if (_sourceChannels > 1)
            {
                var mixed = 0f;
                for (var channel = 0; channel < _sourceChannels; channel++)
                    mixed += _floatFrame[sourceIndex + channel];

                sample = mixed / _sourceChannels;
            }

            peak = Math.Max(peak, Math.Abs(sample));
            var pcm = FloatToPcm(sample);
            for (var channel = 0; channel < Channels; channel++)
                destination[destinationIndex++] = pcm;
        }

        _lastPeak = peak;
    }

    private static short FloatToPcm(float sample)
    {
        var clamped = Mathf.Clamp(sample, -1f, 1f);
        return (short)Mathf.RoundToInt(clamped * short.MaxValue);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnityMicrophonePcmCapture));
    }
}
#endif
