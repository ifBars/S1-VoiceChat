#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.IO;
using System.Reflection;
using MelonLoader;

namespace S1VoiceChat.Runtime;

internal static class EmbeddedHudIconAssets
{
    private const string ResourcePrefix = "S1VoiceChat.Assets.";

    public static byte[]? LoadIconBytes(string fileName, MelonLogger.Instance logger)
    {
        var extractedPath = TryExtract(fileName, logger);
        if (extractedPath != null)
            return File.ReadAllBytes(extractedPath);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream == null)
            return null;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? TryExtract(string fileName, MelonLogger.Instance logger)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName);
            if (stream == null)
                return null;

            var directory = Path.Combine(AppContext.BaseDirectory, "UserData", "S1VoiceChat", "assets");
            Directory.CreateDirectory(directory);

            var targetPath = Path.Combine(directory, fileName);
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length == stream.Length)
                return targetPath;

            using var output = File.Create(targetPath);
            stream.CopyTo(output);
            return targetPath;
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to extract embedded HUD icon '{fileName}': {ex.Message}");
            return null;
        }
    }
}
#endif
