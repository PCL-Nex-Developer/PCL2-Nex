using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.App.Plugins;

public sealed class PluginUpdateCandidate
{
    public required PluginInstallRecord Installed { get; init; }

    public required PluginRepositoryEntry Entry { get; init; }

    public required PluginInstallSourceEntry Source { get; init; }

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
            if (latestVersion <= record.InstalledVersion) continue;

            var source = PluginRepositoryService.GetInstallSources(entry).FirstOrDefault();
            if (source is null) continue;

            var candidate = new PluginUpdateCandidate
            {
                Installed = record,
                Entry = entry,
                Source = source,
                LatestVersion = latestVersion
            };

            if (!bestByPlugin.TryGetValue(record.PluginId, out var existing) || candidate.LatestVersion > existing.LatestVersion)
                bestByPlugin[record.PluginId] = candidate;
        }

        return bestByPlugin.Values
            .OrderBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<PluginUpdateCandidate> FindUpdates(IReadOnlyList<PluginRepositoryEntry> entries)
    {
        var installed = PluginInstallService.GetInstalledPlugins()
            .ToDictionary(record => record.PluginId, StringComparer.OrdinalIgnoreCase);
        return FindUpdates(entries, installed);
    }

    /// <summary>
    /// 从市场条目的 manifestUrl 获取实际最新兼容版本号。
    /// plugins.json 里的 version 字段可能过时，真正的最新版本在每个插件的 manifest.json 中。
    /// </summary>
    public static async Task<Version?> FetchLatestVersionAsync(PluginRepositoryEntry entry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.ManifestUrl)) return null;
        var manifest = await PluginRemoteInstallService.FetchManifestAsync(entry.ManifestUrl, ct).ConfigureAwait(false);
        if (manifest is null) return null;
        try
        {
            var version = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest);
            return Version.TryParse(version.Version, out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<IReadOnlyList<PluginUpdateCandidate>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var indexes = await FetchEnabledIndexesAsync(ct).ConfigureAwait(false);
        var entries = PluginRepositoryService.MergeIndexes(indexes);
        var installed = PluginInstallService.GetInstalledPlugins()
            .ToDictionary(record => record.PluginId, StringComparer.OrdinalIgnoreCase);

        // 对每个已安装插件，fetch manifestUrl 获取实时最新版本，而非依赖 plugins.json 的静态 version 字段。
        var candidates = new List<PluginUpdateCandidate>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!installed.TryGetValue(entry.Id, out var record)) continue;

            var latestVersion = await FetchLatestVersionAsync(entry, ct).ConfigureAwait(false);
            if (latestVersion is null) continue;
            if (latestVersion <= record.InstalledVersion) continue;

            var source = PluginRepositoryService.GetInstallSources(entry).FirstOrDefault();
            if (source is null) continue;

            candidates.Add(new PluginUpdateCandidate
            {
                Installed = record,
                Entry = entry,
                Source = source,
                LatestVersion = latestVersion
            });
        }

        return candidates
            .OrderBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryGetDisplayVersion(PluginRepositoryEntry entry, out Version version)
        => Version.TryParse(entry.Version, out version!);

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