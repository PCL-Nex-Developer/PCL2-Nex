using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.IoC;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;
using HintType = PCL.HintType;

namespace PCL.Plugins;

/// <summary>
/// 插件宿主引导服务。<br/>
/// 负责实现 <see cref="IPluginHostBridge"/> 并注册到 <see cref="PluginHostBridge"/>，
/// 将宿主能力暴露给插件运行时。<br/>
/// 加载时机：<see cref="LifecycleState.WindowCreated"/> —— 在主窗体创建后、
/// 插件加载器（同状态、更低优先级）之前。
/// </summary>
/// <remarks>
/// <b>注册方式</b>：宿主 app 不运行生命周期源生成器，因此本类不依赖 <c>[LifecycleService]</c>
/// 自动发现，而是提供 <see cref="Initialize"/> 静态入口，由 <c>Application</c> 构造函数通过
/// <c>Lifecycle.When(WindowCreated, PluginHostBootstrapService.Initialize)</c> 显式挂载。
/// <see cref="Lifecycle.When"/> 回调在状态切换事件中先于该状态的生命周期服务初始化触发，
/// 因此本引导必然先于 <c>PluginLoaderService</c>（同为 WindowCreated 的服务）执行。
/// </remarks>
/// <remarks>
/// <b>许可证约束</b>：本类<b>仅</b>暴露安全的、与启动/主题无关的能力。
/// 它不持有任何对 <c>ModLaunch</c>、账户令牌、主题服务的引用。
/// </remarks>
public static class PluginHostBootstrap
{
    private static int _initialized;
    private static readonly PluginUiExtensionRegistry _uiRegistry = new();
    private static readonly PluginCliRegistry _cliRegistry = new();
    private static readonly PluginUriActionRegistry _uriActionRegistry = new();
    private static readonly PluginExtensionRegistry _extensionRegistry = new();
    private static InstanceInfoProviderAdapter? _instanceAdapter;
    private static UiExtensionHostImpl? _uiHost;

    /// <summary>
    /// 初始化并注册插件宿主桥接。幂等，重复调用安全。
    /// 应在 <see cref="LifecycleState.WindowCreated"/> 时机调用一次。
    /// </summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1) return;
        _instanceAdapter = new InstanceInfoProviderAdapter();
        _uiHost = new UiExtensionHostImpl(_uiRegistry);
        var bridge = new HostBridgeImpl(_uiHost, _instanceAdapter, _cliRegistry, _uriActionRegistry, _extensionRegistry);
        PluginHostBridge.Register(bridge);
        // 退出时清理注册表
        Lifecycle.When(LifecycleState.Exiting, Cleanup);
        ModBase.Log("[Plugins] 插件宿主桥接已注册");
    }

    /// <summary>
    /// 清理所有插件贡献的注册项。在 <see cref="LifecycleState.Exiting"/> 自动调用。
    /// </summary>
    public static void Cleanup()
    {
        _uiRegistry.Clear();
        _cliRegistry.Clear();
        _uriActionRegistry.Clear();
        _extensionRegistry.Clear();
    }

    /// <summary>全局 UI 扩展注册表。宿主 UI 页面可读取以渲染插件贡献的面板。</summary>
    public static PluginUiExtensionRegistry UiExtensions => _uiRegistry;

    /// <summary>全局命令行扩展注册表。宿主命令行解析可读取以派发插件子命令。</summary>
    public static PluginCliRegistry CliCommands => _cliRegistry;

    /// <summary>全局 URI Scheme 动作注册表。</summary>
    public static PluginUriActionRegistry UriActions => _uriActionRegistry;

    /// <summary>全局通用扩展点注册表。</summary>
    public static PluginExtensionRegistry Extensions => _extensionRegistry;
}

/// <summary>
/// 插件贡献的 UI 扩展（设置/工具/导航面板）注册表。
/// </summary>
public sealed class PluginUiExtensionRegistry
{
    private readonly List<UiExtensionEntry> _entries = [];
    private readonly object _lock = new();
    public event EventHandler? Changed;

    internal IDisposable Add(UiExtensionEntry entry)
    {
        lock (_lock) { _entries.Add(entry); }
        Changed?.Invoke(this, EventArgs.Empty);
        return new _Remover(this, entry);
    }

    internal void Clear()
    {
        lock (_lock) { _entries.Clear(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>获取所有设置面板扩展（按 Order 升序）。</summary>
    public IReadOnlyList<UiExtensionEntry> GetSettings()
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Kind == UiExtensionKind.Settings)
                           .OrderBy(e => e.Order).ThenBy(e => e.PluginId).ToList();
        }
    }

    /// <summary>获取所有工具面板扩展（按 Order 升序）。</summary>
    public IReadOnlyList<UiExtensionEntry> GetTools()
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Kind == UiExtensionKind.Tools)
                           .OrderBy(e => e.Order).ThenBy(e => e.PluginId).ToList();
        }
    }

    /// <summary>获取所有插件页面扩展（按 Order 升序）。</summary>
    public IReadOnlyList<UiExtensionEntry> GetPluginPages()
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Kind == UiExtensionKind.PluginPage)
                           .OrderBy(e => e.Order).ThenBy(e => e.PluginId).ToList();
        }
    }

    /// <summary>获取所有主导航页面扩展（按 Order 升序）。</summary>
    public IReadOnlyList<UiExtensionEntry> GetNavigationPages()
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Kind == UiExtensionKind.NavigationPage)
                           .OrderBy(e => e.Order).ThenBy(e => e.PluginId).ToList();
        }
    }

    /// <summary>获取所有关于页法律信息链接扩展（按 Order 升序）。</summary>
    public IReadOnlyList<UiExtensionEntry> GetAboutLegalLinks()
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Kind == UiExtensionKind.AboutLegalLink)
                           .OrderBy(e => e.Order).ThenBy(e => e.PluginId).ToList();
        }
    }

    private sealed class _Remover(PluginUiExtensionRegistry reg, UiExtensionEntry entry) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1) return;
            lock (reg._lock) { reg._entries.Remove(entry); }
            reg.Changed?.Invoke(reg, EventArgs.Empty);
        }
    }
}

/// <summary>UI 扩展种类。</summary>
public enum UiExtensionKind { Settings, Tools, PluginPage, NavigationPage, AboutLegalLink }

/// <summary>UI 扩展条目。</summary>
public sealed class UiExtensionEntry
{
    public required string PluginId { get; init; }
    public required string Id { get; init; }
    public required UiExtensionKind Kind { get; init; }
    public required string Title { get; init; }
    public string? Group { get; init; }
    public required int Order { get; init; }
    /// <summary>侧边栏图标（lucide 图标名）。仅 PluginPage 类型使用。</summary>
    public string? Icon { get; init; }
    /// <summary>链接地址。仅 AboutLegalLink 类型使用。</summary>
    public string? Url { get; init; }
    /// <summary>是否使用强调色按钮。仅 AboutLegalLink 类型使用。</summary>
    public bool IsHighlighted { get; init; }
    /// <summary>面板工厂委托，在 UI 线程调用并返回 <see cref="FrameworkElement"/>。</summary>
    public required Delegate Factory { get; init; }

    internal FrameworkElement CreateControl() => (FrameworkElement)Factory.DynamicInvoke();
}

/// <summary>插件贡献的命令行子命令注册表。</summary>
public sealed class PluginCliRegistry
{
    private readonly List<PluginCliEntry> _entries = [];
    private readonly object _lock = new();

    internal IDisposable Add(PluginCliEntry entry)
    {
        lock (_lock) { _entries.Add(entry); }
        return new _Remover(this, entry);
    }

    internal void Clear() { lock (_lock) { _entries.Clear(); } }

    /// <summary>所有已注册的子命令。</summary>
    public IReadOnlyList<PluginCliEntry> GetAll() { lock (_lock) { return _entries.ToList(); } }

    /// <summary>尝试获取指定名称的子命令。</summary>
    public PluginCliEntry? Find(string name)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class _Remover(PluginCliRegistry reg, PluginCliEntry entry) : IDisposable
    {
        public void Dispose() { lock (reg._lock) { reg._entries.Remove(entry); } }
    }
}

/// <summary>命令行子命令条目。</summary>
public sealed class PluginCliEntry
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Usage { get; init; }
    public required Func<string[], int> Handler { get; init; }
}

/// <summary>插件贡献的 URI Scheme 动作注册表。</summary>
public sealed class PluginUriActionRegistry
{
    private readonly List<PluginUriActionEntry> _entries = [];
    private readonly object _lock = new();

    internal IDisposable Add(PluginUriActionEntry entry)
    {
        lock (_lock) { _entries.Add(entry); }
        return new _Remover(this, entry);
    }

    internal void Clear() { lock (_lock) { _entries.Clear(); } }

    /// <summary>所有已注册的 URI 动作。</summary>
    public IReadOnlyList<PluginUriActionEntry> GetAll() { lock (_lock) { return _entries.ToList(); } }

    /// <summary>尝试获取指定插件的 URI 动作。</summary>
    public PluginUriActionEntry? Find(string pluginId, string name)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e =>
                string.Equals(e.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class _Remover(PluginUriActionRegistry reg, PluginUriActionEntry entry) : IDisposable
    {
        public void Dispose() { lock (reg._lock) { reg._entries.Remove(entry); } }
    }
}

/// <summary>URI Scheme 动作条目。</summary>
public sealed class PluginUriActionEntry
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Usage { get; init; }
    public required Action<HostUriActionContext> Handler { get; init; }
}

internal sealed class UiExtensionHostImpl(PluginUiExtensionRegistry registry) : IUiExtensionHost
{
    public IDisposable RegisterSettingsPanel(string pluginId, string id, string title, string? group, int order, string? icon, Delegate factory)
    {
        return registry.Add(new UiExtensionEntry
        {
            PluginId = pluginId, Id = id, Kind = UiExtensionKind.Settings, Title = title, Group = group, Order = order, Icon = icon, Factory = factory
        });
    }

    public IDisposable RegisterToolsPanel(string pluginId, string id, string title, string? group, int order, string? icon, Delegate factory)
    {
        return registry.Add(new UiExtensionEntry
        {
            PluginId = pluginId, Id = id, Kind = UiExtensionKind.Tools, Title = title, Group = group, Order = order, Icon = icon, Factory = factory
        });
    }

    public IDisposable RegisterPluginPanel(string pluginId, string id, string title, int order, string? icon, Delegate factory)
    {
        return registry.Add(new UiExtensionEntry
        {
            PluginId = pluginId, Id = id, Kind = UiExtensionKind.PluginPage, Title = title, Order = order, Icon = icon, Factory = factory
        });
    }

    public IDisposable RegisterNavigationPage(string pluginId, string id, string title, int order, string? icon, Delegate factory)
    {
        return registry.Add(new UiExtensionEntry
        {
            PluginId = pluginId, Id = id, Kind = UiExtensionKind.NavigationPage, Title = title, Order = order, Icon = icon, Factory = factory
        });
    }

    public IDisposable RegisterAboutLegalLink(string pluginId, string id, string title, string url, int order, bool isHighlighted)
    {
        return registry.Add(new UiExtensionEntry
        {
            PluginId = pluginId, Id = id, Kind = UiExtensionKind.AboutLegalLink, Title = title, Url = url, Order = order, IsHighlighted = isHighlighted, Factory = (Func<FrameworkElement>)(() => null!)
        });
    }

    public void InvokeOnUi(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    public bool CheckAccess()
    {
        var app = Application.Current;
        return app is null ? true : app.Dispatcher.CheckAccess();
    }
}

internal sealed class HostBridgeImpl(
    UiExtensionHostImpl ui,
    InstanceInfoProviderAdapter instances,
    PluginCliRegistry commands,
    PluginUriActionRegistry uriActions,
    PluginExtensionRegistry extensions) : IPluginHostBridge
{
    public IUiExtensionHost? Ui => ui;
    public IHostInstanceInfoProvider? Instances => instances;
    public IHostCliRegistrar? Commands => new CliRegistrarImpl(commands);
    public IHostUriActionRegistrar? UriActions => new UriActionRegistrarImpl(uriActions);
    public IPluginExtensionHost? Extensions => extensions;

    public void Notify(string message, int type)
    {
        try
        {
            var t = (HintType)type;
            var app = Application.Current;
            if (app is null) return;
            if (app.Dispatcher.CheckAccess()) HintService.Hint(message, t);
            else app.Dispatcher.BeginInvoke(new Action(() => HintService.Hint(message, t)));
        }
        catch { /* 忽略提示失败 */ }
    }

    public string CurrentLanguage => ModBase.currentLang ?? "zh_CN";

    public string Localize(string key, string? fallback)
    {
        try
        {
            var text = PCL.Core.App.Localization.Lang.Text(key);
            return string.IsNullOrEmpty(text) ? (fallback ?? key) : text;
        }
        catch { return fallback ?? key; }
    }

        public string HostVersion => ModBase.versionBaseName ?? "3.0.2";
}

internal sealed class CliRegistrarImpl(PluginCliRegistry registry) : IHostCliRegistrar
{
    public IDisposable RegisterCommand(string pluginId, string name, string description, string usage, Func<string[], int> handler)
    {
        return registry.Add(new PluginCliEntry
        {
            PluginId = pluginId, Name = name, Description = description, Usage = usage, Handler = handler
        });
    }
}

internal sealed class UriActionRegistrarImpl(PluginUriActionRegistry registry) : IHostUriActionRegistrar
{
    public IDisposable RegisterAction(string pluginId, string name, string description, string usage, Action<HostUriActionContext> handler)
    {
        return registry.Add(new PluginUriActionEntry
        {
            PluginId = pluginId, Name = name, Description = description, Usage = usage, Handler = handler
        });
    }
}

/// <summary>
/// 实例只读信息适配器：从 <see cref="ModInstanceList"/> 读取实例，
/// 转换为 <see cref="HostInstanceSnapshot"/>。仅暴露元信息，不涉及启动/登录。
/// </summary>
internal sealed class InstanceInfoProviderAdapter : IHostInstanceInfoProvider
{
    public IReadOnlyList<HostInstanceSnapshot> GetInstances()
    {
        var result = new List<HostInstanceSnapshot>();
        try
        {
            var list = ModInstanceList.mcInstanceList;
            if (list is null) return result;
            foreach (var kv in list)
            {
                if (kv.Value is null) continue;
                foreach (var inst in kv.Value)
                {
                    if (inst is null) continue;
                    result.Add(_ToSnapshot(inst));
                }
            }
        }
        catch { /* 读取失败返回空列表 */ }
        return result;
    }

    public string? GetSelectedInstanceId()
    {
        try { return States.Game.SelectedInstance; }
        catch { return null; }
    }

    public event EventHandler? InstancesChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? SelectedChanged
    {
        add { }
        remove { }
    }

    private static HostInstanceSnapshot _ToSnapshot(McInstance inst)
    {
        string loader = "Vanilla";
        string loaderVersion = "";
        string version = string.Empty;
        try
        {
            var info = inst.Info;
            if (info is not null)
            {
                version = info.VanillaName ?? string.Empty;
                if (info.HasFabric) { loader = "Fabric"; loaderVersion = info.Fabric; }
                else if (info.HasQuilt) { loader = "Quilt"; loaderVersion = info.Quilt; }
                else if (info.HasNeoForge) { loader = "NeoForge"; loaderVersion = info.NeoForge; }
                else if (info.HasForge) { loader = "Forge"; loaderVersion = info.Forge; }
                else if (info.HasLabyMod) { loader = "LabyMod"; loaderVersion = info.LabyMod; }
                else if (info.HasCleanroom) { loader = "Cleanroom"; loaderVersion = info.Cleanroom; }
                else if (info.HasLegacyFabric) { loader = "LegacyFabric"; loaderVersion = info.LegacyFabric; }
                else if (info.HasLiteLoader) { loader = "LiteLoader"; }
                else if (info.HasOptiFine) { loader = "OptiFine"; loaderVersion = info.OptiFine; }
            }
        }
        catch { /* 忽略 info 读取失败 */ }

        return new HostInstanceSnapshot
        {
            Id = inst.Name ?? string.Empty,
            Name = inst.Name ?? string.Empty,
            Path = inst.PathInstance ?? string.Empty,
            Version = version,
            Loader = loader,
            LoaderVersion = loaderVersion,
            IconPath = string.IsNullOrEmpty(inst.Logo) ? null : inst.Logo
        };
    }
}
