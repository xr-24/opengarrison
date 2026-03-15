using System;
using System.IO;
using System.Net.Http;
using GG2.Core;

namespace GG2.Client;

internal static class CustomMapSyncService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static bool EnsureMapAvailable(
        string levelName,
        bool isCustomMap,
        string mapDownloadUrl,
        string mapContentHash,
        out string error)
    {
        error = string.Empty;
        if (!isCustomMap)
        {
            return true;
        }

        if (!CustomMapLocatorStore.TryNormalizeLevelName(levelName, out var normalizedLevelName))
        {
            error = $"Invalid custom map name: {levelName}";
            return false;
        }

        var expectedHash = CustomMapHashService.NormalizeHash(mapContentHash);
        var mapPath = CustomMapLocatorStore.GetMapPath(normalizedLevelName);
        if (File.Exists(mapPath))
        {
            if (expectedHash.Length == 0)
            {
                CacheLocator(normalizedLevelName, mapDownloadUrl);
                return true;
            }

            var currentHash = CustomMapHashService.ComputeSha256(mapPath);
            if (string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                CacheLocator(normalizedLevelName, mapDownloadUrl);
                return true;
            }
        }

        var downloadUrl = ResolveDownloadUrl(normalizedLevelName, mapDownloadUrl);
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            error = $"Missing map {normalizedLevelName}. Server did not provide a download URL.";
            return false;
        }

        if (!TryDownloadMap(downloadUrl, mapPath, expectedHash, out error))
        {
            return false;
        }

        CacheLocator(normalizedLevelName, downloadUrl);
        return true;
    }

    private static string ResolveDownloadUrl(string levelName, string mapDownloadUrl)
    {
        if (!string.IsNullOrWhiteSpace(mapDownloadUrl))
        {
            return mapDownloadUrl.Trim();
        }

        return CustomMapLocatorStore.TryReadMapUrl(levelName) ?? string.Empty;
    }

    private static bool TryDownloadMap(string mapDownloadUrl, string mapPath, string expectedHash, out string error)
    {
        error = string.Empty;
        if (!Uri.TryCreate(mapDownloadUrl, UriKind.Absolute, out var mapUri))
        {
            error = $"Invalid map download URL: {mapDownloadUrl}";
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
        var tempPath = mapPath + ".download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, mapUri);
            using var response = HttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                error = $"Map download failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
                return false;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType)
                && !mediaType.Contains("png", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Map download returned unsupported content type: {mediaType}.";
                return false;
            }

            using (var networkStream = response.Content.ReadAsStream())
            using (var fileStream = File.Create(tempPath))
            {
                networkStream.CopyTo(fileStream);
            }

            if (expectedHash.Length > 0)
            {
                var downloadedHash = CustomMapHashService.ComputeSha256(tempPath);
                if (!string.Equals(downloadedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Downloaded map hash does not match the server hash.";
                    return false;
                }
            }

            File.Move(tempPath, mapPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Map download failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private static void CacheLocator(string levelName, string mapDownloadUrl)
    {
        if (!string.IsNullOrWhiteSpace(mapDownloadUrl))
        {
            CustomMapLocatorStore.WriteMapUrl(levelName, mapDownloadUrl.Trim());
        }
    }
}
