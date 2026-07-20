using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.App.Plugins;

public enum PluginDeveloperTrustLevel
{
    Official,
    Local,
    Other
}

public sealed class PluginDeveloperAllowlist
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("developers")]
    public List<PluginDeveloperRecord> Developers { get; set; } = [];
}

public sealed class PluginDeveloperRecord
{
    [JsonPropertyName("githubLogin")]
    public string GitHubLogin { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }
}

public static class PluginDeveloperTrustService
{
    public const string OfficialAllowlistUrl =
        "https://cdn.jsdelivr.net/gh/PCL-Nex-Developer/Nex_Server@main/apiv2/developers.json";

    public static async Task<PluginDeveloperAllowlist> FetchOfficialAsync(
        HttpClient? httpClient = null,
        string? cachePath = null,
        CancellationToken ct = default)
    {
        httpClient ??= NetworkService.GetClient();
        cachePath ??= Path.Combine(Paths.PluginTrust, "developers.json");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var json = await FetchStringWithFallbackAsync(httpClient, timeout.Token).ConfigureAwait(false);
            var allowlist = Deserialize(json);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, json);
            return allowlist;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            try
            {
                if (File.Exists(cachePath)) return Deserialize(File.ReadAllText(cachePath));
            }
            catch { }
            return new PluginDeveloperAllowlist();
        }
    }

    public static PluginDeveloperTrustLevel GetTrustLevel(
        string? githubLogin,
        PluginDeveloperAllowlist? officialAllowlist,
        IEnumerable<string>? localAllowlist = null)
    {
        if (string.IsNullOrWhiteSpace(githubLogin)) return PluginDeveloperTrustLevel.Other;
        if ((officialAllowlist?.Developers ?? []).Any(record =>
                string.Equals(record.GitHubLogin, githubLogin, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.Level, "official", StringComparison.OrdinalIgnoreCase)))
            return PluginDeveloperTrustLevel.Official;
        if ((localAllowlist ?? GetLocalAllowlist()).Any(login =>
                string.Equals(login, githubLogin, StringComparison.OrdinalIgnoreCase)))
            return PluginDeveloperTrustLevel.Local;
        return PluginDeveloperTrustLevel.Other;
    }

    public static IReadOnlyList<PluginRepositoryEntry> FilterVisible(
        IEnumerable<PluginRepositoryEntry> entries,
        bool showNonWhitelistedDevelopers)
    {
        return entries
            .Where(entry => showNonWhitelistedDevelopers
                            || entry.DeveloperTrustLevel != PluginDeveloperTrustLevel.Other)
            .ToList();
    }

    public static IReadOnlyList<string> GetLocalAllowlist()
    {
        try { return Normalize(Config.Plugin.TrustedGitHubLogins ?? []).ToArray(); }
        catch { return []; }
    }

    public static void AddLocal(string githubLogin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(githubLogin);
        var logins = GetLocalAllowlist().ToList();
        if (!logins.Contains(githubLogin.Trim(), StringComparer.OrdinalIgnoreCase)) logins.Add(githubLogin.Trim());
        Config.Plugin.TrustedGitHubLogins = Normalize(logins).ToList();
    }

    public static bool RemoveLocal(string githubLogin)
    {
        if (string.IsNullOrWhiteSpace(githubLogin)) return false;
        var logins = GetLocalAllowlist().ToList();
        var removed = logins.RemoveAll(login => string.Equals(login, githubLogin, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Config.Plugin.TrustedGitHubLogins = logins;
        return removed;
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> logins)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var login in logins)
        {
            if (string.IsNullOrWhiteSpace(login)) continue;
            var trimmed = login.Trim();
            if (seen.Add(trimmed)) yield return trimmed;
        }
    }

    private static async Task<string> FetchStringWithFallbackAsync(HttpClient client, CancellationToken ct)
    {
        Exception? lastError = null;
        var candidates = GitHubAccelerator.GetRequestCandidatesByConfig(OfficialAllowlistUrl);
        for (var index = 0; index < candidates.Count; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidates[index]);
            request.Headers.TryAddWithoutValidation("User-Agent", $"PCL-Nex/{Basics.VersionName}");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            try
            {
                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode && index < candidates.Count - 1) continue;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { lastError = ex; }
        }
        throw lastError ?? new HttpRequestException("Developer allowlist request failed.");
    }

    private static PluginDeveloperAllowlist Deserialize(string json)
    {
        var result = JsonSerializer.Deserialize<PluginDeveloperAllowlist>(json, PluginJson.SerializerOptions)
            ?? new PluginDeveloperAllowlist();
        if (result.Version != 1 || result.Developers is null)
            throw new InvalidDataException("Developer allowlist has an unsupported or malformed schema.");
        result.Developers = result.Developers
            .Where(record => !string.IsNullOrWhiteSpace(record.GitHubLogin)
                             && string.Equals(record.Level, "official", StringComparison.OrdinalIgnoreCase))
            .GroupBy(record => record.GitHubLogin.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return result;
    }
}
