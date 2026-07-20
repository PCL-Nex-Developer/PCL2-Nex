using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;
using PCL.Core.Utils;

namespace PCL.Core.App.Plugins;

public sealed class PluginUpdateCandidate
{
    public required PluginInstallRecord Installed { get; init; }

    public required PluginRepositoryEntry Entry { get; init; }

    public required PluginInstallSourceEntry Source { get; init; }

    public required PluginMarketVersion ManifestVersion { get; init; }

    public required string LatestVersion { get; init; }
}

public static class PluginUpdateService
{
    public static async Task<IReadOnlyList<PluginRepositoryIndex>> FetchEnabledIndexesAsync(CancellationToken ct = default)
    {
        var market = await PluginMarketplaceService.LoadAsync(new PluginMarketQueryOptions
        {
            GitHubToken = Config.Plugin.GitHubToken,
            IncludeArchived = Config.Plugin.ShowArchivedRepositories,
            IncludeDisabled = Config.Plugin.ShowDisabledRepositories,
            IncludeForks = Config.Plugin.ShowForkRepositories
        }, ct: ct).ConfigureAwait(false);
        return
        [
            new PluginRepositoryIndex { Name = "Configured Plugin Marketplace", Plugins = market.Entries.ToList() }
        ];
    }

    public static IReadOnlyList<PluginUpdateCandidate> FindUpdates(
        IReadOnlyList<PluginRepositoryEntry> entries,
        IReadOnlyDictionary<string, PluginInstallRecord> installed)
    {
        var bestByPlugin = new Dictionary<string, PluginUpdateCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!installed.TryGetValue(entry.Id, out var record)) continue;
            if (!MatchesInstalledSource(record, entry)) continue;
            if (!TryGetDisplayVersion(entry, out var latestVersion)) continue;
            if (CompareVersion(latestVersion, record.InstalledVersion) <= 0) continue;

            var source = PluginRepositoryService.GetInstallSources(entry).FirstOrDefault();
            if (source is null) continue;

            var candidate = new PluginUpdateCandidate
            {
                Installed = record,
                Entry = entry,
                Source = source,
                ManifestVersion = entry.SelectedVersion ?? new PluginMarketVersion
                {
                    PluginId = entry.Id,
                    Version = entry.Version,
                    ResolvedPackageUrl = source.Url,
                    ResolvedSha256 = source.Sha256
                },
                LatestVersion = latestVersion
            };

            if (!bestByPlugin.TryGetValue(record.PluginId, out var existing) || CompareVersion(candidate.LatestVersion, existing.LatestVersion) > 0)
                bestByPlugin[record.PluginId] = candidate;
        }

        return bestByPlugin.Values
            .OrderBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<PluginUpdateCandidate> FindUpdates(IReadOnlyList<PluginRepositoryEntry> entries)
    {
        return FindUpdates(entries, GetInstalledPluginRecords());
    }

    public static IReadOnlyDictionary<string, PluginInstallRecord> GetInstalledPluginRecords()
    {
        var installed = new Dictionary<string, PluginInstallRecord>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var record in PluginInstallService.GetInstalledPlugins())
                installed[record.PluginId] = record;
        }
        catch { }

        return installed;
    }

    /// <summary>
    /// 从 Topic 市场条目或仓库根 manifest.json 获取实际最新版本号。
    /// </summary>
    public static async Task<string?> FetchLatestVersionAsync(PluginRepositoryEntry entry, CancellationToken ct = default)
    {
        var latest = await FetchLatestManifestVersionAsync(entry, ct).ConfigureAwait(false);
        return latest?.Version;
    }

    public sealed class PluginLatestManifestVersion
    {
        public required PluginMarketVersion ManifestVersion { get; init; }

        public required string Version { get; init; }
    }

    public static async Task<PluginLatestManifestVersion?> FetchLatestManifestVersionAsync(PluginRepositoryEntry entry, CancellationToken ct = default)
    {
        if (entry.SelectedVersion is { } selectedVersion && TryParseVersion(selectedVersion.Version, out _))
            return new PluginLatestManifestVersion { ManifestVersion = selectedVersion, Version = selectedVersion.Version! };

        if (string.IsNullOrWhiteSpace(entry.ManifestUrl)) return null;
        var manifest = await PluginRemoteInstallService.FetchManifestAsync(entry.ManifestUrl, ct).ConfigureAwait(false);
        if (manifest is null)
        {
            LogWrapper.Debug("Plugin", "Failed to fetch manifest for " + entry.Id + " from " + entry.ManifestUrl);
            return null;
        }
        try
        {
            var version = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest);
            return TryParseVersion(version.Version, out _)
                ? new PluginLatestManifestVersion
                {
                    ManifestVersion = version,
                    Version = version.Version!
                }
                : null;
        }
        catch (Exception ex)
        {
            LogWrapper.Debug(ex, "Plugin", "No compatible version in manifest for " + entry.Id + ": " + ex.Message);
            return null;
        }
    }

    public static async Task<IReadOnlyList<PluginUpdateCandidate>> FindUpdatesAsync(
        IReadOnlyList<PluginRepositoryEntry> entries,
        IReadOnlyDictionary<string, PluginInstallRecord> installed,
        CancellationToken ct = default)
    {
        var bestByPlugin = new Dictionary<string, PluginUpdateCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!installed.TryGetValue(entry.Id, out var record)) continue;
            if (!MatchesInstalledSource(record, entry)) continue;

            var latest = await FetchLatestManifestVersionAsync(entry, ct).ConfigureAwait(false);
            if (latest is null) continue;
            if (CompareVersion(latest.Version, record.InstalledVersion) <= 0) continue;

            var source = PluginRepositoryService.GetInstallSources(entry).FirstOrDefault();
            if (source is null) continue;

            var candidate = new PluginUpdateCandidate
            {
                Installed = record,
                Entry = entry,
                Source = source,
                ManifestVersion = latest.ManifestVersion,
                LatestVersion = latest.Version
            };

            if (!bestByPlugin.TryGetValue(record.PluginId, out var existing) || CompareVersion(candidate.LatestVersion, existing.LatestVersion) > 0)
                bestByPlugin[record.PluginId] = candidate;
        }

        return bestByPlugin.Values
            .OrderBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<IReadOnlyList<PluginUpdateCandidate>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var indexes = await FetchEnabledIndexesAsync(ct).ConfigureAwait(false);
        var entries = PluginRepositoryService.MergeIndexes(indexes);
        var installed = GetInstalledPluginRecords();

        return await FindUpdatesAsync(entries, installed, ct).ConfigureAwait(false);
    }

    public static bool TryGetDisplayVersion(PluginRepositoryEntry entry, out string version)
        => TryParseVersion(entry.Version, out version);

    public static bool TryParseVersion(string? value, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) return false;
        if (!SemVer.TryParse(text, out _)) return false;
        version = text;
        return true;
    }

    public static int CompareVersion(string left, string right)
    {
        if (!SemVer.TryParse(left, out var leftVersion) || !SemVer.TryParse(right, out var rightVersion))
            throw new ArgumentException("Plugin versions must be valid SemVer values.");
        return leftVersion!.CompareTo(rightVersion);
    }

    public static string FormatVersion(string version)
        => SemVer.TryParse(version, out var parsed) ? parsed!.ToString() : version;

    public static PluginTrustDecision EvaluateUpdate(PluginUpdateCandidate candidate)
    {
        var expectedSourceUrl = candidate.Installed.SourceType switch
        {
            PluginInstallSourceType.Git => candidate.Entry.SourceRepoUrl,
            PluginInstallSourceType.Manifest => candidate.Entry.ManifestUrl,
            _ => candidate.Source.Url
        };
        if (string.IsNullOrWhiteSpace(expectedSourceUrl)
            || !string.Equals(candidate.Installed.InstalledFrom, expectedSourceUrl, StringComparison.OrdinalIgnoreCase))
            return PluginTrustDecision.RequireReconfirm;

        return PluginTrustService.EvaluateUpdate(candidate.Installed, candidate.Entry, expectedSourceUrl);
    }

    internal static bool MatchesInstalledSource(PluginInstallRecord installed, PluginRepositoryEntry entry)
    {
        return installed.SourceType switch
        {
            PluginInstallSourceType.Git =>
                string.Equals(installed.InstalledFrom, entry.SourceRepoUrl, StringComparison.OrdinalIgnoreCase),
            PluginInstallSourceType.Manifest =>
                string.Equals(installed.InstalledFrom, entry.ManifestUrl, StringComparison.OrdinalIgnoreCase),
            PluginInstallSourceType.Local => false,
            _ => true // 兼容旧 Repository 安装记录。
        };
    }
}
