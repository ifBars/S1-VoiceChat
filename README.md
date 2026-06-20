# S1 VoiceChat

Foundation/prototype for a Schedule I voice chat mod.

This repo is intentionally a starting point, not a finished mod. It is designed around an IL2CPP-friendly architecture that avoids OpusSharp and uses a minimal native `libopus` P/Invoke wrapper instead.

## Goals

- Proximity voice for Schedule I multiplayer.
- P2P support through SteamNetworkLib/SNL.
- Dedicated-server support through server relay.
- Native Opus compression suitable for IL2CPP.
- Small binary voice packets instead of JSON-per-frame stream messages.
- Clean separation between capture, codec, transport, routing, and playback.

## Current status

Implemented foundation:

- `IVoiceCodec` abstraction.
- `NativeOpusCodec` wrapper around native `opus.dll` / `libopus`.
- Binary `VoicePacket` encode/decode helpers.
- `IVoiceTransport` abstraction.
- Loopback transport for local pipeline testing.
- Placeholder SNL transport adapter.
- Jitter buffer.
- Ring buffer.
- Voice session coordinator.
- Basic settings model.
- IL2CPP-oriented notes.
- CI workflow for core compile checks.

Not implemented yet:

- Actual Schedule I player lookup.
- Actual SteamNetworkLib/SNL send/receive binding.
- Unity microphone capture implementation.
- Unity `AudioSource` / streaming `AudioClip` playback.
- Dedicated server relay integration.
- In-game UI, mute menu, push-to-talk binding, and speaking indicators.

## Build configurations

The project now follows the same configuration naming pattern as the S1API template:

```powershell
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c CrossCompat
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c MonoMelon
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c Il2CppMelon
```

`CrossCompat` and `MonoMelon` target `netstandard2.1`. `Il2CppMelon` targets `net6.0`, matching the common MelonLoader IL2CPP pattern.

Copy `src/S1VoiceChat/local.build.props.template` to `src/S1VoiceChat/local.build.props` when you start adding local Schedule I, MelonLoader, SteamNetworkLib, or assembly references.

## Suggested validation path

1. Test `NativeOpusCodec` locally with generated PCM, no networking.
2. Test loopback voice packets with encoded Opus frames.
3. Add Unity microphone capture.
4. Add local decode/playback.
5. Wire `SnlVoiceTransport` to SteamNetworkLib.
6. Add proximity routing.
7. Add dedicated server relay mode.

## Native Opus dependency

Do not use OpusSharp for IL2CPP. Ship a native Opus binary next to the mod, for example:

```text
Mods/S1VoiceChat/
├─ S1VoiceChat.dll
└─ opus.dll
```

On Windows, the P/Invoke target is currently `opus`. This usually resolves to `opus.dll`.

## Project layout

```text
src/S1VoiceChat/
├─ Capture/
├─ Codec/
├─ Network/
├─ Playback/
├─ Routing/
├─ Runtime/
└─ Utilities/
```

## Notes

The original SteamNetworkLib audio streaming example was designed for music-like streaming and was Mono-gated because OpusSharp had IL2CPP marshaling issues. This repo takes the safer approach: native Opus, manual packet serialization, small mono VOIP frames, and transport abstraction.
