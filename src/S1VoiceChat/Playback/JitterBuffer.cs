using System;
using System.Collections.Generic;
using S1VoiceChat.Network;

namespace S1VoiceChat.Playback;

public sealed class JitterBuffer
{
    private readonly SortedDictionary<ushort, VoicePacket> _packets = new();
    private ushort _expectedSequence;
    private bool _hasStarted;

    public int TargetBufferedPackets { get; set; } = 3;
    public int MaxBufferedPackets { get; set; } = 10;

    public int Count => _packets.Count;

    public void Add(VoicePacket packet)
    {
        if (!_hasStarted)
        {
            _expectedSequence = packet.Sequence;
            _hasStarted = true;
        }

        _packets[packet.Sequence] = packet;

        while (_packets.Count > MaxBufferedPackets)
        {
            var firstKey = FirstKey();
            _packets.Remove(firstKey);
            _expectedSequence = unchecked((ushort)(firstKey + 1));
        }
    }

    public bool TryPop(out VoicePacket? packet, out bool missing)
    {
        packet = null;
        missing = false;

        if (!_hasStarted || _packets.Count < TargetBufferedPackets)
            return false;

        if (_packets.TryGetValue(_expectedSequence, out packet))
        {
            _packets.Remove(_expectedSequence);
            _expectedSequence++;
            return true;
        }

        missing = true;
        _expectedSequence++;
        return true;
    }

    public void Reset()
    {
        _packets.Clear();
        _expectedSequence = 0;
        _hasStarted = false;
    }

    private ushort FirstKey()
    {
        foreach (var key in _packets.Keys)
            return key;

        throw new InvalidOperationException("Jitter buffer is empty.");
    }
}
