#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using SteamNetworkLib;

#if MONOMELON
using Steamworks;
#elif IL2CPPMELON
using Il2CppSteamworks;
#endif

namespace S1VoiceChat.Network;

/// <summary>
/// SteamNetworkLib-backed raw packet adapter for S1 voice packets.
/// </summary>
public sealed class SteamNetworkLibVoicePacketClient : ISnlVoicePacketClient, IDisposable
{
    public const int DefaultChannel = 3;

    private readonly SteamNetworkClient _client;
    private readonly int _channel;
    private bool _disposed;

    public SteamNetworkLibVoicePacketClient(SteamNetworkClient client, int channel = DefaultChannel)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _channel = channel;

        if (_client.P2PManager != null)
            _client.P2PManager.OnPacketReceived += OnPacketReceived;
    }

    public event Action<ulong, byte[]>? OnRawVoicePacket;

    public bool IsReady => !_disposed && _client.IsInitialized && _client.P2PManager?.IsActive == true;

    public bool SendVoicePacket(ulong peerId, byte[] data)
    {
        if (!IsReady || peerId == 0 || data.Length > VoicePacket.MaxWireBytes)
            return false;

        _ = _client.P2PManager!.SendPacketAsync(ToSteamId(peerId), data, _channel, EP2PSend.k_EP2PSendUnreliableNoDelay);
        return true;
    }

    public void BroadcastVoicePacket(byte[] data)
    {
        if (!IsReady || data.Length > VoicePacket.MaxWireBytes)
            return;

        _client.P2PManager!.BroadcastPacket(data, _channel, EP2PSend.k_EP2PSendUnreliableNoDelay);
    }

    public void ProcessIncomingVoicePackets()
    {
        if (!_disposed)
            _client.ProcessIncomingMessages();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_client.P2PManager != null)
            _client.P2PManager.OnPacketReceived -= OnPacketReceived;

        _disposed = true;
    }

    private void OnPacketReceived(object? sender, SteamNetworkLib.Events.P2PPacketReceivedEventArgs args)
    {
        if (_disposed || args.Channel != _channel || args.Data.Length > VoicePacket.MaxWireBytes)
            return;

        OnRawVoicePacket?.Invoke(args.SenderId.m_SteamID, args.Data);
    }

    private static CSteamID ToSteamId(ulong peerId)
    {
        return new CSteamID(peerId);
    }
}
#endif
