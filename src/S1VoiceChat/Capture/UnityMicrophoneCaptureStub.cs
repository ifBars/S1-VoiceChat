using System;

namespace S1VoiceChat.Capture;

/// <summary>
/// Placeholder for the Unity Microphone implementation.
///
/// The final implementation should live in the Schedule I mod assembly where UnityEngine is
/// referenced. Keep this core project independent so the codec/network/session pieces can be
/// tested without Unity.
/// </summary>
public sealed class UnityMicrophoneCaptureStub : IVoiceCapture
{
    public bool IsCapturing { get; private set; }
    public int SampleRate { get; }
    public int Channels { get; }

    public UnityMicrophoneCaptureStub(int sampleRate = 48000, int channels = 1)
    {
        SampleRate = sampleRate;
        Channels = channels;
    }

    public void Start()
    {
        IsCapturing = true;
    }

    public void Stop()
    {
        IsCapturing = false;
    }

    public int ReadFrame(Span<short> destination)
    {
        destination.Clear();
        return 0;
    }

    public void Dispose()
    {
        Stop();
    }
}
