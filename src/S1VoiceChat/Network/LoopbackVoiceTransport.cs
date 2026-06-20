using System;
using System.Collections.Generic;

namespace S1VoiceChat.Network;

public sealed class LoopbackVoiceTransport : IVoiceTransport
{
    private readonly Queue<(ulong Sender, VoicePacket Packet)> _queue = new();
    private bool _disposed;

    public event Action<ulong, VoicePacket>? OnPacket;

    public bool IsReady => !_disposed;

    public ulong LocalPeerId { get; set; } = 1;

    public void SendTo(ulong peerId, VoicePacket packet)
    {
        if (_disposed)
            return;

        _queue.Enqueue((LocalPeerId, packet));
    }

    public void Broadcast(VoicePacket packet)
    {
        SendTo(LocalPeerId, packet);
    }

    public void Poll()
    {
        while (_queue.Count > 0)
        {
            var item = _queue.Dequeue();
            OnPacket?.Invoke(item.Sender, item.Packet);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _queue.Clear();
    }
}
