using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarsXZMedia;

internal sealed class YtDlpUpdateInfo
{
    public bool CheckSucceeded { get; init; }
    public bool IsOutdated { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = YtDlpUpdateHelper.DefaultX86DownloadUrl;
    public string Message { get; init; } = string.Empty;
}

internal static class YtDlpUpdateHelper
{
    public const string AssetNameX86 = "yt-dlp_x86.exe";
    public const string DefaultX86DownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_x86.exe";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
    private static readonly string CachePath = Path.Combine(AppPaths.DataRoot, "yt-dlp-update-cache.json");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

    public static async Task<YtDlpUpdateInfo> CheckAsync(string ytDlpPath, CancellationToken token)
    {
        try
        {
            if (!TryGetLocalVersion(ytDlpPath, out var currentVersion))
            {
                return new YtDlpUpdateInfo
                {
                    CheckSucceeded = false,
                    CurrentVersion = string.Empty,
                    Message = "Не удалось определить локальную версию yt-dlp."
                };
            }

            var release = await GetLatestReleaseInfoAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(release.Version))
            {
                return new YtDlpUpdateInfo
                {
                    CheckSucceeded = false,
                    CurrentVersion = currentVersion,
                    LatestVersion = string.Empty,
                    Message = "Не удалось получить актуальную версию yt-dlp."
                };
            }

            bool isOutdated = IsRemoteVersionNewer(release.Version, currentVersion);
            return new YtDlpUpdateInfo
            {
                CheckSucceeded = true,
                IsOutdated = isOutdated,
                CurrentVersion = currentVersion,
                LatestVersion = release.Version,
                DownloadUrl = string.IsNullOrWhiteSpace(release.DownloadUrl) ? DefaultX86DownloadUrl : release.DownloadUrl,
                Message = isOutdated
                    ? $"yt-dlp устарел: {currentVersion} -> {release.Version}"
                    : $"yt-dlp уже актуален: {currentVersion}"
            };
        }
        catch (OperationCanceledException)
        {
            return new YtDlpUpdateInfo
            {
                CheckSucceeded = false,
                Message = "Проверка обновления yt-dlp была отменена."
            };
        }
        catch (Exception ex)
        {
            return new YtDlpUpdateInfo
            {
                CheckSucceeded = false,
                Message = ex.Message
            };
        }
    }

    public static bool TryGetLocalVersion(string ytDlpPath, out string version)
    {
        version = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(ytDlpPath) || !File.Exists(ytDlpPath))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(ytDlpPath) ?? AppPaths.AppDirectory
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            if (!process.WaitForExit(2500))
            {
                try { process.Kill(true); } catch { }
                return false;
            }

            string output = (process.StandardOutput.ReadToEnd() + "\n" + process.StandardError.ReadToEnd()).Trim();
            if (string.IsNullOrWhiteSpace(output))
                return false;

            version = NormalizeVersion(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty);
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            version = string.Empty;
            return false;
        }
    }

    private static async Task<(string Version, string DownloadUrl)> GetLatestReleaseInfoAsync(CancellationToken token)
    {
        if (TryReadCache(out var cachedVersion, out var cachedUrl, out var checkedAtUtc) &&
            DateTime.UtcNow - checkedAtUtc <= CacheLifetime)
        {
            return (cachedVersion, cachedUrl);
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MarsXZ Media");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await client.GetAsync(LatestReleaseApiUrl, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

            string version = string.Empty;
            string downloadUrl = DefaultX86DownloadUrl;

            if (doc.RootElement.TryGetProperty("tag_name", out var tagNameElement))
                version = NormalizeVersion(tagNameElement.GetString() ?? string.Empty);

            if (doc.RootElement.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nameElement) ? (nameElement.GetString() ?? string.Empty) : string.Empty;
                    if (!string.Equals(name, AssetNameX86, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (asset.TryGetProperty("browser_download_url", out var urlElement))
                    {
                        string candidate = urlElement.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(candidate))
                            downloadUrl = candidate;
                    }
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(version))
                TryWriteCache(version, downloadUrl, DateTime.UtcNow);

            return (version, downloadUrl);
        }
        catch
        {
            if (TryReadCache(out cachedVersion, out cachedUrl, out _))
                return (cachedVersion, cachedUrl);
            throw;
        }
    }

    private static bool TryReadCache(out string version, out string downloadUrl, out DateTime checkedAtUtc)
    {
        version = string.Empty;
        downloadUrl = DefaultX86DownloadUrl;
        checkedAtUtc = DateTime.MinValue;

        try
        {
            if (!File.Exists(CachePath))
                return false;

            string json = File.ReadAllText(CachePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            version = root.TryGetProperty("latestVersion", out var versionElement)
                ? NormalizeVersion(versionElement.GetString() ?? string.Empty)
                : string.Empty;
            downloadUrl = root.TryGetProperty("downloadUrl", out var urlElement)
                ? (urlElement.GetString() ?? DefaultX86DownloadUrl)
                : DefaultX86DownloadUrl;

            if (root.TryGetProperty("checkedAtUtc", out var checkedElement))
                DateTime.TryParse(checkedElement.GetString(), out checkedAtUtc);

            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    private static void TryWriteCache(string version, string downloadUrl, DateTime checkedAtUtc)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            var payload = new
            {
                latestVersion = NormalizeVersion(version),
                downloadUrl = string.IsNullOrWhiteSpace(downloadUrl) ? DefaultX86DownloadUrl : downloadUrl,
                checkedAtUtc = checkedAtUtc.ToString("O")
            };

            File.WriteAllText(CachePath, JsonSerializer.Serialize(payload));
        }
        catch
        {
        }
    }

    private static bool IsRemoteVersionNewer(string latestVersion, string currentVersion)
    {
        var left = ParseVersionParts(latestVersion);
        var right = ParseVersionParts(currentVersion);
        int count = Math.Max(left.Length, right.Length);

        for (int i = 0; i < count; i++)
        {
            int a = i < left.Length ? left[i] : 0;
            int b = i < right.Length ? right[i] : 0;
            if (a == b)
                continue;
            return a > b;
        }

        return false;
    }

    private static int[] ParseVersionParts(string version)
    {
        string normalized = NormalizeVersion(version);
        return normalized
            .Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .ToArray();
    }

    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        string value = version.Trim();
        int atIndex = value.LastIndexOf('@');
        if (atIndex >= 0 && atIndex < value.Length - 1)
            value = value[(atIndex + 1)..];

        if (value.StartsWith("stable", StringComparison.OrdinalIgnoreCase))
            value = value[6..].TrimStart('@', ':', ' ');

        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        return value.Trim();
    }
}
