using System;
using PCL.Plugin.Abstractions;

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
    public Version InstalledVersion { get; set; } = new(0, 0, 0, 0);

    /// <summary>
    /// 安装来源标识。
    /// 对于市场安装为 index.json 或 manifest URL，包安装为来源地址。
    /// </summary>
    public string InstalledFrom { get; set; } = string.Empty;

    /// <summary>安装来源类型。</summary>
    public PluginInstallSourceType SourceType { get; set; } = PluginInstallSourceType.Repository;

    /// <summary>已安装包的 SHA-256 哈希。</summary>
    public string? InstalledSha256 { get; set; }

    /// <summary>安装时的能力快照，用于检测能力升级。</summary>
    public PluginCapabilities[] CapabilitiesSnapshot { get; set; } = [];

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
