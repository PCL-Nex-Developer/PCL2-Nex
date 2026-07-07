using System;
using System.Collections.Generic;
using System.Threading;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 宿主应用与插件运行时之间的桥接接口。<br/>
/// 由宿主应用（Plain Craft Launcher 2）在生命周期早期实现并注册到 <see cref="PluginHostBridge"/>。
/// 插件运行时通过本桥接访问宿主能力，避免 PCL.Core 反向依赖宿主应用的具体类型。
/// </summary>
public interface IPluginHostBridge
{
    /// <summary>
    /// UI 扩展宿主，提供设置/工具面板注册能力。在 UI 就绪前可能为 <see langword="null"/>。
    /// </summary>
    IUiExtensionHost? Ui { get; }

    /// <summary>
    /// 实例只读信息提供方。
    /// </summary>
    IHostInstanceInfoProvider? Instances { get; }

    /// <summary>
    /// 命令行子命令注册器。
    /// </summary>
    IHostCliRegistrar? Commands { get; }

    /// <summary>
    /// URI Scheme 动作注册器。
    /// </summary>
    IHostUriActionRegistrar? UriActions { get; }

    /// <summary>
    /// 通用扩展点注册器。
    /// </summary>
    IPluginExtensionHost? Extensions { get; }

    /// <summary>
    /// 显示一条用户提示。
    /// </summary>
    /// <param name="message">文本</param>
    /// <param name="type">提示类型（0=Info 1=Success 2=Warning 3=Error）</param>
    void Notify(string message, int type);

    /// <summary>
    /// 当前展示语言代码（如 <c>zh-CN</c>）。
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// 本地化文本查找。
    /// </summary>
    string Localize(string key, string? fallback);

    /// <summary>
    /// 宿主版本名称。
    /// </summary>
    string HostVersion { get; }

    /// <summary>
    /// 获取宿主暴露的扩展服务（实验性）。未知标识符返回 <see langword="null"/>。
    /// </summary>
    object? GetOptionalService(string serviceId);
}

/// <summary>
/// 全局桥接注册点。宿主应用在启动时调用 <see cref="Register"/> 注入实现。
/// </summary>
public static class PluginHostBridge
{
    private static IPluginHostBridge? _current;
    private static readonly object _lock = new();

    /// <summary>当前注册的桥接实例。</summary>
    public static IPluginHostBridge? Current => Volatile.Read(ref _current);

    /// <summary>
    /// 注册桥接实现。重复注册将覆盖前者。
    /// </summary>
    public static void Register(IPluginHostBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        lock (_lock) { _current = bridge; }
    }
}
