using System;
using System.Text;

namespace S1VoiceChat.Runtime;

public static class VoiceDiagnostics
{
    public static bool IsAllZero(ReadOnlySpan<byte> data, int bytesToCheck = 8)
    {
        if (data.Length == 0)
            return true;

        var count = Math.Min(data.Length, bytesToCheck);
        for (var i = 0; i < count; i++)
        {
            if (data[i] != 0)
                return false;
        }

        return true;
    }

    public static long SumAbsolutePcm(ReadOnlySpan<short> pcm)
    {
        long sum = 0;
        foreach (var sample in pcm)
            sum += Math.Abs((int)sample);

        return sum;
    }

    public static string Hex(ReadOnlySpan<byte> data, int maxBytes = 16)
    {
        var sb = new StringBuilder();
        var count = Math.Min(data.Length, maxBytes);

        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(' ');

            sb.Append(data[i].ToString("X2"));
        }

        return sb.ToString();
    }
}
