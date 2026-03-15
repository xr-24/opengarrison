using System;
using System.Collections.Generic;
using System.IO;

namespace GG2.Core;

public readonly record struct CustomMapDescriptor(
    string LevelName,
    string LocalFilePath,
    string SourceUrl,
    string ContentHash);

public static class CustomMapDescriptorResolver
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, CachedDescriptor> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryResolve(string levelName, out CustomMapDescriptor descriptor)
    {
        descriptor = default;
        if (!CustomMapLocatorStore.TryNormalizeLevelName(levelName, out var normalizedLevelName))
        {
            return false;
        }

        var mapPath = CustomMapLocatorStore.GetMapPath(normalizedLevelName);
        if (!File.Exists(mapPath))
        {
            return false;
        }

        var lastWriteUtcTicks = File.GetLastWriteTimeUtc(mapPath).Ticks;
        lock (Sync)
        {
            if (Cache.TryGetValue(normalizedLevelName, out var cached)
                && cached.LastWriteUtcTicks == lastWriteUtcTicks)
            {
                descriptor = cached.Descriptor;
                return true;
            }
        }

        var hash = CustomMapHashService.ComputeSha256(mapPath);
        var sourceUrl = CustomMapLocatorStore.TryReadMapUrl(normalizedLevelName) ?? string.Empty;
        var resolved = new CustomMapDescriptor(normalizedLevelName, mapPath, sourceUrl, hash);
        lock (Sync)
        {
            Cache[normalizedLevelName] = new CachedDescriptor(lastWriteUtcTicks, resolved);
        }

        descriptor = resolved;
        return true;
    }

    private sealed record CachedDescriptor(long LastWriteUtcTicks, CustomMapDescriptor Descriptor);
}
