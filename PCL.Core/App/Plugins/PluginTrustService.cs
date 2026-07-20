using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件信任策略引擎。
/// 负责插件源信任记录管理、安装/更新信任决策。
/// </summary>
public static class PluginTrustService
{
    private static readonly object _lock = new();
    private static List<PluginRepositoryTrustRecord>? _cache;

    /// <summary>
    /// 判断指定插件源 URL 是否已受信任且启用。
    /// </summary>
    public static bool IsRepositoryTrusted(string repoUrl)
    {
        var records = _LoadRecords();
        return records.Any(r => string.Equals(r.RepoUrl, repoUrl, StringComparison.OrdinalIgnoreCase) && r.Enabled);
    }

    /// <summary>
    /// 判断指定插件源 URL 是否为官方源。
    /// </summary>
    public static bool IsOfficialRepository(string repoUrl)
    {
        return string.Equals(repoUrl, PluginRepositoryService.GetOfficialIndexUrl(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>返回需要建立信任的实际市场来源，而不是插件自身的代码仓库。</summary>
    public static string GetRepositoryTrustUrl(PluginRepositoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.SourceKind is "Json" or "Manifest"
            && !string.IsNullOrWhiteSpace(entry.ManifestUrl))
            return entry.ManifestUrl.Trim();
        return (entry.SourceRepoUrl ?? entry.ManifestUrl ?? string.Empty).Trim();
    }

    /// <summary>
    /// 评估新安装的插件是否允许安装。
    /// </summary>
    /// <param name="entry">待安装的市场插件条目。</param>
    /// <param name="sourceType">安装来源类型。</param>
    /// <returns>信任决策。</returns>
    public static PluginTrustDecision EvaluateInstall(PluginRepositoryEntry entry, PluginInstallSourceType sourceType)
    {
        if (entry.SourceIsOfficial) return PluginTrustDecision.Allow;

        // Git 远程安装始终需要高风险确认
        if (sourceType is PluginInstallSourceType.Git)
            return PluginTrustDecision.RequireReconfirm;

        var repoUrl = GetRepositoryTrustUrl(entry);

        // Topic market entries are discovered through GitHub API and use the repository owner as identity.
        if (entry.SourceKind is "GitHub" or "Topics"
            && !string.IsNullOrWhiteSpace(entry.GitHubLogin)
            && Uri.TryCreate(entry.SourceRepoUrl, UriKind.Absolute, out var repoUri)
            && string.Equals(repoUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return PluginTrustDecision.Allow;

        // 官方源：允许（仍展示来源说明，但不阻断）
        if (IsOfficialRepository(repoUrl))
            return PluginTrustDecision.Allow;

        // 第三方插件源：检查是否已信任
        if (!IsRepositoryTrusted(repoUrl))
            return PluginTrustDecision.RequireRepositoryTrust;

        // 已信任的第三方插件源：允许
        return PluginTrustDecision.Allow;
    }

    /// <summary>
    /// 评估已安装插件的更新是否允许。
    /// </summary>
    /// <param name="installed">当前安装记录。</param>
    /// <param name="incoming">市场注册表中的新版本条目。</param>
    /// <returns>信任决策。</returns>
    public static PluginTrustDecision EvaluateUpdate(PluginInstallRecord installed, PluginRepositoryEntry incoming)
        => EvaluateUpdate(installed, incoming, incoming.SourceRepoUrl);

    /// <summary>
    /// 评估已安装插件的更新是否允许。
    /// </summary>
    /// <param name="installed">当前安装记录。</param>
    /// <param name="incoming">市场注册表中的新版本条目。</param>
    /// <param name="expectedSourceUrl">本次更新将写入安装记录的来源地址。</param>
    /// <returns>信任决策。</returns>
    public static PluginTrustDecision EvaluateUpdate(PluginInstallRecord installed, PluginRepositoryEntry incoming, string? expectedSourceUrl)
    {
        // 来源变更：需要二次确认
        if (!string.Equals(installed.InstalledFrom, expectedSourceUrl, StringComparison.OrdinalIgnoreCase))
            return PluginTrustDecision.RequireReconfirm;

        return PluginTrustDecision.Allow;
    }

    /// <summary>
    /// 添加仓库信任记录。
    /// </summary>
    public static void AddTrust(
        string repoUrl,
        string repoName,
        PluginRepositorySourceType sourceType,
        PluginRepositorySourceKind sourceKind = PluginRepositorySourceKind.Json)
    {
        var records = _LoadRecords();
        var existing = records.FirstOrDefault(r => r.RepoUrl == repoUrl);
        if (existing is not null)
        {
            existing.Enabled = true;
            existing.TrustedAt = DateTime.UtcNow;
            existing.RepoName = repoName;
            existing.SourceType = sourceType;
            existing.SourceKind = sourceKind;
        }
        else
        {
            records.Add(new PluginRepositoryTrustRecord
            {
                RepoUrl = repoUrl,
                RepoName = repoName,
                TrustedAt = DateTime.UtcNow,
                Enabled = true,
                SourceType = sourceType,
                SourceKind = sourceKind
            });
        }
        _SaveRecords(records);
    }

    /// <summary>
    /// 设置仓库信任记录的启用状态。
    /// </summary>
    public static void SetRepositoryEnabled(string repoUrl, bool enabled)
    {
        if (IsOfficialRepository(repoUrl)) return;

        var records = _LoadRecords();
        var record = records.FirstOrDefault(r => r.RepoUrl == repoUrl);
        if (record is not null)
        {
            record.Enabled = enabled;
            _SaveRecords(records);
        }
    }

    /// <summary>
    /// 移除仓库信任记录。
    /// </summary>
    public static void RemoveTrust(string repoUrl)
    {
        if (IsOfficialRepository(repoUrl)) return;

        var records = _LoadRecords();
        records.RemoveAll(r => r.RepoUrl == repoUrl);
        _SaveRecords(records);
    }

    /// <summary>
    /// 获取所有仓库信任记录。
    /// </summary>
    public static IReadOnlyList<PluginRepositoryTrustRecord> GetAllTrustRecords()
    {
        return _LoadRecords().AsReadOnly();
    }

    private static string _TrustFilePath => Path.Combine(PCL.Core.App.Paths.PluginTrust, "repositories.json");

    private static List<PluginRepositoryTrustRecord> _LoadRecords()
    {
        lock (_lock)
        {
            if (_cache is not null) return _cache;

            try
            {
                var path = _TrustFilePath;
                if (!File.Exists(path)) { _cache = []; return _cache; }
                var json = File.ReadAllText(path);
                _cache = JsonSerializer.Deserialize<List<PluginRepositoryTrustRecord>>(json, PluginJson.SerializerOptions) ?? [];
            }
            catch
            {
                _cache = [];
            }
            return _cache;
        }
    }

    private static void _SaveRecords(List<PluginRepositoryTrustRecord> records)
    {
        lock (_lock)
        {
            _cache = records;
            try
            {
                var path = _TrustFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(records, PluginJson.SerializerOptions);
                File.WriteAllText(path, json);
            }
            catch
            {
                // 写入失败不阻断运行，信任记录仅在内存中保留
            }
        }
    }
}

/// <summary>
/// 信任决策枚举。
/// </summary>
public enum PluginTrustDecision
{
    /// <summary>允许继续。</summary>
    Allow,

    /// <summary>需要建立仓库信任（首次使用第三方仓库）。</summary>
    RequireRepositoryTrust,

    /// <summary>需要二次确认（来源变更等高风险变更）。</summary>
    RequireReconfirm,

    /// <summary>拒绝安装（技术校验失败）。</summary>
    Reject
}
