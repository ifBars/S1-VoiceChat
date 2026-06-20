# IL2CPP Opus Notes

This project intentionally avoids OpusSharp for the IL2CPP path.

## Known issue from the older SteamNetworkLib audio example

The older audio streaming experiment was useful architecturally, but the OpusSharp layer was not reliable under IL2CPP. The common symptom was encoded/decoded buffers that appeared to become zeroed or invalid after crossing managed/native boundaries.

## Current approach

Use direct P/Invoke against native Opus:

- `opus_encoder_create`
- `opus_encode`
- `opus_decoder_create`
- `opus_decode`
- `opus_encoder_destroy`
- `opus_decoder_destroy`

This keeps the interop surface small and predictable.

## First validation test

Before touching Schedule I, SteamNetworkLib, or Unity microphone input:

1. Generate a 440 Hz PCM test frame.
2. Encode with `NativeOpusCodec`.
3. Assert encoded length is positive.
4. Assert encoded bytes are not all zero.
5. Decode back to PCM.
6. Assert decoded PCM has non-zero amplitude.

Only after that should the pipeline move to microphone capture and network transport.

## Recommended voice settings

```text
Sample rate: 48000 Hz
Channels: 1
Frame size: 960 samples
Frame duration: 20 ms
Bitrate: TODO via opus_encoder_ctl
Transport: unreliable/no-delay
Jitter target: 3 packets
```

## Native binary placement

For Windows mod builds, ship `opus.dll` beside the mod DLL or ensure the mod folder is in the DLL search path before constructing `NativeOpusCodec`.
