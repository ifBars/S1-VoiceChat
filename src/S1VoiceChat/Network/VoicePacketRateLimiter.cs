using System;
using System.Collections.Generic;

namespace S1VoiceChat.Network;

public sealed class VoicePacketRateLimiter
{
    private readonly int _maxPackets;
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<ulong, Queue<DateTime>> _peerPackets = new();

    public VoicePacketRateLimiter(int maxPackets, TimeSpan window, Func<DateTime>? utcNow = null)
    {
        if (maxPackets <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPackets), "Max packets must be greater than zero.");

        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be greater than zero.");

        _maxPackets = maxPackets;
        _window = window;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public bool Allow(ulong peerId)
    {
        if (peerId == 0)
            return false;

        var now = _utcNow();
        if (!_peerPackets.TryGetValue(peerId, out var packets))
        {
            packets = new Queue<DateTime>();
            _peerPackets[peerId] = packets;
        }

        var cutoff = now - _window;
        while (packets.Count > 0 && packets.Peek() <= cutoff)
            packets.Dequeue();

        if (packets.Count >= _maxPackets)
            return false;

        packets.Enqueue(now);
        return true;
    }

    public void Clear()
    {
        _peerPackets.Clear();
    }
}
