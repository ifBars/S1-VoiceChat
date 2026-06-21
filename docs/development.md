# Development workflow

This page is for contributors working on S1 VoiceChat locally.

The public README is intentionally user-facing. Keep build, validation, and architecture notes here unless they are needed by someone installing the mod.

## Build configurations

S1 VoiceChat ships two MelonLoader builds:

```powershell
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c MonoMelon
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c Il2CppMelon
```

`MonoMelon` targets `netstandard2.1`.

`Il2CppMelon` targets `net6.0`.

There is no CrossCompat build. The live mod references Mono and Il2Cpp game assemblies behind conditional compilation, so a single cross-compatible assembly is the wrong shape for this project.

Copy `src/S1VoiceChat/local.build.props.template` to `src/S1VoiceChat/local.build.props` when setting up a new machine. That file points the project at local Schedule I, MelonLoader, SteamNetworkLib, and generated Il2Cpp assemblies.

## Local validation

Run the unit harness:

```powershell
dotnet run --project tests/S1VoiceChat.Tests/S1VoiceChat.Tests.csproj -c Release
```

Run isolated install validation:

```powershell
tests/Run-S1VoiceChatValidation.ps1 -Scenario P2P -UseIsolatedInstalls
tests/Run-S1VoiceChatValidation.ps1 -Scenario Dedicated -UseIsolatedInstalls
```

The validation script builds both runtime targets, creates clean install profiles, deploys only expected files, and audits `Mods/` and `UserLibs/`.

The P2P profile expects only:

- S1VoiceChat
- SteamNetworkLib
- NAudio support DLLs
- `opus.dll`

The dedicated profile expects S1VoiceChat plus the matching DedicatedServerMod and S1API companion DLLs.

## Runtime smoke tests

Use these when transport behavior changes.

P2P LocalLobby smoke:

```powershell
tests/Run-S1VoiceChatTwoClientSmoke.ps1 -Runtime Mono
tests/Run-S1VoiceChatTwoClientSmoke.ps1 -Runtime Il2Cpp
```

Dedicated relay smoke:

```powershell
tests/Run-S1VoiceChatDedicatedRelaySmoke.ps1 -Runtime Mono
tests/Run-S1VoiceChatDedicatedRelaySmoke.ps1 -Runtime Il2Cpp
```

These tests are heavier than the validation gate. Run them sequentially. Isolated dedicated installs copy a lot of game files.

## Manual live voice checks

Install to a local game folder with:

```powershell
tests/Install-S1VoiceChatManualLocalLobby.ps1 -Runtime Il2Cpp -GamePath "D:\SteamLibrary\steamapps\common\Schedule I_public" -EnableLiveVoice
```

For proximity testing:

```powershell
tests/Install-S1VoiceChatManualLocalLobby.ps1 -Runtime Il2Cpp -GamePath "D:\SteamLibrary\steamapps\common\Schedule I_public" -EnableLiveVoice -VoiceChannel Proximity
```

Enable `S1VoiceChat.DiagnosticLogging` or launch with `--s1vc-debug-logs` only while diagnosing capture/playback. The diagnostic logs are intentionally noisy.

## WASAPI probe

Verify Windows capture without launching Schedule I:

```powershell
dotnet run --project tests/S1VoiceChat.WasapiProbe/S1VoiceChat.WasapiProbe.csproj -- --device auto --duration-ms 2000 --play-test-tone --require-nonzero
```

## Release packaging

Build both targets first:

```powershell
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c MonoMelon
dotnet build src/S1VoiceChat/S1VoiceChat.csproj -c Il2CppMelon
```

Release zips should contain only installable files:

```text
Mods/
S1VoiceChat.MonoMelon.dll or S1VoiceChat.Il2CppMelon.dll

UserLibs/
SteamNetworkLib.dll
NAudio.Core.dll
NAudio.Wasapi.dll
opus.dll

README.md
ICON-CREDITS.txt
```

Do not package Schedule I assemblies, generated Il2Cpp assemblies, AssetRipper exports, prefabs, scenes, textures, or local game files.

## CI notes

GitHub Actions builds both runtime targets using private reference-assembly repositories:

- `ifBars/schedule-one-assemblies` for Mono game and MelonLoader references.
- `ifBars/il2cpp-scheduleone-assemblies` for generated Il2Cpp and MelonLoader `net6` references.
- `ifBars/SteamNetworkLib`, built during the workflow so the packaged `UserLibs/SteamNetworkLib.dll` matches the target runtime.

The repository needs secrets that can read the private assembly repositories:

- `SCHEDULE_ONE_ASSEMBLIES_TOKEN` or `GAME_ASSEMBLIES_TOKEN`
- `IL2CPP_SCHEDULE_ONE_ASSEMBLIES_TOKEN` or `IL2CPP_ASSEMBLIES_TOKEN`

There is no CrossCompat CI build. The live mod references Mono and Il2Cpp game assemblies behind conditional compilation, so release CI builds `MonoMelon` and `Il2CppMelon` separately.

## Architecture notes

The live path uses:

- WASAPI microphone capture by default.
- Direct native Opus P/Invoke through `NativeOpusCodec`.
- PCM16 as a fallback/debug codec.
- Binary `VoicePacket` serialization.
- SteamNetworkLib raw packet transport.
- Dedicated-server relay that preserves the original speaker ID.
- Client-side proximity recipient selection with lobby-member fallback.
- Unity playback, with spatialization for proximity/whisper/shout when player positions are available.

See also:

- [Integration notes](Integration-Notes.md)
- [IL2CPP Opus notes](IL2CPP-Opus-Notes.md)
