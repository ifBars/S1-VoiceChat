using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

var options = ProbeOptions.Parse(args);
const int AutoProbeMilliseconds = 650;
using var tonePlayback = options.PlayTestTone ? TestTonePlayback.Start() : null;
using var enumerator = new MMDeviceEnumerator();

var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
Console.WriteLine($"Active capture devices: {devices.Length}");
for (var i = 0; i < devices.Length; i++)
{
    var format = devices[i].AudioClient.MixFormat;
    var subFormat = format is WaveFormatExtensible extensible ? extensible.SubFormat.ToString() : "";
    Console.WriteLine($"[{i}] {devices[i].FriendlyName}|Id={devices[i].ID}|SampleRate={format.SampleRate}|Channels={format.Channels}|Bits={format.BitsPerSample}|Encoding={format.Encoding}|SubFormat={subFormat}|Volume={GetEndpointVolume(devices[i]):0.000}|Muted={GetEndpointMuted(devices[i])}|Meter={GetEndpointMeter(devices[i]):0.000000}");
}

if (devices.Length == 0)
{
    Console.Error.WriteLine("No active capture devices found.");
    return 2;
}

using var device = ResolveDevice(enumerator, devices, options.Device);
var selectedFormat = device.AudioClient.MixFormat;
Console.WriteLine($"Selected: {device.FriendlyName}");
Console.WriteLine($"DurationMs: {options.DurationMs}");
Console.WriteLine($"Endpoint: Volume={GetEndpointVolume(device):0.000}|Muted={GetEndpointMuted(device)}|Meter={GetEndpointMeter(device):0.000000}");

var summary = new CaptureSummary();
using var capture = new WasapiCapture(device);
capture.DataAvailable += (_, eventArgs) => summary.Add(eventArgs.Buffer, eventArgs.BytesRecorded, capture.WaveFormat);
capture.RecordingStopped += (_, eventArgs) =>
{
    if (eventArgs.Exception != null)
        Console.Error.WriteLine($"Recording stopped with exception: {eventArgs.Exception}");
};

capture.StartRecording();
await Task.Delay(options.DurationMs);
capture.StopRecording();

Console.WriteLine($"WaveFormat: SampleRate={capture.WaveFormat.SampleRate}|Channels={capture.WaveFormat.Channels}|Bits={capture.WaveFormat.BitsPerSample}|Encoding={capture.WaveFormat.Encoding}");
Console.WriteLine($"Captured: Events={summary.Events}|Bytes={summary.Bytes}|Frames={summary.Frames}|Unsupported={summary.UnsupportedEvents}|Energy={summary.Energy}|Peak={summary.Peak:0.000000}|EndpointMeter={GetEndpointMeter(device):0.000000}");

if (options.RequireNonZero && summary.Energy == 0)
{
    Console.Error.WriteLine("Captured audio was all zero.");
    return 1;
}

return 0;

static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, IReadOnlyList<MMDevice> devices, string? preferredDevice)
{
    if (string.Equals(preferredDevice, "auto", StringComparison.OrdinalIgnoreCase))
        return ResolveAutoDevice(devices);

    if (string.Equals(preferredDevice, "default", StringComparison.OrdinalIgnoreCase))
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

    if (int.TryParse(preferredDevice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
        index >= 0 &&
        index < devices.Count)
    {
        return devices[index];
    }

    if (!string.IsNullOrWhiteSpace(preferredDevice))
    {
        var exact = devices.FirstOrDefault(device => string.Equals(device.FriendlyName, preferredDevice, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var contains = devices.FirstOrDefault(device => device.FriendlyName.Contains(preferredDevice, StringComparison.OrdinalIgnoreCase));
        if (contains != null)
            return contains;
    }

    return devices[0];
}

static MMDevice ResolveAutoDevice(IReadOnlyList<MMDevice> devices)
{
    (MMDevice Device, CaptureSummary Summary)? best = null;
    foreach (var device in devices)
    {
        var summary = ProbeDevice(device, AutoProbeMilliseconds);
        Console.WriteLine($"AutoProbe: Device={device.FriendlyName}|Events={summary.Events}|Bytes={summary.Bytes}|Frames={summary.Frames}|Unsupported={summary.UnsupportedEvents}|Energy={summary.Energy}|Peak={summary.Peak:0.000000}");
        if (best == null || summary.Energy > best.Value.Summary.Energy || summary.Energy == best.Value.Summary.Energy && summary.Peak > best.Value.Summary.Peak)
            best = (device, summary);
    }

    if (best == null)
        return devices[0];

    Console.WriteLine($"AutoSelected: Device={best.Value.Device.FriendlyName}|Energy={best.Value.Summary.Energy}|Peak={best.Value.Summary.Peak:0.000000}");
    return best.Value.Device;
}

static CaptureSummary ProbeDevice(MMDevice device, int durationMs)
{
    var summary = new CaptureSummary();
    using var capture = new WasapiCapture(device);
    capture.DataAvailable += (_, eventArgs) => summary.Add(eventArgs.Buffer, eventArgs.BytesRecorded, capture.WaveFormat);
    capture.StartRecording();
    Thread.Sleep(durationMs);
    capture.StopRecording();
    return summary;
}

static float GetEndpointVolume(MMDevice device)
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

static bool GetEndpointMuted(MMDevice device)
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

static float GetEndpointMeter(MMDevice device)
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

internal sealed class CaptureSummary
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public int Events { get; private set; }
    public long Bytes { get; private set; }
    public long Frames { get; private set; }
    public long Energy { get; private set; }
    public float Peak { get; private set; }
    public int UnsupportedEvents { get; private set; }

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

    private static bool IsFloatFormat(WaveFormat format)
    {
        return format.Encoding == WaveFormatEncoding.IeeeFloat ||
               format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat;
    }
}

internal sealed class ProbeOptions
{
    public string? Device { get; private set; }
    public int DurationMs { get; private set; } = 3000;
    public bool RequireNonZero { get; private set; }
    public bool PlayTestTone { get; private set; }

    public static ProbeOptions Parse(string[] args)
    {
        var options = new ProbeOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--device" when i + 1 < args.Length:
                    options.Device = args[++i];
                    break;
                case "--duration-ms" when i + 1 < args.Length &&
                                          int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs):
                    options.DurationMs = Math.Max(100, durationMs);
                    break;
                case "--require-nonzero":
                    options.RequireNonZero = true;
                    break;
                case "--play-test-tone":
                    options.PlayTestTone = true;
                    break;
            }
        }

        return options;
    }
}

internal sealed class TestTonePlayback : IDisposable
{
    private readonly WaveOutEvent _waveOut;
    private readonly SignalGenerator _signal;

    private TestTonePlayback(WaveOutEvent waveOut, SignalGenerator signal)
    {
        _waveOut = waveOut;
        _signal = signal;
    }

    public static TestTonePlayback Start()
    {
        var signal = new SignalGenerator(48000, 2)
        {
            Gain = 0.25,
            Frequency = 440,
            Type = SignalGeneratorType.Sin
        };
        var waveOut = new WaveOutEvent();
        waveOut.Init(signal);
        waveOut.Play();
        return new TestTonePlayback(waveOut, signal);
    }

    public void Dispose()
    {
        _waveOut.Stop();
        _waveOut.Dispose();
    }
}
