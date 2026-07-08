using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;

namespace PCL.Core.App.Plugins;

public sealed class PluginUpdateCandidate
{
    public required PluginInstallRecord Installed { get; init; }

    public required PluginRepositoryEntry Entry { get; init; }

    public required PluginInstallSourceEntry Source { get; init; }

    public required PluginMarketVersion ManifestVersion { get; init; }

    public required Version LatestVersion { get; init; }
}

public static class PluginUpdateService
{
    public static async Task<IReadOnlyList<PluginRepositoryIndex>> FetchEnabledIndexesAsync(CancellationToken ct = default)
    {
        var indexes = new List<PluginRepositoryIndex>();

        var officialIndex = await PluginRepositoryService.FetchIndexAsync(PluginRepositoryService.GetOfficialIndexUrl(), ct).ConfigureAwait(false);
        if (officialIndex is not null) indexes.Add(officialIndex);

        foreach (var repo in PluginTrustService.GetAllTrustRecords().Where(r => r.Enabled))
        {
            if (PluginTrustService.IsOfficialRepository(repo.RepoUrl)) continue;
            var index = await PluginRepositoryService.FetchIndexAsync(repo.RepoUrl, ct).ConfigureAwait(false);
            if (index is not null) indexes.Add(index);
        }

        return indexes;
    }

    public static IReadOnlyList<PluginUpdateCandidate> FindUpdates(
        IReadOnlyList<PluginRepositoryEntry> entries,
        IReadOnlyDictionary<string, PluginInstallRecord> installed)
    {
        var bestByPlugin = new Dictionary<string, PluginUpdateCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!installed.TryGetValue(entry.Id, out var record)) continue;
            if (!TryGetDisplayVersion(entry, out var latestVersion)) continue;
            if (CompareVersion(latestVersion, record.InstalledVersion) <= 0) continue;

            var source = PluginRepositoryService.GetInstallSources(entry).FirstOrDefault();
            if (source is null) continue;

            var candidate = new PluginUpdateCandidate
            {
                Installed = record,
                Entry = entry,
                Source = source,
                ManifestVersion = new PluginMarketVersion
                {
                    Version = entry.Version,
                    PackageUrl = source.Url
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
    /// 从市场条目的 manifestUrl 获取实际最新兼容版本号。
    /// plugins.json 里的 version 字段可能过时，真正的最新版本在每个插件的 manifest.json 中。
    /// </summary>
    public static async Task<Version?> FetchLatestVersionAsync(PluginRepositoryEntry entry, CancellationToken ct = default)
    {
        var latest = await FetchLatestManifestVersionAsync(entry, ct).ConfigureAwait(false);
        return latest?.Version;
    }

    public sealed class PluginLatestManifestVersion
    {
        public required PluginMarketVersion ManifestVersion { get; init; }

        public required Version Version { get; init; }
    }

    public static async Task<PluginLatestManifestVersion?> FetchLatestManifestVersionAsync(PluginRepositoryEntry entry, CancellationToken ct = default)
    {
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
            return TryParseVersion(version.Version, out var parsed)
                ? new PluginLatestManifestVersion
                {
                    ManifestVersion = version,
                    Version = parsed
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

    public static bool TryGetDisplayVersion(PluginRepositoryEntry entry, out Version version)
        => TryParseVersion(entry.Version, out version);

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        return Version.TryParse(text, out version!);
    }

    public static int CompareVersion(Version left, Version right)
    {
        var compare = left.Major.CompareTo(right.Major);
        if (compare != 0) return compare;

        compare = left.Minor.CompareTo(right.Minor);
        if (compare != 0) return compare;

        compare = NormalizeVersionPart(left.Build).CompareTo(NormalizeVersionPart(right.Build));
        if (compare != 0) return compare;

        return NormalizeVersionPart(left.Revision).CompareTo(NormalizeVersionPart(right.Revision));
    }

    public static string FormatVersion(Version version)
    {
        var build = NormalizeVersionPart(version.Build);
        var revision = NormalizeVersionPart(version.Revision);
        return revision > 0
            ? $"{version.Major}.{version.Minor}.{build}.{revision}"
            : $"{version.Major}.{version.Minor}.{build}";
    }

    private static int NormalizeVersionPart(int value) => value < 0 ? 0 : value;

    public static PluginTrustDecision EvaluateUpdate(PluginUpdateCandidate candidate)
    {
        var expectedSourceUrl = candidate.Source.Url;
        if (string.Equals(candidate.Installed.InstalledFrom, candidate.Entry.SourceRepoUrl, StringComparison.OrdinalIgnoreCase))
            expectedSourceUrl = candidate.Entry.SourceRepoUrl;
        else if (!string.Equals(candidate.Installed.InstalledFrom, candidate.Source.Url, StringComparison.OrdinalIgnoreCase))
            return PluginTrustDecision.RequireReconfirm;

        return PluginTrustService.EvaluateUpdate(candidate.Installed, candidate.Entry, expectedSourceUrl);
    }
}