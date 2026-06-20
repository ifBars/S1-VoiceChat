using System;

namespace S1VoiceChat.Network;

/// <summary>
/// Adapter boundary for SteamNetworkLib/SNL.
///
/// Keep the voice stack independent from the concrete networking library. The final mod should
/// implement this class by forwarding serialized VoicePacket bytes through SteamNetworkLib custom
/// P2P messages for clients, and through a server relay path for dedicated servers.
/// </summary>
public sealed class SnlVoiceTransport : IVoiceTransport
{
    public event Action<ulong, VoicePacket>? OnPacket;

    public bool IsReady { get; private set; }

    public SnlVoiceTransport()
    {
        IsReady = false;
    }

    public void Attach(object snlClientOrServer)
    {
        // TODO: Replace object with the real SteamNetworkLib client/server type once references
        // are added. Register the voice packet handler here.
        IsReady = snlClientOrServer != null;
    }

    public void SendTo(ulong peerId, VoicePacket packet)
    {
        if (!IsReady)
            return;

        var bytes = packet.Serialize();

        // TODO: Send bytes to peerId using SteamNetworkLib/SNL unreliable no-delay messaging.
        _ = bytes;
        _ = peerId;
    }

    public void Broadcast(VoicePacket packet)
    {
        if (!IsReady)
            return;

        var bytes = packet.Serialize();

        // TODO: Broadcast to current relevant peers. For proximity voice, prefer caller-side
        // routing so you do not send voice to players outside range.
        _ = bytes;
    }

    public void ReceiveFromNetwork(ulong senderPeerId, byte[] data)
    {
        var packet = VoicePacket.Deserialize(data);
        OnPacket?.Invoke(senderPeerId, packet);
    }

    public void Poll()
    {
        // SteamNetworkLib may already pump callbacks elsewhere. Keep this method for transports
        // that need explicit polling.
    }

    public void Dispose()
    {
        IsReady = false;
    }
}
