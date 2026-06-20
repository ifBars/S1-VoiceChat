namespace S1VoiceChat.Routing;

public readonly struct VoicePeerState
{
    public VoicePeerState(ulong peerId, float x, float y, float z, bool muted = false)
    {
        PeerId = peerId;
        X = x;
        Y = y;
        Z = z;
        Muted = muted;
    }

    public ulong PeerId { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public bool Muted { get; }

    public float DistanceSquaredTo(VoicePeerState other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
