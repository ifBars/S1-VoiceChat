using System;

namespace S1VoiceChat.Network;

public interface IVoiceTransport : IDisposable
{
    event Action<ulong, VoicePacket>? OnPacket;
    bool IsReady { get; }
    void SendTo(ulong peerId, VoicePacket packet);
    void Broadcast(VoicePacket packet);
    void Poll();
}
