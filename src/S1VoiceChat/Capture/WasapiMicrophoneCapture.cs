#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using MelonLoader;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace S1VoiceChat.Capture;

public sealed class WasapiMicrophoneCapture : IVoiceCapture, IVoiceCaptureDiagnostics
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private const int AutoProbeMilliseconds = 650;

    private readonly MelonLogger.Instance _logger;
    private readonly int _frameSamples;
    private readonly string? _preferredDevice;
    private readonly bool _diagnosticLoggingEnabled;
    private readonly Queue<short> _samples = new();
    private readonly object _sync = new();
    private string? _resolvedAutoDeviceId;
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private WaveFormat? _format;
    private int _sourceChannels = 1;
    private int _sourceSampleRate;
    private int _lastReadPosition;
    private int _lastAvailableSourceSamples;
    private float _lastPeak;
    private long _dataEvents;
    private long _dataBytes;
    private long _sourceFrames;
    private int _unsupportedEvents;
    private bool _disposed;

    public WasapiMicrophoneCapture(MelonLogger.Instance logger, int sampleRate = 48000, int channels = 1, int frameSamples = 480, string? preferredDevice = null, bool diagnosticLoggingEnabled = false)
    {
        _logger = logger;
        SampleRate = sampleRate;
        Channels = channels;
        _frameSamples = frameSamples;
        _preferredDevice = preferredDevice;
        _diagnosticLoggingEnabled = diagnosticLoggingEnabled;
    }

    public bool IsCapturing { get; private set; }
    public int SampleRate { get; }
    public int Channels { get; }
    public string DeviceLabel => _device?.FriendlyName ?? "<none>";
    public int LastReadPosition => _lastReadPosition;
    public int LastAvailableSourceSamples => _lastAvailableSourceSamples;
    public float LastPeak => _lastPeak;

    public string GetDiagnosticSummary()
    {
        return $"CaptureDevice={DeviceLabel}|CapturePosition={LastReadPosition}|AvailableSourceSamples={LastAvailableSourceSamples}|CapturePeak={LastPeak:0.000000}|WasapiEvents={_dataEvents}|WasapiBytes={_dataBytes}|WasapiSourceFrames={_sourceFrames}|WasapiUnsupported={_unsupportedEvents}|EndpointVolume={GetEndpointVolume():0.000}|EndpointMuted={GetEndpointMuted()}|EndpointMeter={GetEndpointMeter():0.000000}";
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (IsCapturing)
            return;

        _enumerator = new MMDeviceEnumerator();
        _device = ResolveDevice(_enumerator);
        if (_device == null)
        {
            _logger.Warning("WASAPI capture could not start because no active capture endpoints are available.");
            return;
        }

        _capture = new WasapiCapture(_device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _format = _capture.WaveFormat;
        _sourceChannels = Math.Max(1, _format.Channels);
        _sourceSampleRate = _format.SampleRate;

        lock (_sync)
            _samples.Clear();

        _lastReadPosition = 0;
        _lastAvailableSourceSamples = 0;
        _lastPeak = 0f;
        _dataEvents = 0;
        _dataBytes = 0;
        _sourceFrames = 0;
        _unsupportedEvents = 0;
        _capture.StartRecording();
        IsCapturing = true;

        if (_diagnosticLoggingEnabled)
        {
            var devices = string.Join(",", EnumerateActiveDevices(_enumerator).Select(device => device.FriendlyName));
            var subFormat = _format is WaveFormatExtensible extensible ? extensible.SubFormat.ToString() : string.Empty;
            _logger.Msg($"WASAPI microphone capture started. Device={_device.FriendlyName}|DeviceId={_device.ID}|Devices={devices}|SampleRate={_sourceSampleRate}|Bits={_format.BitsPerSample}|Encoding={_format.Encoding}|SubFormat={subFormat}|Channels={_sourceChannels}|FrameSamples={_frameSamples}|EndpointVolume={GetEndpointVolume():0.000}|EndpointMuted={GetEndpointMuted()}|EndpointMeter={GetEndpointMeter():0.000000}");
        }
    }

    public void Stop()
    {
        if (!IsCapturing && _capture == null)
            return;

        var capture = _capture;
        if (capture != null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try
            {
                capture.StopRecording();
            }
            catch
            {
                // StopRecording can throw if the Windows audio client already stopped.
            }

            capture.Dispose();
        }

        _capture = null;
        _format = null;
        _device?.Dispose();
        _device = null;
        _enumerator?.Dispose();
        _enumerator = null;
        lock (_sync)
            _samples.Clear();

        _lastReadPosition = 0;
        _lastAvailableSourceSamples = 0;
        _lastPeak = 0f;
        _dataEvents = 0;
        _dataBytes = 0;
        _sourceFrames = 0;
        _unsupportedEvents = 0;
        IsCapturing = false;
        if (_diagnosticLoggingEnabled)
            _logger.Msg("WASAPI microphone capture stopped.");
    }

    public int ReadFrame(Span<short> destination)
    {
        if (!IsCapturing)
            return 0;

        var destinationFrames = destination.Length / Math.Max(Channels, 1);
        var requiredSamples = destinationFrames * Channels;
        lock (_sync)
        {
            _lastAvailableSourceSamples = _samples.Count / Math.Max(Channels, 1);
            if (_samples.Count < requiredSamples)
                return 0;

            while (_samples.Count > requiredSamples * 4)
                _ = _samples.Dequeue();

            var peak = 0f;
            for (var i = 0; i < requiredSamples; i++)
            {
                var sample = _samples.Dequeue();
                destination[i] = sample;
                peak = Math.Max(peak, Math.Abs(sample / 32768f));
            }

            _lastPeak = peak;
            _lastReadPosition += destinationFrames;
            _lastAvailableSourceSamples = _samples.Count / Math.Max(Channels, 1);
            return requiredSamples;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }

    private MMDevice? ResolveDevice(MMDeviceEnumerator enumerator)
    {
        var devices = EnumerateActiveDevices(enumerator).ToArray();
        if (devices.Length == 0)
            return null;

        if (string.Equals(_preferredDevice, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(_resolvedAutoDeviceId))
            {
                var cached = devices.FirstOrDefault(device => string.Equals(device.ID, _resolvedAutoDeviceId, StringComparison.Ordinal));
                if (cached != null)
                    return cached;
            }

            return ResolveAutoDevice(devices);
        }

        if (string.Equals(_preferredDevice, "default", StringComparison.OrdinalIgnoreCase))
            return ResolveDefaultDevice(enumerator) ?? devices[0];

        if (int.TryParse(_preferredDevice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 &&
            index < devices.Length)
        {
            return devices[index];
        }

        if (!string.IsNullOrWhiteSpace(_preferredDevice))
        {
            var exact = devices.FirstOrDefault(device => string.Equals(device.FriendlyName, _preferredDevice, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var contains = devices.FirstOrDefault(device => device.FriendlyName.IndexOf(_preferredDevice, StringComparison.OrdinalIgnoreCase) >= 0);
            if (contains != null)
                return contains;
        }

        return devices[0];
    }

    private MMDevice ResolveAutoDevice(IReadOnlyList<MMDevice> devices)
    {
        AutoProbeResult? best = null;
        foreach (var device in devices)
        {
            var result = ProbeDevice(device);
            if (_diagnosticLoggingEnabled)
                _logger.Msg($"WASAPI auto probe. Device={device.FriendlyName}|Events={result.Events}|Bytes={result.Bytes}|Frames={result.Frames}|Energy={result.Energy}|Peak={result.Peak:0.000000}|Unsupported={result.UnsupportedEvents}|EndpointVolume={GetEndpointVolume(device):0.000}|EndpointMuted={GetEndpointMuted(device)}|EndpointMeter={GetEndpointMeter(device):0.000000}");

            if (best == null || result.Energy > best.Energy || result.Energy == best.Energy && result.Peak > best.Peak)
                best = result;
        }

        if (best is { Energy: > 0 })
        {
            _resolvedAutoDeviceId = best.Device.ID;
            if (_diagnosticLoggingEnabled)
                _logger.Msg($"WASAPI auto selected capture device. Device={best.Device.FriendlyName}|Energy={best.Energy}|Peak={best.Peak:0.000000}");
            else
                _logger.Msg($"WASAPI auto selected capture device. Device={best.Device.FriendlyName}");

            return best.Device;
        }

        var fallback = ResolveDefaultDevice(_enumerator!) ?? devices[0];
        _resolvedAutoDeviceId = fallback.ID;
        _logger.Warning($"WASAPI auto probe found no nonzero PCM. Falling back to {fallback.FriendlyName}.");
        return fallback;
    }

    private AutoProbeResult ProbeDevice(MMDevice device)
    {
        var result = new AutoProbeResult(device);
        WasapiCapture? capture = null;
        try
        {
            capture = new WasapiCapture(device);
            capture.DataAvailable += (_, args) => result.Add(args.Buffer, args.BytesRecorded, capture.WaveFormat);
            capture.StartRecording();
            Thread.Sleep(AutoProbeMilliseconds);
            capture.StopRecording();
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.Warning($"WASAPI auto probe failed. Device={device.FriendlyName}|Error={ex.Message}");
        }
        finally
        {
            capture?.Dispose();
        }

        return result;
    }

    private static MMDevice? ResolveDefaultDevice(MMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch
            {
                return null;
            }
        }
    }

    private static IEnumerable<MMDevice> EnumerateActiveDevices(MMDeviceEnumerator enumerator)
    {
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (_format == null || args.BytesRecorded <= 0)
            return;

        _dataEvents++;
        _dataBytes += args.BytesRecorded;
        if (_format.BitsPerSample == 32 && IsFloatFormat(_format))
        {
            EnqueueFloat32(args.Buffer, args.BytesRecorded);
            return;
        }

        if (_format.BitsPerSample == 16)
        {
            EnqueuePcm16(args.Buffer, args.BytesRecorded);
            return;
        }

        _unsupportedEvents++;
        _logger.Warning($"Unsupported WASAPI capture format. Encoding={_format.Encoding}|Bits={_format.BitsPerSample}");
    }

    private void EnqueueFloat32(byte[] buffer, int bytesRecorded)
    {
        var frameCount = bytesRecorded / (sizeof(float) * _sourceChannels);
        _sourceFrames += frameCount;
        lock (_sync)
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var mixed = 0f;
                for (var channel = 0; channel < _sourceChannels; channel++)
                {
                    var offset = (frame * _sourceChannels + channel) * sizeof(float);
                    mixed += BitConverter.ToSingle(buffer, offset);
                }

                EnqueueMonoSample(mixed / _sourceChannels);
            }
        }
    }

    private void EnqueuePcm16(byte[] buffer, int bytesRecorded)
    {
        var frameCount = bytesRecorded / (sizeof(short) * _sourceChannels);
        _sourceFrames += frameCount;
        lock (_sync)
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var mixed = 0f;
                for (var channel = 0; channel < _sourceChannels; channel++)
                {
                    var offset = (frame * _sourceChannels + channel) * sizeof(short);
                    mixed += BitConverter.ToInt16(buffer, offset) / 32768f;
                }

                EnqueueMonoSample(mixed / _sourceChannels);
            }
        }
    }

    private void EnqueueMonoSample(float sample)
    {
        var pcm = FloatToPcm(sample);
        for (var channel = 0; channel < Channels; channel++)
            _samples.Enqueue(pcm);
    }

    private static bool IsFloatFormat(WaveFormat format)
    {
        return format.Encoding == WaveFormatEncoding.IeeeFloat ||
               format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat;
    }

    private static short FloatToPcm(float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        return (short)Math.Round(clamped * short.MaxValue);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception != null)
            _logger.Warning($"WASAPI microphone capture stopped unexpectedly: {args.Exception.Message}");
    }

    private float GetEndpointVolume()
    {
        return _device == null ? 0f : GetEndpointVolume(_device);
    }

    private bool GetEndpointMuted()
    {
        return _device != null && GetEndpointMuted(_device);
    }

    private float GetEndpointMeter()
    {
        return _device == null ? 0f : GetEndpointMeter(_device);
    }

    private static float GetEndpointVolume(MMDevice device)
    {
        try
        {
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return 0f;
        }
    }

    private static bool GetEndpointMuted(MMDevice device)
    {
        try
        {
            return device.AudioEndpointVolume.Mute;
        }
        catch
        {
            return false;
        }
    }

    private static float GetEndpointMeter(MMDevice device)
    {
        try
        {
            return device.AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            return 0f;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WasapiMicrophoneCapture));
    }

    private sealed class AutoProbeResult
    {
        public AutoProbeResult(MMDevice device)
        {
            Device = device;
        }

        public MMDevice Device { get; }
        public int Events { get; private set; }
        public long Bytes { get; private set; }
        public long Frames { get; private set; }
        public long Energy { get; private set; }
        public float Peak { get; private set; }
        public int UnsupportedEvents { get; private set; }
        public string? Error { get; set; }

        public void Add(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            Events++;
            Bytes += bytesRecorded;

            if (format.BitsPerSample == 32 && IsFloatFormat(format))
            {
                AddFloat32(buffer, bytesRecorded, Math.Max(1, format.Channels));
                return;
            }

            if (format.BitsPerSample == 16)
            {
                AddPcm16(buffer, bytesRecorded, Math.Max(1, format.Channels));
                return;
            }

            UnsupportedEvents++;
        }

        private void AddFloat32(byte[] buffer, int bytesRecorded, int channels)
        {
            var frameCount = bytesRecorded / (sizeof(float) * channels);
            Frames += frameCount;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var mixed = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var offset = (frame * channels + channel) * sizeof(float);
                    mixed += BitConverter.ToSingle(buffer, offset);
                }

                AddSample(mixed / channels);
            }
        }

        private void AddPcm16(byte[] buffer, int bytesRecorded, int channels)
        {
            var frameCount = bytesRecorded / (sizeof(short) * channels);
            Frames += frameCount;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var mixed = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var offset = (frame * channels + channel) * sizeof(short);
                    mixed += BitConverter.ToInt16(buffer, offset) / 32768f;
                }

                AddSample(mixed / channels);
            }
        }

        private void AddSample(float sample)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            var abs = Math.Abs(clamped);
            Peak = Math.Max(Peak, abs);
            Energy += Math.Abs((short)Math.Round(clamped * short.MaxValue));
        }
    }
}
#endif
