using System.Collections.Generic;
using S1VoiceChat.Runtime;

namespace S1VoiceChat.Routing;

public sealed class ProximityRouter
{
    private readonly VoiceSettings _settings;

    public ProximityRouter(VoiceSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<ulong> GetRecipients(VoicePeerState sender, IEnumerable<VoicePeerState> peers, VoiceChannel channel)
    {
        var result = new List<ulong>();
        var range = GetRange(channel);
        var rangeSquared = range * range;

        foreach (var peer in peers)
        {
            if (peer.PeerId == sender.PeerId || peer.Muted)
                continue;

            if (channel == VoiceChannel.Radio || channel == VoiceChannel.Global)
            {
                result.Add(peer.PeerId);
                continue;
            }

            if (sender.DistanceSquaredTo(peer) <= rangeSquared)
                result.Add(peer.PeerId);
        }

        return result;
    }

    private float GetRange(VoiceChannel channel)
    {
        return channel switch
        {
            VoiceChannel.Whisper => _settings.WhisperRangeMeters,
            VoiceChannel.Shout => _settings.ShoutRangeMeters,
            VoiceChannel.Proximity => _settings.ProximityRangeMeters,
            _ => float.MaxValue
        };
    }
}
