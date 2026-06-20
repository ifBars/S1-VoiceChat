using System;
using S1VoiceChat.Codec;
using S1VoiceChat.Network;
using S1VoiceChat.Playback;
using S1VoiceChat.Routing;

namespace S1VoiceChat.Runtime;

public sealed class RemoteVoiceStream : IDisposable
{
    private readonly IVoiceCodec _codec;
    private readonly JitterBuffer _jitterBuffer;
    private readonly AudioRingBuffer _audioBuffer;
    private readonly short[] _decodeBuffer;
    private bool _disposed;

    public RemoteVoiceStream(ulong peerId, IVoiceCodec codec, VoiceSettings settings)
    {
        PeerId = peerId;
        _codec = codec;
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
                ? _codec.DecodePacketLoss(_decodeBuffer)
                : _codec.Decode(packet.Payload, _decodeBuffer);

            if (decodedSamples > 0)
                _audioBuffer.Write(_decodeBuffer.AsSpan(0, decodedSamples * _codec.Channels));
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
        _codec.Dispose();
    }
}
