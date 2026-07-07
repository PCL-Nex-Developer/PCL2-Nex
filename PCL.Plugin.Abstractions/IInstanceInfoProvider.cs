using System;
using System.Collections.Generic;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 实例（游戏版本文件夹）只读信息提供方。<br/>
/// <b>仅暴露元信息</b>，不包含任何与启动、登录、账户令牌相关的内容。
/// </summary>
public interface IInstanceInfoProvider
{
    /// <summary>
    /// 当前已知的实例列表。
    /// </summary>
    IReadOnlyList<InstanceInfo> Instances { get; }

    /// <summary>
    /// 当前选中的实例，若无则为 <see langword="null"/>。
    /// </summary>
    InstanceInfo? Selected { get; }

    /// <summary>
    /// 实例列表发生变化时触发。
    /// </summary>
    event EventHandler? InstancesChanged;

    /// <summary>
    /// 当前选中实例发生变化时触发。
    /// </summary>
    event EventHandler? SelectedChanged;
}

/// <summary>
/// 实例元信息。仅包含展示所需的最小信息集合。
/// </summary>
public sealed class InstanceInfo
{
    /// <summary>实例内部 Id（通常是文件夹名）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>实例所在目录的绝对路径。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>游戏版本号（如 <c>1.20.4</c>），无法确定时为空。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>加载器类型（如 <c>Forge</c> <c>Fabric</c> <c>Vanilla</c>），无法确定时为空。</summary>
    public string Loader { get; init; } = string.Empty;

    /// <summary>加载器版本，无法确定时为空。</summary>
    public string LoaderVersion { get; init; } = string.Empty;

    /// <summary>图标路径（可能为空）。</summary>
    public string? IconPath { get; init; }
}
