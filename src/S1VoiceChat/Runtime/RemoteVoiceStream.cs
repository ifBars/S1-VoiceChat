using System;
using S1VoiceChat.Codec;
using S1VoiceChat.Network;
using S1VoiceChat.Playback;
using S1VoiceChat.Routing;

namespace S1VoiceChat.Runtime;

public sealed class RemoteVoiceStream : IDisposable
{
    private readonly Func<VoiceCodecKind, IVoiceCodec?> _codecFactory;
    private readonly Dictionary<VoiceCodecKind, IVoiceCodec> _codecs = new();
    private readonly JitterBuffer _jitterBuffer;
    private readonly AudioRingBuffer _audioBuffer;
    private readonly short[] _decodeBuffer;
    private VoiceCodecKind _lastCodecKind = VoiceCodecKind.Opus;
    private bool _disposed;

    public RemoteVoiceStream(ulong peerId, Func<VoiceCodecKind, IVoiceCodec?> codecFactory, VoiceSettings settings)
    {
        PeerId = peerId;
        _codecFactory = codecFactory;
        _jitterBuffer = new JitterBuffer
        {
            TargetBufferedPackets = settings.JitterTargetPackets,
            MaxBufferedPackets = settings.JitterMaxPackets
        };
        _audioBuffer = new AudioRingBuffer(settings.SampleRate * settings.Channels * 2);
        _decodeBuffer = new short[settings.FrameSize * settings.Channels];
    }

    public ulong PeerId { get; }
    public int BufferedSamples => _audioBuffer.Count;
    public VoiceChannel LastChannel { get; private set; } = VoiceChannel.Global;

    public void AddPacket(VoicePacket packet)
    {
        if (_disposed)
            return;

        LastChannel = Enum.IsDefined(typeof(VoiceChannel), packet.Channel)
            ? (VoiceChannel)packet.Channel
            : VoiceChannel.Global;
        _jitterBuffer.Add(packet);
    }

    public void Update()
    {
        if (_disposed)
            return;

        while (_jitterBuffer.TryPop(out var packet, out var missing))
        {
            Array.Clear(_decodeBuffer, 0, _decodeBuffer.Length);

            var decodedSamples = missing || packet == null
                ? DecodePacketLoss()
                : DecodePacket(packet);

            if (decodedSamples > 0)
            {
                var channels = GetCodec(_lastCodecKind)?.Channels ?? 1;
                _audioBuffer.Write(_decodeBuffer.AsSpan(0, decodedSamples * channels));
            }
        }
    }

    public int ReadPcm(Span<short> destination)
    {
        return _audioBuffer.Read(destination);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _audioBuffer.Clear();
        foreach (var codec in _codecs.Values)
            codec.Dispose();

        _codecs.Clear();
    }

    private int DecodePacket(VoicePacket packet)
    {
        if (packet.Codec == VoiceCodecKind.Control)
            return 0;

        var codec = GetCodec(packet.Codec);
        if (codec == null)
            return 0;

        _lastCodecKind = packet.Codec;
        return codec.Decode(packet.Payload, _decodeBuffer);
    }

    private int DecodePacketLoss()
    {
        var codec = GetCodec(_lastCodecKind);
        return codec?.DecodePacketLoss(_decodeBuffer) ?? 0;
    }

    private IVoiceCodec? GetCodec(VoiceCodecKind kind)
    {
        if (kind == VoiceCodecKind.Control)
            return null;

        if (_codecs.TryGetValue(kind, out var codec))
            return codec;

        codec = _codecFactory(kind);
        if (codec == null)
            return null;

        _codecs.Add(kind, codec);
        return codec;
    }
}
