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
    private ISnlVoicePacketClient? _client;
    private bool _disposed;

    public event Action<ulong, VoicePacket>? OnPacket;

    public bool IsReady => !_disposed && _client?.IsReady == true;

    public SnlVoiceTransport()
    {
    }

    public SnlVoiceTransport(ISnlVoicePacketClient client)
    {
        Attach(client);
    }

    public void Attach(object snlClientOrServer)
    {
        if (snlClientOrServer is not ISnlVoicePacketClient client)
            throw new ArgumentException("SNL voice transport requires an ISnlVoicePacketClient adapter.", nameof(snlClientOrServer));

        Attach(client);
    }

    public void Attach(ISnlVoicePacketClient client)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SnlVoiceTransport));

        if (_client != null)
            _client.OnRawVoicePacket -= ReceiveFromNetwork;

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _client.OnRawVoicePacket += ReceiveFromNetwork;
    }

    public void SendTo(ulong peerId, VoicePacket packet)
    {
        TrySendTo(peerId, packet);
    }

    public bool TrySendTo(ulong peerId, VoicePacket packet)
    {
        if (!IsReady)
            return false;

        var bytes = packet.Serialize();
        return _client!.SendVoicePacket(peerId, bytes);
    }

    public void Broadcast(VoicePacket packet)
    {
        if (!IsReady)
            return;

        var bytes = packet.Serialize();
        _client!.BroadcastVoicePacket(bytes);
    }

    public void ReceiveFromNetwork(ulong senderPeerId, byte[] data)
    {
        TryReceiveFromNetwork(senderPeerId, data);
    }

    public bool TryReceiveFromNetwork(ulong senderPeerId, byte[] data)
    {
        try
        {
            var packet = VoicePacket.Deserialize(data);
            OnPacket?.Invoke(senderPeerId, packet);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Poll()
    {
        if (IsReady)
            _client!.ProcessIncomingVoicePackets();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_client != null)
            _client.OnRawVoicePacket -= ReceiveFromNetwork;

        _client = null;
        _disposed = true;
    }
}

public interface ISnlVoicePacketClient
{
    event Action<ulong, byte[]>? OnRawVoicePacket;

    bool IsReady { get; }

    bool SendVoicePacket(ulong peerId, byte[] data);

    void BroadcastVoicePacket(byte[] data);

    void ProcessIncomingVoicePackets();
}
