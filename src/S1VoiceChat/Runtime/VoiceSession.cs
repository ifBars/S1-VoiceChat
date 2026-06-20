using System;
using System.Collections.Generic;
using S1VoiceChat.Codec;
using S1VoiceChat.Network;
using S1VoiceChat.Routing;

namespace S1VoiceChat.Runtime;

public sealed class VoiceSession : IDisposable
{
    private readonly IVoiceTransport _transport;
    private readonly IVoiceCodec _localCodec;
    private readonly VoiceCodecKind _localCodecKind;
    private readonly VoiceSettings _settings;
    private readonly Func<VoiceCodecKind, IVoiceCodec?> _remoteCodecFactory;
    private readonly Dictionary<ulong, RemoteVoiceStream> _remoteStreams = new();
    private ushort _sequence;
    private bool _disposed;

    public VoiceSession(
        ulong localPeerId,
        IVoiceTransport transport,
        IVoiceCodec localCodec,
        VoiceSettings settings,
        VoiceCodecKind localCodecKind = VoiceCodecKind.Opus,
        Func<VoiceCodecKind, IVoiceCodec?>? remoteCodecFactory = null)
    {
        LocalPeerId = localPeerId;
        _transport = transport;
        _localCodec = localCodec;
        _localCodecKind = localCodecKind;
        _settings = settings;
        _remoteCodecFactory = remoteCodecFactory ?? CreateDefaultRemoteCodec;
        _transport.OnPacket += OnPacket;
    }

    public ulong LocalPeerId { get; }

    public IReadOnlyDictionary<ulong, RemoteVoiceStream> RemoteStreams => _remoteStreams;

    public void SendPcmFrame(ReadOnlySpan<short> pcm, VoiceChannel channel, IReadOnlyList<ulong>? recipients = null)
    {
        if (_disposed || !_transport.IsReady)
            return;

        var encoded = new byte[Math.Min(_settings.MaxEncodedBytesPerFrame, VoicePacket.MaxPayloadBytes)];
        var encodedLength = _localCodec.Encode(pcm, encoded);
        if (encodedLength <= 0)
            return;

        var payload = new byte[encodedLength];
        Array.Copy(encoded, payload, encodedLength);

        var packet = new VoicePacket
        {
            Version = 1,
            Channel = (byte)channel,
            Codec = _localCodecKind,
            Sequence = _sequence++,
            CaptureTimeMs = unchecked((uint)Environment.TickCount),
            SenderPeerId = LocalPeerId,
            Payload = payload
        };

        if (recipients == null || recipients.Count == 0)
        {
            _transport.Broadcast(packet);
            return;
        }

        foreach (var peerId in recipients)
            _transport.SendTo(peerId, packet);
    }

    public RemoteVoiceStream GetOrCreateRemoteStream(ulong peerId)
    {
        if (_remoteStreams.TryGetValue(peerId, out var stream))
            return stream;

        stream = new RemoteVoiceStream(peerId, _remoteCodecFactory, _settings);
        _remoteStreams.Add(peerId, stream);
        return stream;
    }

    public void Update()
    {
        _transport.Poll();

        foreach (var stream in _remoteStreams.Values)
            stream.Update();
    }

    private void OnPacket(ulong senderPeerId, VoicePacket packet)
    {
        var speakerPeerId = packet.SenderPeerId == 0 ? senderPeerId : packet.SenderPeerId;
        if (speakerPeerId == LocalPeerId)
            return;

        var stream = GetOrCreateRemoteStream(speakerPeerId);
        stream.AddPacket(packet);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _transport.OnPacket -= OnPacket;
        _localCodec.Dispose();

        foreach (var stream in _remoteStreams.Values)
            stream.Dispose();

        _remoteStreams.Clear();
        _disposed = true;
    }

    private IVoiceCodec? CreateDefaultRemoteCodec(VoiceCodecKind kind)
    {
        return kind is VoiceCodecKind.Opus or VoiceCodecKind.Pcm16
            ? VoiceCodecFactory.Create(kind, ToCodecOptions())
            : null;
    }

    private VoiceCodecOptions ToCodecOptions()
    {
        return new VoiceCodecOptions
        {
            SampleRate = _settings.SampleRate,
            Channels = _settings.Channels,
            FrameSize = _settings.FrameSize,
            OpusBitrate = _settings.OpusBitrate,
            OpusComplexity = _settings.OpusComplexity,
            OpusExpectedPacketLossPercent = _settings.OpusExpectedPacketLossPercent,
            OpusInbandFecEnabled = _settings.OpusInbandFecEnabled,
            OpusDtxEnabled = _settings.OpusDtxEnabled
        };
    }
}
