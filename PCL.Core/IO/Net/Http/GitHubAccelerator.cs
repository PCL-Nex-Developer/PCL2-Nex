using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.IO.Net;

namespace PCL.Core.IO.Net.Http;

public static class GitHubAccelerator
{
    public const string SpeedTestPath = "/__speedtest/download?size=26214400";

    private static readonly string[] MirrorPrefixes =
    [
        "https://gh-proxy.org/",
        "https://v4.gh-proxy.org/",
        "https://v6.gh-proxy.org/",
        "https://cdn.gh-proxy.org/"
    ];

    public static IReadOnlyList<string> Mirrors => MirrorPrefixes;

    public static IReadOnlyList<string> SupportedDomains { get; } =
    [
        "github.com",
        "api.github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "gist.githubusercontent.com",
        "avatars.githubusercontent.com"
    ];

    public static string RewriteByConfig(string url)
    {
        return Rewrite(url, GetConfiguredMirror(), GetConfiguredDomains());
    }

    public static string Rewrite(string url, int mirror)
        => Rewrite(url, mirror, SupportedDomains);

    public static string Rewrite(string url, int mirror, IEnumerable<string>? enabledDomains)
    {
        if (mirror <= 0 || mirror > MirrorPrefixes.Length) return url;
        if (string.IsNullOrWhiteSpace(url)) return url;

        var mirrorPrefix = MirrorPrefixes[mirror - 1];
        if (IsAccelerated(url)) return url;
        if (!ShouldRewrite(url, enabledDomains)) return url;

        return mirrorPrefix + url;
    }

    public static bool ShouldRewrite(string url)
        => ShouldRewrite(url, SupportedDomains);

    public static bool ShouldRewrite(string url, IEnumerable<string>? enabledDomains)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        var selected = new HashSet<string>(enabledDomains ?? [], StringComparer.OrdinalIgnoreCase);
        return selected.Contains(uri.Host) && SupportedDomains.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> GetRequestCandidates(string url, int mirror)
    {
        var rewritten = Rewrite(url, mirror);
        return string.Equals(rewritten, url, StringComparison.OrdinalIgnoreCase)
            ? [url]
            : [rewritten, url];
    }

    public static IReadOnlyList<string> GetRequestCandidatesByConfig(string url, int? mirror = null)
    {
        var rewritten = Rewrite(url, mirror ?? GetConfiguredMirror(), GetConfiguredDomains());
        return string.Equals(rewritten, url, StringComparison.OrdinalIgnoreCase)
            ? [url]
            : [rewritten, url];
    }

    public static IReadOnlyList<string> GetConfiguredDomains()
    {
        try
        {
            var value = Config.Download.PluginGitAcceleratedDomains ?? string.Empty;
            return value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(domain => SupportedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            // 配置系统尚未初始化的测试/早期启动阶段保持原有“全部支持域名”行为。
            return SupportedDomains;
        }
    }

    public static void SetConfiguredDomains(IEnumerable<string> domains)
    {
        Config.Download.PluginGitAcceleratedDomains = string.Join('|', domains
            .Where(domain => SupportedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static int GetConfiguredMirror()
    {
        try { return Config.Download.PluginGitMirror; }
        catch { return 0; }
    }

    public static bool IsAccelerated(string url)
    {
        return MirrorPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetSpeedTestUrl(int mirror)
    {
        if (mirror <= 0 || mirror > MirrorPrefixes.Length)
            throw new ArgumentOutOfRangeException(nameof(mirror));

        return MirrorPrefixes[mirror - 1].TrimEnd('/') + SpeedTestPath;
    }

    public static async Task<GitHubAcceleratorSpeedTestResult> TestMirrorAsync(
        int mirror,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var url = GetSpeedTestUrl(mirror);
        var stopwatch = Stopwatch.StartNew();
        long bytesRead = 0;

        try
        {
            using var timeoutCts = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (timeoutCts is not null && timeout is { } timeoutValue)
                timeoutCts.CancelAfter(timeoutValue);
            var token = timeoutCts?.Token ?? cancellationToken;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await NetworkService.GetClient()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                bytesRead += read;

            stopwatch.Stop();
            return new GitHubAcceleratorSpeedTestResult(mirror, MirrorPrefixes[mirror - 1], stopwatch.Elapsed, bytesRead, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new GitHubAcceleratorSpeedTestResult(mirror, MirrorPrefixes[mirror - 1], stopwatch.Elapsed, bytesRead, ex);
        }
    }

    public static async Task<GitHubAcceleratorSpeedTestResult?> FindFastestMirrorAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = Enumerable.Range(1, MirrorPrefixes.Length)
            .Select(mirror => TestMirrorAsync(mirror, timeout, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .Where(result => result.IsSuccess)
            .OrderByDescending(result => result.BytesPerSecond)
            .FirstOrDefault();
    }
}

public sealed record GitHubAcceleratorSpeedTestResult(
    int Mirror,
    string MirrorUrl,
    TimeSpan Elapsed,
    long BytesRead,
    Exception? Error)
{
    public bool IsSuccess => Error is null && BytesRead > 0;

    public double BytesPerSecond => IsSuccess && Elapsed.TotalSeconds > 0
        ? BytesRead / Elapsed.TotalSeconds
        : 0;
}
