using System;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 单个插件的本地安装记录。
/// 持久化到 <c>trust/</c> 目录下，用于判断来源变更、能力升级、启用状态等。
/// </summary>
public sealed class PluginInstallRecord
{
    /// <summary>插件唯一标识符。</summary>
    public string PluginId { get; set; } = string.Empty;

    /// <summary>已安装版本号。</summary>
    public string InstalledVersion { get; set; } = string.Empty;

    /// <summary>
    /// 安装来源标识。
    /// GitHub 商店安装保存 Git 仓库；非 Git 与自定义 manifest 安装保存可重新获取更新的 JSON/manifest 地址。
    /// </summary>
    public string InstalledFrom { get; set; } = string.Empty;

    /// <summary>安装来源类型。</summary>
    public PluginInstallSourceType SourceType { get; set; } = PluginInstallSourceType.Repository;

    /// <summary>已安装包的 SHA-256 哈希。</summary>
    public string? InstalledSha256 { get; set; }

    /// <summary>首次信任/安装时间（UTC）。</summary>
    public DateTime TrustedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 插件安装来源类型。
/// </summary>
public enum PluginInstallSourceType
{
    /// <summary>从市场注册表安装（官方或第三方）。</summary>
    Repository,

    /// <summary>从可持续获取更新的 manifest 或市场 JSON 地址安装。</summary>
    Manifest,

    /// <summary>从 Git 仓库安装。</summary>
    Git,

    /// <summary>从本地 .pclx 或 .zip 包导入。</summary>
    Local
}

/// <summary>
/// 仓库信任记录。
/// 记录用户对某个第三方仓库的信任状态。
/// </summary>
public sealed class PluginRepositoryTrustRecord
{
    /// <summary>市场注册表 URL。</summary>
    public string RepoUrl { get; set; } = string.Empty;

    /// <summary>插件源名称（便于展示）。</summary>
    public string RepoName { get; set; } = string.Empty;

    /// <summary>建立信任的时间（UTC）。</summary>
    public DateTime TrustedAt { get; set; } = DateTime.UtcNow;

    /// <summary>是否启用（用户可临时禁用已信任的仓库）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>来源类型。</summary>
    public PluginRepositorySourceType SourceType { get; set; } = PluginRepositorySourceType.Custom;

    /// <summary>第三方源内容类型。旧记录未声明时按 JSON 处理。</summary>
    public PluginRepositorySourceKind SourceKind { get; set; } = PluginRepositorySourceKind.Json;
}

public enum PluginRepositorySourceKind
{
    Json,
    Topic,
    Manifest
}

/// <summary>
/// 仓库来源类型。
/// </summary>
public enum PluginRepositorySourceType
{
    /// <summary>官方内置源。</summary>
    Official,

    /// <summary>用户添加的第三方源。</summary>
    Custom
}
