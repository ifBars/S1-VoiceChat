using System;
using System.Collections.Generic;
using System.Globalization;

namespace S1VoiceChat.Runtime;

public sealed class VoiceMuteList
{
    private readonly HashSet<ulong> _mutedPeers;

    public VoiceMuteList()
        : this(Array.Empty<ulong>())
    {
    }

    public VoiceMuteList(IEnumerable<ulong> mutedPeers)
    {
        _mutedPeers = new HashSet<ulong>();
        foreach (var peerId in mutedPeers)
        {
            if (peerId != 0)
                _mutedPeers.Add(peerId);
        }
    }

    public int Count => _mutedPeers.Count;

    public bool IsMuted(ulong peerId)
    {
        return peerId != 0 && _mutedPeers.Contains(peerId);
    }

    public static VoiceMuteList FromCommandLine(string[] args)
    {
        var mutedPeers = new List<ulong>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--s1vc-muted-peer" && i + 1 < args.Length)
            {
                AddPeer(args[++i], mutedPeers);
            }
            else if (arg == "--s1vc-muted-peers" && i + 1 < args.Length)
            {
                foreach (var part in args[++i].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    AddPeer(part, mutedPeers);
            }
        }

        return new VoiceMuteList(mutedPeers);
    }

    private static void AddPeer(string rawValue, List<ulong> mutedPeers)
    {
        if (ulong.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var peerId) && peerId != 0)
            mutedPeers.Add(peerId);
    }
}
