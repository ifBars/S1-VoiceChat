using System;
using System.Collections.Generic;

namespace S1VoiceChat.Network;

/// <summary>
/// Dedicated-server relay for compressed voice packets.
/// </summary>
public sealed class VoiceRelayService : IDisposable
{
    private readonly IVoiceTransport _transport;
    private readonly Func<ulong, VoicePacket, IReadOnlyList<ulong>> _resolveRecipients;
    private readonly VoicePacketRateLimiter _rateLimiter;
    private readonly ulong _serverPeerId;
    private bool _disposed;

    public VoiceRelayService(
        ulong serverPeerId,
        IVoiceTransport transport,
        Func<ulong, VoicePacket, IReadOnlyList<ulong>> resolveRecipients)
        : this(serverPeerId, transport, resolveRecipients, new VoicePacketRateLimiter(40, TimeSpan.FromSeconds(1)))
    {
    }

    public VoiceRelayService(
        ulong serverPeerId,
        IVoiceTransport transport,
        Func<ulong, VoicePacket, IReadOnlyList<ulong>> resolveRecipients,
        VoicePacketRateLimiter rateLimiter)
    {
        _serverPeerId = serverPeerId;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _resolveRecipients = resolveRecipients ?? throw new ArgumentNullException(nameof(resolveRecipients));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _transport.OnPacket += OnPacket;
    }

    public int DroppedSpoofedPackets { get; private set; }
    public int DroppedRateLimitedPackets { get; private set; }
    public int RelayedPackets { get; private set; }

    private void OnPacket(ulong networkSenderPeerId, VoicePacket packet)
    {
        if (_disposed || !_transport.IsReady)
            return;

        if (packet.SenderPeerId == 0)
        {
            packet.SenderPeerId = networkSenderPeerId;
        }
        else if (packet.SenderPeerId != networkSenderPeerId)
        {
            DroppedSpoofedPackets++;
            return;
        }

        if (!_rateLimiter.Allow(packet.SenderPeerId))
        {
            DroppedRateLimitedPackets++;
            return;
        }

        var recipients = _resolveRecipients(networkSenderPeerId, packet);
        if (recipients.Count == 0)
            return;

        var sent = false;
        var dedupe = new HashSet<ulong>();
        foreach (var recipient in recipients)
        {
            if (recipient == 0 || recipient == networkSenderPeerId || recipient == _serverPeerId || !dedupe.Add(recipient))
                continue;

            _transport.SendTo(recipient, packet);
            sent = true;
        }

        if (sent)
            RelayedPackets++;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _transport.OnPacket -= OnPacket;
        _disposed = true;
    }
}
