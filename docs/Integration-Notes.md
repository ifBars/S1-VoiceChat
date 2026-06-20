# Integration Notes

## SteamNetworkLib / SNL

`SnlVoiceTransport` now has unit coverage through an adapter fake, and the MelonLoader bootstrap has runtime smoke coverage for SteamNetworkLib transport initialization in clean isolated installs:

- P2P Mono client.
- P2P Il2Cpp client.
- Dedicated Mono client.
- Dedicated Mono server.
- Dedicated Il2Cpp client.
- Dedicated Il2Cpp server.

Regular P2P packet exchange has been validated with the automated LocalLobby two-client smoke test on Mono and Il2Cpp. Dedicated relay packet exchange has been validated with the automated two-client plus server smoke test on Mono and Il2Cpp. The live path now uses WASAPI PCM capture and SteamNetworkLib raw packet delivery on logical P2P channel `3`.

The packet header now carries `SenderPeerId`, so a dedicated server can forward a voice packet while clients still key playback by the original speaker instead of the relay server. `VoiceRelayService` validates that the claimed sender matches the network sender before forwarding and drops spoofed packets.

Requirements:

- Keep sending serialized `VoicePacket` bytes.
- Keep using unreliable/no-delay where possible.
- Keep S1 voice traffic on a dedicated SteamNetworkLib logical P2P channel.
- Avoid JSON-per-frame for live voice.
- Prefer targeted sends for proximity voice.
- Support server relay mode for dedicated servers.
- Run runtime smoke profiles sequentially; isolated dedicated installs are too large to prepare in parallel on the current local disk.

## Schedule I player state

Current live client routing resolves:

- local player Steam ID / SNL peer ID
- remote peer IDs
- player transforms/positions from `Player.PlayerList`
- temporary command-line client mute state
- fallback remote lobby/session members when player state is not available

Future polish ideas:

- in-game client mute UI
- alive/loaded state
- tighter dedicated-server recipient resolution if server-authoritative proximity routing is required

## Voice capture

Current live mode uses WASAPI by default. Unity `Microphone`, native Opus, and Steam Voice remain experiment/probe paths only.

Rules:

- Keep live capture client-only and push-to-talk by default.
- Use WASAPI `auto` input selection by default, probing once per live capture instance and then reusing the selected endpoint.
- Let the Audio settings `Open Mic` toggle switch capture to continuous transmit while live voice is active.
- Do not create capture in batch/headless dedicated-server mode.
- Tear down capture, playback, and live packet subscriptions outside `Main` and `Tutorial`.
- Keep verbose capture/status diagnostics behind `S1VoiceChat.DiagnosticLogging` or explicit debug launch flags.
- Keep native Opus / Unity microphone / Steam Voice experiments separate from the current live runtime.

## Unity playback

Current live mode plays short PCM clips through `UnityVoicePlaybackSink`. Playback uses one `AudioSource` per remote speaker. Proximity, whisper, and shout packets are spatialized when the remote player's position can be resolved, and speaker source positions are refreshed from `Player.PlayerList` while live voice is active. Global packets remain 2D; radio packets remain 2D with a light low-pass filter. Received voice volume is controlled by the injected Audio settings `S1 Voice Chat` slider.

Future polish ideas:

- smooth positional updates if raw transform snapping is too noticeable in live play

## Dedicated server relay

Server should not decode audio.

Client sends voice packets through SteamNetworkLib. In dedicated sessions, SteamNetworkLib's `DedicatedRelay` compatibility path moves the same raw S1VoiceChat packets through DedicatedServerMod custom messaging. Server processes must not decode audio or create capture/playback/HUD state.

Current implementation:

- `VoicePacket.SenderPeerId` preserves original speaker identity across relay.
- `VoiceSession` keys remote streams by packet sender when a relay server is the network sender.
- `VoiceRelayService` validates and forwards compressed packets through `IVoiceTransport`.
- `VoiceRelayService` applies per-peer packet rate limiting before forwarding.
- SteamNetworkLib's `DedicatedRelay` compatibility path has two-client runtime smoke coverage through DedicatedServerMod on Mono and Il2Cpp.

Future polish idea:

- Decide whether dedicated servers should do server-authoritative proximity recipient filtering, or keep proximity recipient selection client-side with server relay only.

## Moderation and safety features

- client mute
- server mute
- packet size cap
- per-peer rate limit for live playback and dedicated relay
- push-to-talk by default
- opt-in diagnostics for zero capture/decode frames, decode failures, and rate-limited packets
