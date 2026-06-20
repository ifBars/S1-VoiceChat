using System;

namespace S1VoiceChat.Capture;

public interface IVoiceCapture : IDisposable
{
    bool IsCapturing { get; }
    int SampleRate { get; }
    int Channels { get; }

    void Start();
    void Stop();
    int ReadFrame(Span<short> destination);
}
