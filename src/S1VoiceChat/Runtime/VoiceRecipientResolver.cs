using System;
using System.Collections.Generic;
using S1VoiceChat.Routing;

namespace S1VoiceChat.Runtime;

public sealed class VoiceRecipientResolver
{
    private readonly ProximityRouter _router;

    public VoiceRecipientResolver(VoiceSettings settings)
    {
        _router = new ProximityRouter(settings);
    }

    public IReadOnlyList<ulong> Resolve(
        ulong localPeerId,
        VoiceChannel channel,
        IReadOnlyList<VoicePeerState> playerStates,
        IReadOnlyList<ulong> fallbackRemotePeers,
        VoiceMuteList? muteList = null)
    {
        if (channel == VoiceChannel.Global || channel == VoiceChannel.Radio)
            return DistinctNonZero(fallbackRemotePeers, localPeerId, muteList);

        if (playerStates.Count == 0)
            return DistinctNonZero(fallbackRemotePeers, localPeerId, muteList);

        VoicePeerState? localState = null;
        foreach (var peer in playerStates)
        {
            if (peer.PeerId == localPeerId)
            {
                localState = peer;
                break;
            }
        }

        if (!localState.HasValue)
            return DistinctNonZero(fallbackRemotePeers, localPeerId, muteList);

        return _router.GetRecipients(localState.Value, playerStates, channel);
    }

    private static IReadOnlyList<ulong> DistinctNonZero(IReadOnlyList<ulong> peers, ulong localPeerId, VoiceMuteList? muteList)
    {
        if (peers.Count == 0)
            return Array.Empty<ulong>();

        var result = new List<ulong>();
        foreach (var peer in peers)
        {
            if (peer == 0 || peer == localPeerId || result.Contains(peer) || muteList?.IsMuted(peer) == true)
                continue;

            result.Add(peer);
        }

        return result;
    }
}
