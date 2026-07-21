using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using PCL.Core.App.Localization;

namespace PCL.Core.App.Plugins;

public enum PluginDeveloperTrustLevel
{
    Official,
    Local,
    Other
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
    public static PluginDeveloperTrustLevel GetTrustLevel(
        string? githubLogin,
        IEnumerable<PluginDeveloperRecord>? officialDevelopers,
        IEnumerable<string>? localAllowlist = null)
    {
        if (string.IsNullOrWhiteSpace(githubLogin)) return PluginDeveloperTrustLevel.Other;
        if ((officialDevelopers ?? []).Any(record =>
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
        try { return NormalizeLogins(Config.Plugin.TrustedGitHubLogins ?? []).ToArray(); }
        catch { return []; }
    }

    public static void AddLocal(string githubLogin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(githubLogin);
        var normalized = githubLogin.Trim();
        if (!IsValidGitHubLogin(normalized))
            throw new ArgumentException(Text("Plugins.Trust.Error.InvalidGitHubLogin", "GitHub Login 无效。"), nameof(githubLogin));
        var logins = GetLocalAllowlist().ToList();
        if (!logins.Contains(normalized, StringComparer.OrdinalIgnoreCase)) logins.Add(normalized);
        Config.Plugin.TrustedGitHubLogins = NormalizeLogins(logins).ToList();
    }

    public static bool RemoveLocal(string githubLogin)
    {
        if (string.IsNullOrWhiteSpace(githubLogin)) return false;
        var logins = GetLocalAllowlist().ToList();
        var removed = logins.RemoveAll(login =>
            string.Equals(login, githubLogin, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Config.Plugin.TrustedGitHubLogins = logins;
        return removed;
    }

    internal static IReadOnlyList<PluginDeveloperRecord> NormalizeSourceDevelopers(
        IEnumerable<PluginDeveloperRecord>? developers,
        bool officialSource)
    {
        var result = new List<PluginDeveloperRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var developer in developers ?? [])
        {
            if (developer is null) continue;
            var login = developer.GitHubLogin?.Trim();
            if (!IsValidGitHubLogin(login) || !seen.Add(login!)) continue;
            if (officialSource
                && !string.Equals(developer.Level, "official", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new PluginDeveloperRecord
            {
                GitHubLogin = login!,
                DisplayName = string.IsNullOrWhiteSpace(developer.DisplayName)
                    ? login
                    : developer.DisplayName.Trim(),
                Level = officialSource ? "official" : "trusted"
            });
        }
        return result;
    }

    private static IEnumerable<string> NormalizeLogins(IEnumerable<string> logins)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var login in logins)
        {
            if (string.IsNullOrWhiteSpace(login)) continue;
            var trimmed = login.Trim();
            if (IsValidGitHubLogin(trimmed) && seen.Add(trimmed)) yield return trimmed;
        }
    }

    private static bool IsValidGitHubLogin(string? login)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length > 39
            || login[0] == '-' || login[^1] == '-' || login.Contains("--", StringComparison.Ordinal))
            return false;
        return login.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static string Text(string key, string fallback, params object?[] args)
    {
        var template = Lang.Text(key);
        if (string.Equals(template, key, StringComparison.Ordinal)
            || string.Equals(template, $"!{key}!", StringComparison.Ordinal))
            template = fallback;
        return string.Format(Lang.Culture, template, args);
    }
}
