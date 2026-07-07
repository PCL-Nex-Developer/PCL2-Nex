using System;
using System.Reflection;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 已加载插件的信息记录。
/// </summary>
public sealed class PluginRecord
{
    /// <summary>插件清单。</summary>
    public required PluginManifest Manifest { get; init; }

    /// <summary>插件入口实例。</summary>
    public required IPclPlugin Instance { get; init; }

    /// <summary>插件程序集。</summary>
    public required Assembly Assembly { get; init; }

    /// <summary>插件程序集的物理路径。</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>插件专属数据目录。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>插件状态。</summary>
    public PluginState State { get; set; } = PluginState.Created;

    /// <summary>最后一次异常（加载或卸载阶段），若无则为 <see langword="null"/>。</summary>
    public Exception? LastException { get; set; }

    /// <summary>标识符快捷访问。</summary>
    public string Id => Manifest.Id;
}

/// <summary>
/// 插件状态。
/// </summary>
public enum PluginState
{
    /// <summary>已实例化但尚未加载。</summary>
    Created,

    /// <summary>加载成功并运行中。</summary>
    Running,

    /// <summary>已卸载。</summary>
    Unloaded,

    /// <summary>加载失败或运行期异常导致的已禁用。</summary>
    Disabled
}
