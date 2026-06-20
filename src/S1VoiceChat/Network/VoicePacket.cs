using System;
using System.IO;

namespace S1VoiceChat.Network;

public sealed class VoicePacket
{
    public byte Version { get; set; } = 1;
    public byte Channel { get; set; }
    public ushort Sequence { get; set; }
    public uint CaptureTimeMs { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Version);
        bw.Write(Channel);
        bw.Write(Sequence);
        bw.Write(CaptureTimeMs);
        bw.Write((ushort)Payload.Length);
        bw.Write(Payload);

        return ms.ToArray();
    }

    public static VoicePacket Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var packet = new VoicePacket();
        packet.Version = br.ReadByte();
        packet.Channel = br.ReadByte();
        packet.Sequence = br.ReadUInt16();
        packet.CaptureTimeMs = br.ReadUInt32();

        var length = br.ReadUInt16();
        packet.Payload = br.ReadBytes(length);

        return packet;
    }
}
