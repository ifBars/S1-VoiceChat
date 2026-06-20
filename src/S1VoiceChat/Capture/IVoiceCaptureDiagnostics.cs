namespace S1VoiceChat.Capture;

public interface IVoiceCaptureDiagnostics
{
    string DeviceLabel { get; }
    int LastReadPosition { get; }
    int LastAvailableSourceSamples { get; }
    float LastPeak { get; }
    string GetDiagnosticSummary();
}
