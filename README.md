# S1 VoiceChat

Voice chat mod for Schedule I.

The current live path uses WASAPI microphone capture, native Opus voice encoding, Unity playback, and SteamNetworkLib packet transport.

## Goals

- Proximity voice for Schedule I multiplayer.
- P2P support through SteamNetworkLib/SNL.
- Dedicated-server support through server relay.
- Live microphone capture suitable for Mono and IL2CPP.
- Small binary voice packets instead of JSON-per-frame stream messages.
- Clean separation between capture, codec, transport, routing, and playback.

## Current status

Implemented foundation:

- `IVoiceCodec` abstraction.
- `NativeOpusCodec` wrapper for production live voice.
- Binary `VoicePacket` encode/decode helpers.
- `IVoiceTransport` abstraction.
- Loopback transport for local pipeline testing.
- SteamNetworkLib raw packet transport adapter.
- Dedicated-server relay core that preserves original speaker identity and rejects spoofed sender IDs.
- Minimal MelonLoader bootstrap that initializes SteamNetworkLib and attaches the voice transport.
- Opt-in live runtime behind `--s1vc-live-voice`.
- WASAPI microphone capture with `auto` device selection that probes active inputs once and reuses the selected endpoint.
- Native Opus encoding by default, with `Pcm16Codec` retained as an explicit debug/fallback codec.
- Proximity recipient resolution from Schedule I player state with lobby-member fallback.
- Small push-to-talk HUD indicator in `Main` and `Tutorial` only.
- Audio settings integration for received voice volume and open-mic mode.
- Headless/batch-mode guard so dedicated server processes never create capture, playback, or HUD state.
- Runtime smoke probes for clean isolated P2P and dedicated server installs.
- Standalone WASAPI probe for verifying Windows capture endpoints without launching the game.
- Jitter buffer.
- Ring buffer.
- Voice session coordinator.
- Basic settings model.
- IL2CPP-oriented notes.
- CI workflow for core compile checks.

Future polish ideas:

- In-game mute menu, rebinding UI, and per-player speaking indicators.

## Build configurations

The project builds deployable Mono and Il2Cpp MelonLoader artifacts:

```powershell
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c MonoMelon
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c Il2CppMelon
```

`MonoMelon` targets `netstandard2.1`. `Il2CppMelon` targets `net6.0`, matching the common MelonLoader IL2CPP pattern. There is no cross-compatible runtime build because the live mod has Mono- and Il2Cpp-specific references behind conditional compilation.

Copy `src/S1VoiceChat/local.build.props.template` to `src/S1VoiceChat/local.build.props` when you start adding local Schedule I, MelonLoader, SteamNetworkLib, or assembly references.

## Suggested Validation Path

1. Run the core unit harness.
2. Build both `MonoMelon` and `Il2CppMelon`.
3. Run the native Opus and WASAPI probe tests.
4. Run isolated install validation for P2P and dedicated profiles.
5. Run the LocalLobby P2P smoke when transport behavior changes.
6. Run the DedicatedServerMod relay smoke when relay behavior changes.
7. For manual live checks, launch with `--s1vc-live-voice`; enable `--s1vc-debug-logs` only while diagnosing capture/playback.

## Automated validation

Run the core packet, routing, buffer, session, and SNL transport adapter checks with:

```powershell
dotnet run --project tests/S1VoiceChat.Tests/S1VoiceChat.Tests.csproj -c Release
```

Run the full local validation gate with:

```powershell
tests/Run-S1VoiceChatValidation.ps1
```

The full gate builds `MonoMelon` and `Il2CppMelon`, then inspects the local Mono/Il2Cpp installs before runtime testing. By default it checks both regular P2P and dedicated-server profiles:

```powershell
tests/Run-S1VoiceChatValidation.ps1 -Scenario P2P
tests/Run-S1VoiceChatValidation.ps1 -Scenario Dedicated
```

The P2P profile expects only the S1VoiceChat mod, `Mods/S1VoiceChat/assets`, SteamNetworkLib, and the WASAPI support libraries. The dedicated profile expects S1VoiceChat plus the matching DedicatedServerMod/S1API companion DLLs. Both profiles fail when `Mods/` is missing the expected DLLs/assets or contains extra mods that could contaminate voice-chat validation.

To copy only the built S1VoiceChat DLLs into the checked installs before auditing:

```powershell
tests/Run-S1VoiceChatValidation.ps1 -Scenario Dedicated -DeployVoiceChat
```

This does not remove or move other mods; extra `Mods/` entries still fail the clean-install audit.

For clean validation without touching the normal game installs, use isolated installs:

```powershell
tests/Run-S1VoiceChatValidation.ps1 -Scenario P2P -UseIsolatedInstalls
tests/Run-S1VoiceChatValidation.ps1 -Scenario Dedicated -UseIsolatedInstalls
```

Isolated mode creates per-run game copies under `artifacts/isolated-installs`, deploys only the expected DLLs for that profile, audits `Mods/` and `UserLibs`, then removes the generated copies after a passing run. Run the P2P and dedicated profiles separately; both build into the same project output directories.

Run the runtime smoke gate to launch a clean isolated game process and require SteamNetworkLib transport initialization:

```powershell
tests/Run-S1VoiceChatRuntimeSmoke.ps1 -Scenario P2P -Runtime Mono -Side Client -RequireTransport
tests/Run-S1VoiceChatRuntimeSmoke.ps1 -Scenario P2P -Runtime Il2Cpp -Side Client -RequireTransport
tests/Run-S1VoiceChatRuntimeSmoke.ps1 -Scenario Dedicated -Runtime Mono -Side Client -RequireTransport
tests/Run-S1VoiceChatRuntimeSmoke.ps1 -Scenario Dedicated -Runtime Mono -Side Server -RequireTransport
tests/Run-S1VoiceChatRuntimeSmoke.ps1 -Scenario Dedicated -Runtime Il2Cpp -Side Client -RequireTransport
tests/Run-S1VoiceChatRuntimeSmoke.ps1 -Scenario Dedicated -Runtime Il2Cpp -Side Server -RequireTransport
```

The runtime smoke script runs the isolated validation gate first, launches the selected clean profile with `--s1vc-smoke`, waits for a `PASS|TransportReady|...` result, captures MelonLoader logs, then removes the generated isolated install unless `-KeepIsolatedInstalls` is set. Run these commands sequentially; each isolated dedicated profile can copy enough game files that parallel runs may exhaust disk space.

Run the automated two-client P2P LocalLobby packet smoke with:

```powershell
tests/Run-S1VoiceChatTwoClientSmoke.ps1 -Runtime Mono
tests/Run-S1VoiceChatTwoClientSmoke.ps1 -Runtime Il2Cpp
```

This prepares a clean isolated install, installs LocalLobby, starts a host and client with separate Goldberg Steam IDs, loads a save through the host-side smoke probe, and requires the receiver to observe the sender's synthetic S1VoiceChat packet.

Run the automated dedicated relay packet smoke with:

```powershell
tests/Run-S1VoiceChatDedicatedRelaySmoke.ps1 -Runtime Mono
tests/Run-S1VoiceChatDedicatedRelaySmoke.ps1 -Runtime Il2Cpp
```

This prepares clean isolated dedicated server/client installs, audits `Mods/`, `UserLibs/`, and `Mods/S1VoiceChat/assets`, waits for the DedicatedServerMod ready marker, auto-connects two clients with `--server-ip` and `--server-port`, and requires the receiver to observe the sender's S1VoiceChat packet through SteamNetworkLib's dedicated relay path.

## Live Voice Mode

Live voice mode is opt-in:

```powershell
tests/Install-S1VoiceChatManualLocalLobby.ps1 -Runtime Il2Cpp -GamePath "D:\SteamLibrary\steamapps\common\Schedule I_public" -EnableLiveVoice
```

The generated launchers add `--s1vc-live-voice --s1vc-ptt-key V --s1vc-voice-channel Global --s1vc-codec Opus --s1vc-capture-source wasapi --s1vc-mic-device auto`. Hold `V` to transmit voice audio. Voice packets are sent as raw SteamNetworkLib packets on logical P2P channel `3`, using unreliable/no-delay delivery. The manual `F8` packet probe remains enabled in the same launchers.

Opus is the production default at a 24 kbps voice bitrate. Use `--s1vc-codec Pcm16` or `--s1vc-pcm16` only for diagnostics or as an emergency fallback when native Opus cannot load. Each voice packet carries its codec id, so control packets and fallback PCM packets are not decoded as Opus audio.

When the Schedule I settings screen is available, S1 VoiceChat adds two controls to the Audio settings panel:

- `S1 Voice Chat`: received voice playback volume, persisted as `S1VoiceChat.OutputVolume`.
- `Open Mic`: transmit continuously while live voice is active, persisted as `S1VoiceChat.OpenMic`.

Push-to-talk is still the default. `--s1vc-open-mic` remains available for launcher-driven testing and forces open mic for that process.

WASAPI is the default live capture source. The `auto` device selector probes active Windows capture endpoints once per live capture instance and chooses the endpoint producing nonzero PCM. This avoids repeatedly probing on every push-to-talk transition. Override it with `--s1vc-mic-device "<device name>"`, `--s1vc-mic-device-index <index>`, or `--s1vc-mic-device default`.

Verbose capture/status logging is off by default. Enable it with the Melon preference `S1VoiceChat.DiagnosticLogging=true` or a launch flag such as `--s1vc-debug-logs` when diagnosing input devices.

Verify Windows capture endpoints without launching Schedule I:

```powershell
dotnet run --project tests/S1VoiceChat.WasapiProbe/S1VoiceChat.WasapiProbe.csproj -- --device auto --duration-ms 2000 --play-test-tone --require-nonzero
```

For temporary client-side mute testing before the in-game mute menu exists, add `--s1vc-muted-peer <steamId>` for one peer or `--s1vc-muted-peers <steamId1,steamId2>` for a list. Muted peers are excluded from outgoing recipient selection.

The live HUD indicator uses `assets/microphone.png` and `assets/mute.png`. The installer copies them to `Mods/S1VoiceChat/assets`. The HUD and live voice capture/playback are gated to the `Main` and `Tutorial` scenes so voice chat is not active in the Menu.

The automated smoke tests prove the S1VoiceChat packet path, including P2P LocalLobby and DedicatedServerMod relay on Mono and Il2Cpp. The WASAPI probe verifies local Windows capture endpoints. In-game live capture is validated by launching with `--s1vc-live-voice` and checking for nonzero `Energy`/`CapturePeak` with diagnostic logging enabled.

For proximity routing:

```powershell
tests/Install-S1VoiceChatManualLocalLobby.ps1 -Runtime Il2Cpp -GamePath "D:\SteamLibrary\steamapps\common\Schedule I_public" -EnableLiveVoice -VoiceChannel Proximity
```

Proximity mode resolves `ScheduleOne.PlayerScripts.Player.PlayerList`, maps `PlayerCode` to Steam IDs, and routes to players inside `VoiceSettings.ProximityRangeMeters`. If player-position mapping is not available yet, the runtime falls back to remote lobby peers rather than dropping voice.

In dedicated-server sessions, SteamNetworkLib switches to its `DedicatedRelay` compatibility path and sends the same S1VoiceChat packets through DedicatedServerMod custom messaging. The server process relays bytes; it does not create microphone capture, audio playback, or the HUD.

## Native Opus Dependency

Do not use OpusSharp for IL2CPP live voice. S1 VoiceChat calls libopus directly through `NativeOpusCodec` and uses `OpusSharp.Natives` only as a source for the Windows native `opus.dll`. Installers copy `opus.dll` to `UserLibs` next to `NAudio.Core.dll`, `NAudio.Wasapi.dll`, and `SteamNetworkLib.dll`:

```text
UserLibs/
SteamNetworkLib.dll
NAudio.Core.dll
NAudio.Wasapi.dll
opus.dll
```

On Windows, the P/Invoke target is `opus`. `NativeOpusCodec` preloads `opus.dll` from the game root, `UserLibs`, the mod assembly directory, or `Mods/S1VoiceChat` before the first encode/decode call.

## Project layout

```text
src/S1VoiceChat/
Capture/
Codec/
Network/
Playback/
Routing/
Runtime/
Utilities/
```

## Notes

The original SteamNetworkLib audio streaming example was designed for music-like streaming and was Mono-gated because OpusSharp had IL2CPP marshaling issues. This repo takes the safer approach: direct native Opus P/Invoke, manual packet serialization, small mono VOIP frames, WASAPI capture, and transport abstraction.
