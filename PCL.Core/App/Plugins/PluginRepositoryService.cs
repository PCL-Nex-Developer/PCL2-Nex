using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件市场索引服务。
/// 负责从远程拉取市场索引，以及合并多个来源的插件目录。
/// </summary>
public static class PluginRepositoryService
{
    /// <summary>
    /// 从指定 URL 拉取市场索引。
    /// </summary>
    /// <param name="url">市场索引的 URL。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析后的市场索引，若请求或解析失败则返回 <c>null</c>。</returns>
    public static async Task<PluginRepositoryIndex?> FetchIndexAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            using var response = await HttpRequest.Create(url).SendAsync(cancellationToken: cts.Token).ConfigureAwait(false);
            var index = await response.AsJsonAsync<PluginRepositoryIndex>(PluginJson.SerializerOptions, cts.Token).ConfigureAwait(false);
            if (index is null || index.Plugins is null) return null;

            NormalizeIndex(index, url);

            return index;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 合并多个市场索引的插件列表。
    /// 以 <c>(Id, SourceRepoUrl)</c> 为唯一键保留所有不同来源的条目。
    /// </summary>
    public static IReadOnlyList<PluginRepositoryEntry> MergeIndexes(IReadOnlyList<PluginRepositoryIndex> indexes)
    {
        var result = new List<PluginRepositoryEntry>();
        var seen = new HashSet<string>();

        foreach (var index in indexes)
        {
            if (index.Plugins is null) continue;
            foreach (var entry in index.Plugins)
            {
                if (!IsValidEntry(entry)) continue;
                var key = $"{entry.ManifestUrl}|{entry.SourceRepoUrl ?? string.Empty}";
                if (!seen.Add(key)) continue;
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// 从市场索引条目获取可安装来源。市场索引只负责指向开发者 manifest。
    /// </summary>
    public static IEnumerable<PluginInstallSourceEntry> GetInstallSources(PluginRepositoryEntry entry)
    {
        if (IsAbsoluteHttpUri(entry.ManifestUrl))
            yield return new PluginInstallSourceEntry { Type = "manifest", Name = "Manifest", Url = entry.ManifestUrl!.Trim() };
    }

    /// <summary>
    /// 将市场索引标准化为 docs/plugin-market.md 中定义的展示条目格式。
    /// </summary>
    public static void NormalizeIndex(PluginRepositoryIndex index, string sourceUrl)
    {
        if (index.Plugins is null) return;

        foreach (var entry in index.Plugins)
        {
            if (entry is null) continue;
            NormalizeEntry(entry, sourceUrl);
        }

        index.Plugins = index.Plugins
            .Where(IsValidEntry)
            .GroupBy(entry => entry.ManifestUrl!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void NormalizeEntry(PluginRepositoryEntry entry, string sourceUrl)
    {
        entry.Id = TrimToEmpty(entry.Id);
        entry.Name = TrimToEmpty(entry.Name);
        entry.Version = TrimToNull(entry.Version);
        entry.Author = TrimToNull(entry.Author);
        entry.Description = TrimToNull(entry.Description);
        entry.ManifestUrl = TrimToNull(entry.ManifestUrl);

        if (string.IsNullOrWhiteSpace(entry.SourceRepoUrl))
            entry.SourceRepoUrl = sourceUrl;
        else
            entry.SourceRepoUrl = entry.SourceRepoUrl.Trim();

        if (string.IsNullOrWhiteSpace(entry.HomepageUrl))
            entry.HomepageUrl = entry.Homepage;
        entry.HomepageUrl = TrimToNull(entry.HomepageUrl);
        entry.Homepage = string.IsNullOrWhiteSpace(entry.Homepage) ? entry.HomepageUrl : entry.Homepage.Trim();
    }

    private static bool IsValidEntry(PluginRepositoryEntry? entry)
    {
        return entry is not null
            && !string.IsNullOrWhiteSpace(entry.Id)
            && !string.IsNullOrWhiteSpace(entry.Name)
            && IsAbsoluteHttpUri(entry.ManifestUrl);
    }

    private static bool IsAbsoluteHttpUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static string TrimToEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 获取默认的官方市场索引 URL。
    /// </summary>
    public static string GetOfficialIndexUrl()
    {
        // 默认官方市场索引地址，由 PCL Nex 开发组审核维护。
        return "https://raw.githubusercontent.com/PCL-Nex-Developer/PCL2-Nex/refs/heads/dev/plugins.json";
    }

}
