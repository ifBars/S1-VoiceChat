# Integration TODO

## SteamNetworkLib / SNL

Implement `SnlVoiceTransport` by binding to SteamNetworkLib custom message send/receive.

Requirements:

- Send serialized `VoicePacket` bytes.
- Use unreliable/no-delay where possible.
- Avoid JSON-per-frame for live voice.
- Prefer targeted sends for proximity voice.
- Support server relay mode for dedicated servers.

## Schedule I player state

Provide a player resolver that can map:

- local player Steam ID / SNL peer ID
- remote peer IDs
- player transforms/positions
- mute state
- alive/loaded state

Feed those into `ProximityRouter`.

## Unity microphone capture

Implement `IVoiceCapture` with Unity `Microphone` or a lower-level Windows capture path.

Rules:

- Capture mono 48 kHz if possible.
- Convert Unity float PCM to signed 16-bit PCM before Opus.
- Do not call Unity APIs from worker threads.
- Keep a ring buffer between Unity capture and the voice session.

## Unity playback

Implement a streaming `AudioClip` that pulls PCM from `RemoteVoiceStream.ReadPcm`.

Suggested model:

- one `AudioSource` per remote speaker
- spatial blend 1.0 for proximity voice
- 2D or filtered source for radio voice
- attach source to the remote player transform once resolved

## Dedicated server relay

Server should not decode audio.

Client sends compressed Opus packet to server. Server evaluates recipients and forwards the compressed packet unchanged.

## Moderation and safety features

- client mute
- server mute
- packet size cap
- per-peer rate limit
- push-to-talk by default
- diagnostics for zero PCM / zero Opus payloads
