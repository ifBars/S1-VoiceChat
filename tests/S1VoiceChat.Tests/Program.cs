using System;
using System.Collections.Generic;
using System.Linq;
using S1VoiceChat.Codec;
using S1VoiceChat.Capture;
using S1VoiceChat.Network;
using S1VoiceChat.Playback;
using S1VoiceChat.Routing;
using S1VoiceChat.Runtime;

var tests = new (string Name, Action Run)[]
{
    ("VoicePacket round-trips binary payload", VoicePacketRoundTrips),
    ("VoicePacket rejects invalid wire data", VoicePacketRejectsInvalidData),
    ("ProximityRouter respects range, mute, and channel rules", ProximityRouterRoutesByChannel),
    ("AudioRingBuffer overwrites oldest samples", AudioRingBufferOverwritesOldest),
    ("JitterBuffer preserves order and reports missing packets", JitterBufferReportsMissingPackets),
    ("VoiceSession sends targeted encoded packets", VoiceSessionSendsTargetedPackets),
    ("VoiceSession uses packet sender for relayed packets", VoiceSessionUsesPacketSenderForRelayedPackets),
    ("RemoteVoiceStream tracks packet channel", RemoteVoiceStreamTracksPacketChannel),
    ("Pcm16Codec round-trips little-endian samples", Pcm16CodecRoundTrips),
    ("NativeOpusCodec round-trips nonzero voice frames when native library is present", NativeOpusCodecRoundTripsWhenAvailable),
    ("SyntheticToneCapture emits nonzero PCM", SyntheticToneCaptureEmitsNonzeroPcm),
    ("VoiceSession unsubscribes from transport on dispose", VoiceSessionUnsubscribesOnDispose),
    ("SnlVoiceTransport sends raw packets through adapter", SnlVoiceTransportSendsRawPackets),
    ("SnlVoiceTransport rejects malformed raw packets", SnlVoiceTransportRejectsMalformedRawPackets),
    ("VoiceRelayService forwards validated client packets", VoiceRelayServiceForwardsValidatedPackets),
    ("VoiceRelayService rejects spoofed sender packets", VoiceRelayServiceRejectsSpoofedPackets),
    ("VoiceRelayService drops rate-limited packets", VoiceRelayServiceDropsRateLimitedPackets),
    ("VoicePacketRateLimiter enforces per-peer sliding window", VoicePacketRateLimiterEnforcesSlidingWindow),
    ("VoiceRecipientResolver uses fallback peers for global voice", VoiceRecipientResolverUsesFallbackForGlobal),
    ("VoiceRecipientResolver excludes muted fallback peers", VoiceRecipientResolverExcludesMutedFallbackPeers),
    ("VoiceRecipientResolver filters proximity recipients by player state", VoiceRecipientResolverFiltersProximity),
    ("VoiceRecipientResolver falls back when local player state is missing", VoiceRecipientResolverFallsBackWhenLocalStateMissing),
    ("VoiceMuteList parses command-line peers", VoiceMuteListParsesCommandLinePeers),
    ("VoiceCaptureModePolicy preserves push-to-talk default", VoiceCaptureModePolicyPreservesPushToTalkDefault),
    ("VoiceCaptureModePolicy supports settings and forced open mic", VoiceCaptureModePolicySupportsOpenMic),
    ("VoiceDiagnostics reports zero payloads and PCM energy", VoiceDiagnosticsReportsZeroPayloadsAndPcmEnergy),
    ("LiveVoiceRuntimePolicy gates scenes and headless mode", LiveVoiceRuntimePolicyGatesScenesAndHeadlessMode),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void VoicePacketRoundTrips()
{
    var packet = new VoicePacket
    {
        Version = 1,
        Channel = (byte)VoiceChannel.Proximity,
        Codec = VoiceCodecKind.Opus,
        Sequence = 42,
        CaptureTimeMs = 123456,
        SenderPeerId = 76561198000000029,
        Payload = new byte[] { 1, 2, 3, 4, 5 }
    };

    var restored = VoicePacket.Deserialize(packet.Serialize());

    AssertEqual(packet.Version, restored.Version);
    AssertEqual(packet.Channel, restored.Channel);
    AssertEqual(packet.Codec, restored.Codec);
    AssertEqual(packet.Sequence, restored.Sequence);
    AssertEqual(packet.CaptureTimeMs, restored.CaptureTimeMs);
    AssertEqual(packet.SenderPeerId, restored.SenderPeerId);
    AssertSequence(packet.Payload, restored.Payload);
}

static void VoicePacketRejectsInvalidData()
{
    AssertThrows<ArgumentException>(() => VoicePacket.Deserialize(new byte[VoicePacket.HeaderLength - 1]));

    var truncated = new VoicePacket { Payload = new byte[] { 10, 20, 30 } }.Serialize();
    Array.Resize(ref truncated, truncated.Length - 1);
    AssertThrows<ArgumentException>(() => VoicePacket.Deserialize(truncated));

    var oversized = new VoicePacket { Payload = new byte[VoicePacket.MaxPayloadBytes + 1] };
    AssertThrows<InvalidOperationException>(() => oversized.Serialize());
}

static void ProximityRouterRoutesByChannel()
{
    var router = new ProximityRouter(new VoiceSettings
    {
        WhisperRangeMeters = 5,
        ProximityRangeMeters = 10,
        ShoutRangeMeters = 20
    });

    var sender = new VoicePeerState(1, 0, 0, 0);
    var peers = new[]
    {
        sender,
        new VoicePeerState(2, 3, 0, 4),
        new VoicePeerState(3, 9, 0, 0),
        new VoicePeerState(4, 19, 0, 0),
        new VoicePeerState(5, 50, 0, 0),
        new VoicePeerState(6, 1, 0, 0, muted: true),
    };

    AssertSequence(new ulong[] { 2 }, router.GetRecipients(sender, peers, VoiceChannel.Whisper));
    AssertSequence(new ulong[] { 2, 3 }, router.GetRecipients(sender, peers, VoiceChannel.Proximity));
    AssertSequence(new ulong[] { 2, 3, 4 }, router.GetRecipients(sender, peers, VoiceChannel.Shout));
    AssertSequence(new ulong[] { 2, 3, 4, 5 }, router.GetRecipients(sender, peers, VoiceChannel.Radio));
}

static void AudioRingBufferOverwritesOldest()
{
    var buffer = new AudioRingBuffer(4);
    AssertEqual(6, buffer.Write(new short[] { 1, 2, 3, 4, 5, 6 }));

    Span<short> destination = stackalloc short[4];
    AssertEqual(4, buffer.Read(destination));
    AssertSequence(new short[] { 3, 4, 5, 6 }, destination.ToArray());
}

static void JitterBufferReportsMissingPackets()
{
    var buffer = new JitterBuffer
    {
        TargetBufferedPackets = 2,
        MaxBufferedPackets = 4
    };

    buffer.Add(new VoicePacket { Sequence = 10, Payload = new byte[] { 10 } });
    buffer.Add(new VoicePacket { Sequence = 12, Payload = new byte[] { 12 } });

    AssertTrue(buffer.TryPop(out var first, out var firstMissing), "First packet should be ready.");
    AssertFalse(firstMissing, "First packet should not be missing.");
    AssertEqual((ushort)10, first!.Sequence);

    buffer.Add(new VoicePacket { Sequence = 13, Payload = new byte[] { 13 } });
    AssertTrue(buffer.TryPop(out var missingPacket, out var missing), "Missing packet slot should be reported.");
    AssertTrue(missing, "Sequence 11 should be reported missing.");
    AssertEqual(null, missingPacket);
}

static void VoiceSessionSendsTargetedPackets()
{
    var transport = new RecordingVoiceTransport();
    using var codec = new FakeVoiceCodec(48000, 1);
    using var session = new VoiceSession(100, transport, codec, new VoiceSettings(), VoiceCodecKind.Pcm16);

    session.SendPcmFrame(new short[] { 1, 2, 3, 4 }, VoiceChannel.Proximity, new ulong[] { 200, 300 });

    AssertEqual(0, transport.Broadcasts.Count);
    AssertEqual(2, transport.Sent.Count);
    AssertEqual((ulong)200, transport.Sent[0].PeerId);
    AssertEqual((ulong)300, transport.Sent[1].PeerId);
    AssertEqual((ulong)100, transport.Sent[0].Packet.SenderPeerId);
    AssertEqual(VoiceCodecKind.Pcm16, transport.Sent[0].Packet.Codec);
    AssertSequence(new byte[] { 1, 2, 3, 4 }, transport.Sent[0].Packet.Payload);
}

static void VoiceSessionUsesPacketSenderForRelayedPackets()
{
    var transport = new RecordingVoiceTransport();
    using var codec = new FakeVoiceCodec(48000, 1);
    using var session = new VoiceSession(
        100,
        transport,
        codec,
        new VoiceSettings(),
        VoiceCodecKind.Pcm16,
        _ => new FakeVoiceCodec(48000, 1));

    transport.Raise(999, new VoicePacket
    {
        Codec = VoiceCodecKind.Pcm16,
        SenderPeerId = 200,
        Sequence = 1,
        Payload = new byte[] { 10, 20 }
    });

    AssertTrue(session.RemoteStreams.ContainsKey(200), "Relayed packet should be keyed by packet sender.");
    AssertFalse(session.RemoteStreams.ContainsKey(999), "Relay server should not become the speaker stream.");
}

static void RemoteVoiceStreamTracksPacketChannel()
{
    using var stream = new RemoteVoiceStream(200, _ => new FakeVoiceCodec(48000, 1), new VoiceSettings());
    stream.AddPacket(new VoicePacket
    {
        Channel = (byte)VoiceChannel.Proximity,
        Codec = VoiceCodecKind.Pcm16,
        Sequence = 1,
        Payload = new byte[] { 10, 20 }
    });

    AssertEqual(VoiceChannel.Proximity, stream.LastChannel);
}

static void NativeOpusCodecRoundTripsWhenAvailable()
{
    var options = new VoiceCodecOptions
    {
        SampleRate = 48000,
        Channels = 1,
        FrameSize = 480,
        OpusBitrate = 24000
    };

    try
    {
        using var codec = new NativeOpusCodec(options);
        var pcm = new short[options.FrameSize];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (short)(Math.Sin(i / 48000d * 440d * Math.PI * 2d) * short.MaxValue * 0.35);

        var encoded = new byte[VoicePacket.MaxPayloadBytes];
        var decoded = new short[options.FrameSize];
        var encodedLength = codec.Encode(pcm, encoded);
        var decodedFrames = codec.Decode(encoded.AsSpan(0, encodedLength), decoded);

        AssertTrue(encodedLength > 0, "Opus should produce a non-empty encoded frame.");
        AssertTrue(encodedLength < pcm.Length * sizeof(short), "Opus payload should be smaller than raw PCM.");
        AssertEqual(options.FrameSize, decodedFrames);
        AssertTrue(VoiceDiagnostics.SumAbsolutePcm(decoded) > 0, "Decoded Opus PCM should have nonzero energy.");
    }
    catch (DllNotFoundException)
    {
        Console.WriteLine("SKIP NativeOpusCodec native library is not available in this test output.");
    }
    catch (BadImageFormatException)
    {
        Console.WriteLine("SKIP NativeOpusCodec native library architecture does not match this process.");
    }
}

static void Pcm16CodecRoundTrips()
{
    using var codec = new Pcm16Codec(16000, 1, 4);
    Span<byte> encoded = stackalloc byte[8];
    Span<short> decoded = stackalloc short[4];

    var written = codec.Encode(new short[] { -32768, -1, 0, 32767 }, encoded);
    var frames = codec.Decode(encoded.Slice(0, written), decoded);

    AssertEqual(8, written);
    AssertEqual(4, frames);
    AssertSequence(new short[] { -32768, -1, 0, 32767 }, decoded.ToArray());
}

static void SyntheticToneCaptureEmitsNonzeroPcm()
{
    using var capture = new SyntheticToneCapture(sampleRate: 48000, channels: 1, frameSamples: 480);
    var frame = new short[480];

    capture.Start();
    var samples = capture.ReadFrame(frame);

    AssertEqual(480, samples);
    AssertTrue(capture.LastPeak > 0.1f, "Synthetic tone should report meaningful peak amplitude.");
    AssertTrue(VoiceDiagnostics.SumAbsolutePcm(frame) > 0, "Synthetic tone should emit nonzero PCM samples.");
}

static void VoiceSessionUnsubscribesOnDispose()
{
    var transport = new RecordingVoiceTransport();
    var codec = new FakeVoiceCodec(48000, 1);
    var session = new VoiceSession(100, transport, codec, new VoiceSettings(), VoiceCodecKind.Pcm16);
    session.Dispose();

    transport.Raise(200, new VoicePacket { Codec = VoiceCodecKind.Pcm16, Payload = new byte[] { 1 } });
    AssertEqual(0, session.RemoteStreams.Count);
}

static void SnlVoiceTransportSendsRawPackets()
{
    var client = new RecordingSnlVoicePacketClient();
    using var transport = new SnlVoiceTransport(client);
    var packet = new VoicePacket { Sequence = 7, Payload = new byte[] { 1, 2, 3 } };

    transport.SendTo(200, packet);
    transport.Broadcast(packet);
    client.Raise(300, packet.Serialize());

    VoicePacket? received = null;
    ulong sender = 0;
    transport.OnPacket += (peerId, voicePacket) =>
    {
        sender = peerId;
        received = voicePacket;
    };

    client.Raise(400, packet.Serialize());

    AssertEqual(1, client.Sent.Count);
    AssertEqual((ulong)200, client.Sent[0].PeerId);
    AssertSequence(packet.Serialize(), client.Sent[0].Data);
    AssertEqual(1, client.Broadcasts.Count);
    AssertSequence(packet.Serialize(), client.Broadcasts[0]);
    AssertEqual((ulong)400, sender);
    AssertEqual((ushort)7, received!.Sequence);
}

static void SnlVoiceTransportRejectsMalformedRawPackets()
{
    var client = new RecordingSnlVoicePacketClient();
    using var transport = new SnlVoiceTransport(client);
    var received = false;
    transport.OnPacket += (_, _) => received = true;

    AssertFalse(transport.TryReceiveFromNetwork(200, new byte[] { 1, 2, 3 }), "Malformed packet should be rejected.");
    AssertFalse(received, "Malformed packet should not raise OnPacket.");
}

static void VoiceRelayServiceForwardsValidatedPackets()
{
    var transport = new RecordingVoiceTransport();
    using var relay = new VoiceRelayService(
        999,
        transport,
        (_, _) => new ulong[] { 200, 300, 200, 100, 999, 0 });

    var packet = new VoicePacket
    {
        SenderPeerId = 100,
        Sequence = 5,
        Payload = new byte[] { 1, 2, 3 }
    };

    transport.Raise(100, packet);

    AssertEqual(1, relay.RelayedPackets);
    AssertEqual(0, relay.DroppedSpoofedPackets);
    AssertEqual(2, transport.Sent.Count);
    AssertSequence(new ulong[] { 200, 300 }, transport.Sent.Select(item => item.PeerId));
    AssertTrue(transport.Sent.All(item => ReferenceEquals(packet, item.Packet)), "Relay should forward the same compressed packet instance.");
    AssertEqual((ulong)100, transport.Sent[0].Packet.SenderPeerId);
}

static void VoiceRelayServiceRejectsSpoofedPackets()
{
    var transport = new RecordingVoiceTransport();
    using var relay = new VoiceRelayService(999, transport, (_, _) => new ulong[] { 200 });

    transport.Raise(100, new VoicePacket
    {
        SenderPeerId = 101,
        Sequence = 5,
        Payload = new byte[] { 1, 2, 3 }
    });

    AssertEqual(0, relay.RelayedPackets);
    AssertEqual(1, relay.DroppedSpoofedPackets);
    AssertEqual(0, transport.Sent.Count);
}

static void VoiceRelayServiceDropsRateLimitedPackets()
{
    var now = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
    var transport = new RecordingVoiceTransport();
    using var relay = new VoiceRelayService(
        999,
        transport,
        (_, _) => new ulong[] { 200 },
        new VoicePacketRateLimiter(1, TimeSpan.FromSeconds(1), () => now));

    var firstPacket = new VoicePacket
    {
        SenderPeerId = 100,
        Sequence = 1,
        Payload = new byte[] { 1 }
    };
    var secondPacket = new VoicePacket
    {
        SenderPeerId = 100,
        Sequence = 2,
        Payload = new byte[] { 2 }
    };

    transport.Raise(100, firstPacket);
    transport.Raise(100, secondPacket);

    AssertEqual(1, relay.RelayedPackets);
    AssertEqual(1, relay.DroppedRateLimitedPackets);
    AssertEqual(1, transport.Sent.Count);
    AssertEqual((ushort)1, transport.Sent[0].Packet.Sequence);
}

static void VoicePacketRateLimiterEnforcesSlidingWindow()
{
    var now = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
    var limiter = new VoicePacketRateLimiter(2, TimeSpan.FromSeconds(1), () => now);

    AssertTrue(limiter.Allow(100), "First packet should be allowed.");
    AssertTrue(limiter.Allow(100), "Second packet inside the window should be allowed.");
    AssertFalse(limiter.Allow(100), "Third packet inside the window should be rate-limited.");
    AssertTrue(limiter.Allow(200), "Different peers should have independent limits.");

    now = now.AddSeconds(1.01);
    AssertTrue(limiter.Allow(100), "Peer should be allowed again after the window expires.");
    AssertFalse(limiter.Allow(0), "Zero peer ID should not be allowed.");
}

static void VoiceRecipientResolverUsesFallbackForGlobal()
{
    var resolver = new VoiceRecipientResolver(new VoiceSettings());

    var recipients = resolver.Resolve(
        100,
        VoiceChannel.Global,
        Array.Empty<VoicePeerState>(),
        new ulong[] { 0, 100, 200, 200, 300 });

    AssertSequence(new ulong[] { 200, 300 }, recipients);
}

static void VoiceRecipientResolverExcludesMutedFallbackPeers()
{
    var resolver = new VoiceRecipientResolver(new VoiceSettings());
    var muteList = new VoiceMuteList(new ulong[] { 300 });

    var recipients = resolver.Resolve(
        100,
        VoiceChannel.Global,
        Array.Empty<VoicePeerState>(),
        new ulong[] { 200, 300, 400 },
        muteList);

    AssertSequence(new ulong[] { 200, 400 }, recipients);
}

static void VoiceRecipientResolverFiltersProximity()
{
    var resolver = new VoiceRecipientResolver(new VoiceSettings
    {
        ProximityRangeMeters = 10
    });

    var recipients = resolver.Resolve(
        100,
        VoiceChannel.Proximity,
        new[]
        {
            new VoicePeerState(100, 0, 0, 0),
            new VoicePeerState(200, 9, 0, 0),
            new VoicePeerState(300, 11, 0, 0),
            new VoicePeerState(400, 1, 0, 0, muted: true)
        },
        new ulong[] { 200, 300, 400 });

    AssertSequence(new ulong[] { 200 }, recipients);
}

static void VoiceRecipientResolverFallsBackWhenLocalStateMissing()
{
    var resolver = new VoiceRecipientResolver(new VoiceSettings());

    var recipients = resolver.Resolve(
        100,
        VoiceChannel.Proximity,
        new[]
        {
            new VoicePeerState(200, 1, 0, 0)
        },
        new ulong[] { 200, 300 });

    AssertSequence(new ulong[] { 200, 300 }, recipients);
}

static void VoiceMuteListParsesCommandLinePeers()
{
    var muteList = VoiceMuteList.FromCommandLine(new[]
    {
        "--s1vc-muted-peer",
        "200",
        "--s1vc-muted-peers",
        "300, 0;bad;400",
        "--ignored",
        "500"
    });

    AssertEqual(3, muteList.Count);
    AssertTrue(muteList.IsMuted(200), "Single muted peer should be parsed.");
    AssertTrue(muteList.IsMuted(300), "Comma-separated muted peer should be parsed.");
    AssertTrue(muteList.IsMuted(400), "Semicolon-separated muted peer should be parsed.");
    AssertFalse(muteList.IsMuted(0), "Zero peer should not be muted.");
    AssertFalse(muteList.IsMuted(500), "Unrelated args should not be parsed.");
}

static void VoiceCaptureModePolicyPreservesPushToTalkDefault()
{
    var settings = new VoiceSettings
    {
        OpenMicEnabled = false
    };

    AssertFalse(VoiceCaptureModePolicy.IsOpenMicEnabled(forcedOpenMic: false, settings), "Push-to-talk mode should not be open mic.");
    AssertFalse(VoiceCaptureModePolicy.ShouldCapture(forcedOpenMic: false, settings, pushToTalkPressed: false), "Push-to-talk mode should stay idle until the key is pressed.");
    AssertTrue(VoiceCaptureModePolicy.ShouldCapture(forcedOpenMic: false, settings, pushToTalkPressed: true), "Push-to-talk mode should capture while the key is pressed.");
}

static void VoiceCaptureModePolicySupportsOpenMic()
{
    var settingsOpenMic = new VoiceSettings
    {
        OpenMicEnabled = true
    };
    var settingsPushToTalk = new VoiceSettings
    {
        OpenMicEnabled = false
    };

    AssertTrue(VoiceCaptureModePolicy.IsOpenMicEnabled(forcedOpenMic: false, settingsOpenMic), "Settings open mic should enable open mic.");
    AssertTrue(VoiceCaptureModePolicy.ShouldCapture(forcedOpenMic: false, settingsOpenMic, pushToTalkPressed: false), "Settings open mic should capture without PTT.");
    AssertTrue(VoiceCaptureModePolicy.IsOpenMicEnabled(forcedOpenMic: true, settingsPushToTalk), "Command-line forced open mic should override PTT settings.");
    AssertTrue(VoiceCaptureModePolicy.ShouldCapture(forcedOpenMic: true, settingsPushToTalk, pushToTalkPressed: false), "Forced open mic should capture without PTT.");
}

static void VoiceDiagnosticsReportsZeroPayloadsAndPcmEnergy()
{
    AssertTrue(VoiceDiagnostics.IsAllZero(Array.Empty<byte>()), "Empty payload should be treated as zero.");
    AssertTrue(VoiceDiagnostics.IsAllZero(new byte[] { 0, 0, 0, 0 }), "All-zero payload should be detected.");
    AssertFalse(VoiceDiagnostics.IsAllZero(new byte[] { 0, 0, 1, 0 }), "Non-zero payload should be detected.");
    AssertEqual(6L, VoiceDiagnostics.SumAbsolutePcm(new short[] { -1, 0, 2, -3 }));
    AssertEqual("01 0A FF", VoiceDiagnostics.Hex(new byte[] { 1, 10, 255 }, maxBytes: 8));
    AssertEqual("01 02", VoiceDiagnostics.Hex(new byte[] { 1, 2, 3 }, maxBytes: 2));
}

static void LiveVoiceRuntimePolicyGatesScenesAndHeadlessMode()
{
    AssertTrue(LiveVoiceRuntimePolicy.IsVoiceScene("Main"), "Main should allow live voice.");
    AssertTrue(LiveVoiceRuntimePolicy.IsVoiceScene("Tutorial"), "Tutorial should allow live voice.");
    AssertFalse(LiveVoiceRuntimePolicy.IsVoiceScene("Menu"), "Menu should not allow live voice.");
    AssertFalse(LiveVoiceRuntimePolicy.IsVoiceScene(null), "Unknown scene should not allow live voice.");
    AssertTrue(LiveVoiceRuntimePolicy.CanCreateLiveVoice(enabled: true, isBatchMode: false), "Interactive live voice should be creatable.");
    AssertFalse(LiveVoiceRuntimePolicy.CanCreateLiveVoice(enabled: true, isBatchMode: true), "Headless/batch mode should not create live voice.");
    AssertFalse(LiveVoiceRuntimePolicy.CanCreateLiveVoice(enabled: false, isBatchMode: false), "Disabled live voice should not be creatable.");
    AssertTrue(LiveVoiceRuntimePolicy.CanCreateInteractiveProbe(enabled: true, isBatchMode: false), "Interactive probes should be creatable in client mode.");
    AssertFalse(LiveVoiceRuntimePolicy.CanCreateInteractiveProbe(enabled: true, isBatchMode: true), "Interactive probes should not be creatable in headless mode.");
    AssertFalse(LiveVoiceRuntimePolicy.CanCreateInteractiveProbe(enabled: false, isBatchMode: false), "Disabled interactive probes should not be creatable.");
}

static void AssertTrue(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool value, string message)
{
    if (value)
        throw new InvalidOperationException(message);
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    if (!expectedArray.SequenceEqual(actualArray))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}].");
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception {typeof(TException).Name}.");
}

sealed class RecordingVoiceTransport : IVoiceTransport
{
    public event Action<ulong, VoicePacket>? OnPacket;

    public bool IsReady { get; set; } = true;
    public List<(ulong PeerId, VoicePacket Packet)> Sent { get; } = new();
    public List<VoicePacket> Broadcasts { get; } = new();

    public void SendTo(ulong peerId, VoicePacket packet)
    {
        Sent.Add((peerId, packet));
    }

    public void Broadcast(VoicePacket packet)
    {
        Broadcasts.Add(packet);
    }

    public void Poll()
    {
    }

    public void Raise(ulong senderPeerId, VoicePacket packet)
    {
        OnPacket?.Invoke(senderPeerId, packet);
    }

    public void Dispose()
    {
        IsReady = false;
    }
}

sealed class FakeVoiceCodec : IVoiceCodec
{
    public FakeVoiceCodec(int sampleRate, int channels, int frameSize = 960)
    {
        SampleRate = sampleRate;
        Channels = channels;
        FrameSize = frameSize;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int FrameSize { get; }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> destination)
    {
        var length = Math.Min(pcm.Length, destination.Length);
        for (var i = 0; i < length; i++)
            destination[i] = unchecked((byte)pcm[i]);

        return length;
    }

    public int Decode(ReadOnlySpan<byte> packet, Span<short> destination, bool useForwardErrorCorrection = false)
    {
        var length = Math.Min(packet.Length, destination.Length);
        for (var i = 0; i < length; i++)
            destination[i] = packet[i];

        return length / Channels;
    }

    public int DecodePacketLoss(Span<short> destination)
    {
        destination.Clear();
        return destination.Length / Channels;
    }

    public void Dispose()
    {
    }
}

sealed class RecordingSnlVoicePacketClient : ISnlVoicePacketClient
{
    public event Action<ulong, byte[]>? OnRawVoicePacket;

    public bool IsReady { get; set; } = true;
    public List<(ulong PeerId, byte[] Data)> Sent { get; } = new();
    public List<byte[]> Broadcasts { get; } = new();
    public int PollCount { get; private set; }

    public bool SendVoicePacket(ulong peerId, byte[] data)
    {
        Sent.Add((peerId, data));
        return true;
    }

    public void BroadcastVoicePacket(byte[] data)
    {
        Broadcasts.Add(data);
    }

    public void ProcessIncomingVoicePackets()
    {
        PollCount++;
    }

    public void Raise(ulong senderPeerId, byte[] data)
    {
        OnRawVoicePacket?.Invoke(senderPeerId, data);
    }
}
