using System;

namespace S1VoiceChat.Utilities;

public static class TestToneGenerator
{
    public static void FillSineWave(Span<short> destination, int sampleRate, int channels, double frequencyHz = 440.0, double amplitude = 0.25)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        var frames = destination.Length / channels;
        var max = short.MaxValue * amplitude;

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)(Math.Sin(2.0 * Math.PI * frequencyHz * frame / sampleRate) * max);
            for (var channel = 0; channel < channels; channel++)
                destination[(frame * channels) + channel] = value;
        }
    }
}
