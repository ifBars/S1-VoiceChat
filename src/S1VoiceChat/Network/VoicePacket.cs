using System;
using System.IO;
using S1VoiceChat.Codec;

namespace S1VoiceChat.Network;

public sealed class VoicePacket
{
    public const int HeaderLength = 19;
    public const int MaxWireBytes = 1200;
    public const int MaxPayloadBytes = MaxWireBytes - HeaderLength;

    public byte Version { get; set; } = 1;
    public byte Channel { get; set; }
    public VoiceCodecKind Codec { get; set; } = VoiceCodecKind.Control;
    public ushort Sequence { get; set; }
    public uint CaptureTimeMs { get; set; }
    public ulong SenderPeerId { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        if (Payload.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"Voice packet payload cannot exceed {MaxPayloadBytes} bytes.");

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Version);
        bw.Write(Channel);
        bw.Write((byte)Codec);
        bw.Write(Sequence);
        bw.Write(CaptureTimeMs);
        bw.Write(SenderPeerId);
        bw.Write((ushort)Payload.Length);
        bw.Write(Payload);

        return ms.ToArray();
    }

    public static VoicePacket Deserialize(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length < HeaderLength)
            throw new ArgumentException($"Voice packet must be at least {HeaderLength} bytes.", nameof(data));

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var packet = new VoicePacket();
        packet.Version = br.ReadByte();
        packet.Channel = br.ReadByte();
        packet.Codec = (VoiceCodecKind)br.ReadByte();
        packet.Sequence = br.ReadUInt16();
        packet.CaptureTimeMs = br.ReadUInt32();
        packet.SenderPeerId = br.ReadUInt64();

        var length = br.ReadUInt16();
        if (length > MaxPayloadBytes)
            throw new ArgumentException($"Voice packet payload cannot exceed {MaxPayloadBytes} bytes.", nameof(data));

        var remaining = ms.Length - ms.Position;
        if (remaining != length)
            throw new ArgumentException("Voice packet payload length does not match the wire payload length.", nameof(data));

        packet.Payload = br.ReadBytes(length);

        return packet;
    }
}
