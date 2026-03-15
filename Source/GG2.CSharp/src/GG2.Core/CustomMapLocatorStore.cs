using System;
using System.IO;

namespace GG2.Core;

public static class CustomMapLocatorStore
{
    public static string? TryReadMapUrl(string levelName)
    {
        if (!TryNormalizeLevelName(levelName, out var normalizedLevelName))
        {
            return null;
        }

        var locatorPath = GetLocatorPath(normalizedLevelName);
        if (!File.Exists(locatorPath))
        {
            return null;
        }

        var locator = File.ReadAllText(locatorPath).Trim();
        return string.IsNullOrWhiteSpace(locator) ? null : locator;
    }

    public static void WriteMapUrl(string levelName, string mapUrl)
    {
        if (!TryNormalizeLevelName(levelName, out var normalizedLevelName))
        {
            return;
        }

        var trimmedUrl = mapUrl.Trim();
        if (trimmedUrl.Length == 0)
        {
            return;
        }

        var locatorPath = GetLocatorPath(normalizedLevelName);
        Directory.CreateDirectory(Path.GetDirectoryName(locatorPath)!);
        File.WriteAllText(locatorPath, trimmedUrl);
    }

    public static string GetMapPath(string levelName)
    {
        var normalized = TryNormalizeLevelName(levelName, out var safeLevelName)
            ? safeLevelName
            : levelName.Trim();
        return Path.Combine(RuntimePaths.MapsDirectory, $"{normalized}.png");
    }

    private static string GetLocatorPath(string normalizedLevelName)
    {
        return Path.Combine(RuntimePaths.MapsDirectory, $"{normalizedLevelName}.locator");
    }

    public static bool TryNormalizeLevelName(string? levelName, out string normalizedLevelName)
    {
        normalizedLevelName = string.Empty;
        if (string.IsNullOrWhiteSpace(levelName))
        {
            return false;
        }

        var trimmed = levelName.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        normalizedLevelName = trimmed;
        return true;
    }
}
