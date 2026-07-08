using System;
using System.Windows.Controls;
using System.Windows;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// UI 扩展能力。允许插件向宿主的设置页或工具页贡献自定义面板。
/// </summary>
/// <remarks>
/// 所有 UI 注册都会返回一个 <see cref="IDisposable"/>，插件在卸载时必须释放。
/// 宿主会在主线程（STA）上调用面板工厂。
/// </remarks>
public interface IPluginUiApi
{
    /// <summary>
    /// 向设置页贡献一个自定义设置面板。
    /// </summary>
    /// <param name="descriptor">面板描述</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable ContributeSettingsPanel(SettingsPanelDescriptor descriptor);

    /// <summary>
    /// 向工具页贡献一个自定义工具面板。
    /// </summary>
    /// <param name="descriptor">面板描述</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable ContributeToolsPanel(ToolsPanelDescriptor descriptor);

    /// <summary>
    /// 向插件页侧边栏贡献一个自定义入口及右侧面板。
    /// 插件拥有独立的侧边栏条目与专属页面。
    /// </summary>
    /// <param name="descriptor">面板描述</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable ContributePluginPanel(PluginPanelDescriptor descriptor);

    /// <summary>
    /// 向主导航栏贡献一个自定义页面。
    /// </summary>
    /// <param name="descriptor">导航页描述</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable ContributeNavigationPage(NavigationPageDescriptor descriptor);

    /// <summary>
    /// 向关于页法律信息区域贡献一个链接按钮。
    /// </summary>
    /// <param name="descriptor">法律链接描述</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable ContributeAboutLegalLink(AboutLegalLinkDescriptor descriptor);

    /// <summary>
    /// 在宿主 UI 线程上执行指定操作。
    /// </summary>
    void InvokeOnUi(Action action);

    /// <summary>
    /// 在宿主 UI 线程上执行指定操作并返回结果。
    /// </summary>
    T InvokeOnUi<T>(Func<T> action);

    /// <summary>
    /// 判断当前调用是否在宿主 UI 线程上。
    /// </summary>
    bool CheckAccess();
}

/// <summary>
/// 面板工厂委托。返回需要挂载的根控件。
/// </summary>
/// <returns>WPF 根控件，由宿主负责布局。</returns>
public delegate FrameworkElement SettingsPanelFactory();

/// <summary>
/// 面板工厂委托。返回需要挂载的根控件。
/// </summary>
public delegate FrameworkElement ToolsPanelFactory();

/// <summary>
/// 设置面板描述符。
/// </summary>
public sealed class SettingsPanelDescriptor
{
    /// <summary>面板唯一标识（在插件内唯一即可）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>显示标题（支持本地化键）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>左侧导航分组标题。为空时不额外创建分组标题。</summary>
    public string? Group { get; init; }

    /// <summary>显示顺序权重，数值越小越靠前。</summary>
    public int Order { get; init; } = 100;

    /// <summary>侧边栏图标（lucide 图标名，如 "lucide/sliders"）。可选。</summary>
    public string? Icon { get; init; }

    /// <summary>面板工厂。</summary>
    public required SettingsPanelFactory Factory { get; init; }
}

/// <summary>
/// 工具面板描述符。
/// </summary>
public sealed class ToolsPanelDescriptor
{
    /// <summary>面板唯一标识（在插件内唯一即可）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>显示标题（支持本地化键）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>左侧导航分组标题。为空时不额外创建分组标题。</summary>
    public string? Group { get; init; }

    /// <summary>显示顺序权重，数值越小越靠前。</summary>
    public int Order { get; init; } = 100;

    /// <summary>侧边栏图标（lucide 图标名，如 "lucide/puzzle"）。可选。</summary>
    public string? Icon { get; init; }

    /// <summary>面板工厂。</summary>
    public required ToolsPanelFactory Factory { get; init; }
}

/// <summary>
/// 插件页面工厂委托。返回需要挂载的根控件。
/// </summary>
/// <returns>WPF 根控件，由宿主负责布局。</returns>
public delegate FrameworkElement PluginPanelFactory();

/// <summary>
/// 插件页侧边栏面板描述符。插件通过此描述符在插件页侧边栏注册独立入口。
/// </summary>
public sealed class PluginPanelDescriptor
{
    /// <summary>面板唯一标识（在插件内唯一即可）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>侧边栏显示标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>显示顺序权重，数值越小越靠前。</summary>
    public int Order { get; init; } = 100;

    /// <summary>侧边栏图标（lucide 图标名，如 "lucide/puzzle"）。可选。</summary>
    public string? Icon { get; init; }

    /// <summary>右侧面板工厂。</summary>
    public required PluginPanelFactory Factory { get; init; }
}

/// <summary>
/// 主导航栏页面描述符。插件通过此描述符在顶部导航注册独立入口。
/// </summary>
public sealed class NavigationPageDescriptor
{
    /// <summary>页面唯一标识（在插件内唯一即可）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>顶部导航显示标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>显示顺序权重，数值越小越靠前。</summary>
    public int Order { get; init; } = 100;

    /// <summary>顶部导航图标（lucide 图标名，如 "lucide/code"）。可选。</summary>
    public string? Icon { get; init; }

    /// <summary>右侧页面工厂。</summary>
    public required PluginPanelFactory Factory { get; init; }
}

/// <summary>
/// 关于页法律信息链接描述符。
/// </summary>
public sealed class AboutLegalLinkDescriptor
{
    /// <summary>链接唯一标识（在插件内唯一即可）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>按钮显示标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>要打开的 URL。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>显示顺序权重，数值越小越靠前。</summary>
    public int Order { get; init; } = 100;

    /// <summary>是否使用强调色按钮。</summary>
    public bool IsHighlighted { get; init; }
}
