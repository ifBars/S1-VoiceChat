# S1 VoiceChat

Voice chat for Schedule I multiplayer.

S1 VoiceChat adds push-to-talk voice, proximity routing, dedicated-server relay support, and a small in-game HUD indicator. It works with SteamNetworkLib and ships in separate Mono and Il2Cpp packages.

## Download

Download the package for your game runtime from the latest GitHub release:

- `S1VoiceChat-Il2Cpp-vX.X.X.zip` for the normal public/beta Schedule I branches.
- `S1VoiceChat-Mono-vX.X.X.zip` for the alternate Mono branches.

If you are not sure which one you need, start with the Il2Cpp package. Most current public installs use Il2Cpp.

## Requirements

- Schedule I with MelonLoader installed.
- SteamNetworkLib installed through the included `UserLibs/SteamNetworkLib.dll`.
- Windows, for the default WASAPI microphone capture path.
- A working microphone selected in Windows.

The release zip includes the voice-chat DLL and required `UserLibs` dependencies:

```text
Mods/
S1VoiceChat.Il2CppMelon.dll or S1VoiceChat.MonoMelon.dll

UserLibs/
SteamNetworkLib.dll
NAudio.Core.dll
NAudio.Wasapi.dll
opus.dll
```

The HUD icons are embedded in the mod assembly. You do not need to install separate PNG files.

## Install

1. Close the game.
2. Download the Mono or Il2Cpp zip that matches your game branch.
3. Extract the zip into your Schedule I game folder.
4. Make sure the zip merged into the existing `Mods/` and `UserLibs/` folders.
5. Start the game.

For example, after installing the Il2Cpp package you should have:

```text
Schedule I/
Mods/S1VoiceChat.Il2CppMelon.dll
UserLibs/SteamNetworkLib.dll
UserLibs/NAudio.Core.dll
UserLibs/NAudio.Wasapi.dll
UserLibs/opus.dll
```

## Basic use

- Hold `V` to talk.
- Release `V` to stop transmitting.
- Voice chat only runs in the `Main` and `Tutorial` scenes. It does not run in the main menu.
- By default, voice uses proximity chat. Nearby players hear you; far-away players do not.
- Dedicated server processes relay voice packets but do not record or play audio.

The HUD indicator appears while in-game so you can tell whether push-to-talk is idle, muted, or transmitting.

## In-game settings

Open `Settings > Audio`.

S1 VoiceChat adds:

- `Voice Chat Volume`: received voice playback volume.
- `Open Mic`: when enabled, your mic transmits continuously while live voice is active.

Push-to-talk is the default and is safer for normal play. Use Open Mic only if you actually want continuous transmit.

## MelonPreferences

The mod creates a `S1VoiceChat` MelonPreferences category. You can edit these from MelonLoader preference files or any preferences UI you use.

Common options:

| Preference | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Enables live voice on interactive clients. |
| `OutputVolume` | `100` | Received voice volume percent. Also controlled by the Audio settings slider. |
| `OpenMic` | `false` | Transmit continuously instead of requiring push-to-talk. Also controlled by the Audio settings toggle. |
| `PushToTalkKey` | `V` | Unity `KeyCode` name for push-to-talk. |
| `VoiceChannel` | `Proximity` | Routing mode: `Proximity`, `Whisper`, `Shout`, `Radio`, or `Global`. |
| `CaptureSource` | `Wasapi` | Capture backend: `Wasapi`, `Microphone`, or `Tone`. |
| `MicrophoneDevice` | `auto` | Capture device. Use `auto`, `default`, an index, or a device name. |
| `Codec` | `Opus` | Voice codec. Use `Pcm16` only as a fallback/debug option. |
| `OpusBitrate` | `24000` | Opus bitrate in bits per second. |
| `ProximityRangeMeters` | `25` | Normal proximity range. |
| `WhisperRangeMeters` | `6` | Whisper range. |
| `ShoutRangeMeters` | `45` | Shout range. |
| `DiagnosticLogging` | `false` | Extra capture/playback logs for troubleshooting. Leave this off during normal play. |

## Launch flags

Most users do not need launch flags. They are useful when testing a specific setup.

```text
--s1vc-live-voice
--s1vc-disable-live-voice
--s1vc-open-mic
--s1vc-ptt-key V
--s1vc-voice-channel Proximity
--s1vc-codec Opus
--s1vc-codec Pcm16
--s1vc-capture-source wasapi
--s1vc-mic-device auto
--s1vc-debug-logs
```

Launch flags override preferences for that game process.

## Troubleshooting

If nobody can hear you:

1. Confirm you installed the package that matches your runtime.
2. Check that `SteamNetworkLib.dll`, `NAudio.Core.dll`, `NAudio.Wasapi.dll`, and `opus.dll` are in `UserLibs/`.
3. Check Windows microphone permissions and input device selection.
4. Load into a save. Voice chat is disabled in the menu.
5. Hold `V` while speaking, unless Open Mic is enabled.
6. Temporarily enable `DiagnosticLogging` or launch with `--s1vc-debug-logs`, then check the MelonLoader log.

If capture logs show silence, set `MicrophoneDevice` to `default` or to the exact Windows device name instead of `auto`.

If the game fails to load the mod, make sure you did not mix the Mono DLL into an Il2Cpp install or the Il2Cpp DLL into a Mono install.

## Credits

Icon credits are included in the release zip as `ICON-CREDITS.txt`.

Mute and unmute icons are by Kiranshastry from Flaticon.

## Developer docs

Developer notes live in `docs/`:

- [Development workflow](docs/development.md)
- [Integration notes](docs/Integration-Notes.md)
- [IL2CPP Opus notes](docs/IL2CPP-Opus-Notes.md)
