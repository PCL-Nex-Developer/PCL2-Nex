using System;
using System.Collections.Generic;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件宿主的扩展点契约。宿主应用（Plain Craft Launcher 2）需实现此接口，用于桥接插件 SDK 与宿主 UI 层。<br/>
/// 该接口位于 PCL.Core，避免 PCL.Plugin.Abstractions 依赖 WPF 控件库等具体实现。
/// </summary>
public interface IUiExtensionHost
{
    /// <summary>
    /// 注册一个设置面板扩展。插件卸载时调用返回的 <see cref="IDisposable"/> 即可注销。
    /// </summary>
    /// <param name="pluginId">插件 Id</param>
    /// <param name="title">面板标题</param>
    /// <param name="group">侧边栏分组标题，可选</param>
    /// <param name="order">排序权重</param>
    /// <param name="icon">侧边栏图标（lucide 图标名，可选）</param>
    /// <param name="factory">面板工厂委托，在 UI 线程调用并返回 <c>System.Windows.Controls.UserControl</c>。</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterSettingsPanel(string pluginId, string id, string title, string? group, int order, string? icon, Delegate factory);

    /// <summary>
    /// 注册一个工具面板扩展。
    /// </summary>
    /// <param name="pluginId">插件 Id</param>
    /// <param name="title">面板标题</param>
    /// <param name="order">排序权重</param>
    /// <param name="factory">面板工厂委托，在 UI 线程调用并返回 <c>System.Windows.Controls.UserControl</c>。</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterToolsPanel(string pluginId, string id, string title, string? group, int order, string? icon, Delegate factory);

    /// <summary>
    /// 注册一个插件页侧边栏面板扩展。插件在插件页拥有独立入口。
    /// </summary>
    /// <param name="pluginId">插件 Id</param>
    /// <param name="title">侧边栏标题</param>
    /// <param name="order">排序权重</param>
    /// <param name="icon">侧边栏图标（lucide 图标名，可选）</param>
    /// <param name="factory">面板工厂委托</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterPluginPanel(string pluginId, string id, string title, int order, string? icon, Delegate factory);

    /// <summary>
    /// 注册一个主导航栏页面扩展。插件在顶部导航拥有独立入口。
    /// </summary>
    /// <param name="pluginId">插件 Id</param>
    /// <param name="title">导航标题</param>
    /// <param name="order">排序权重</param>
    /// <param name="icon">导航图标（lucide 图标名，可选）</param>
    /// <param name="factory">页面工厂委托</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterNavigationPage(string pluginId, string id, string title, int order, string? icon, Delegate factory);

    /// <summary>
    /// 注册一个关于页法律信息链接扩展。
    /// </summary>
    /// <param name="pluginId">插件 Id</param>
    /// <param name="title">按钮标题</param>
    /// <param name="url">链接 URL</param>
    /// <param name="order">排序权重</param>
    /// <param name="isHighlighted">是否使用强调色按钮</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterAboutLegalLink(string pluginId, string id, string title, string url, int order, bool isHighlighted);

    /// <summary>
    /// 在 UI 线程上执行操作。
    /// </summary>
    void InvokeOnUi(Action action);

    /// <summary>
    /// 是否在 UI 线程。
    /// </summary>
    bool CheckAccess();
}

/// <summary>
/// 实例信息提供方扩展点。由宿主应用实现，向插件提供只读实例信息。
/// </summary>
public interface IHostInstanceInfoProvider
{
    /// <summary>当前实例快照列表。</summary>
    IReadOnlyList<HostInstanceSnapshot> GetInstances();

    /// <summary>当前选中的实例 Id，若无返回 <see langword="null"/>。</summary>
    string? GetSelectedInstanceId();

    /// <summary>实例列表变化时触发（可能在非 UI 线程）。</summary>
    event EventHandler? InstancesChanged;

    /// <summary>选中实例变化时触发（可能在非 UI 线程）。</summary>
    event EventHandler? SelectedChanged;
}

/// <summary>
/// 宿主侧的实例只读快照。由宿主填充后转换为 SDK 的 <c>InstanceInfo</c>。
/// </summary>
public sealed class HostInstanceSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Loader { get; init; } = string.Empty;
    public string LoaderVersion { get; init; } = string.Empty;
    public string? IconPath { get; init; }
}

/// <summary>
/// 命令行子命令注册扩展点。
/// </summary>
public interface IHostCliRegistrar
{
    IDisposable RegisterCommand(string pluginId, string name, string description, string usage, Func<string[], int> handler);
}

/// <summary>
/// 宿主侧 URI Scheme 动作注册扩展点。
/// </summary>
public interface IHostUriActionRegistrar
{
    IDisposable RegisterAction(
        string pluginId,
        string name,
        string description,
        string usage,
        Action<HostUriActionContext> handler);
}

/// <summary>
/// 宿主传递给插件 URI 动作的上下文。
/// </summary>
public sealed class HostUriActionContext
{
    public required string Scheme { get; init; }
    public required string RawUri { get; init; }
    public required string Action { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
