using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 插件清单。每个插件必须通过程序集级别的 <see cref="PluginAttribute"/> 暴露一份清单，
/// 或在打包时以 <c>plugin.json</c> 形式随同程序集分发。
/// </summary>
public sealed class PluginManifest
{
    /// <summary>
    /// 插件的唯一标识符。建议使用反向域名格式，如 <c>com.example.myplugin</c>。
    /// 必须仅包含小写字母、数字、点（<c>.</c>）、连字符（<c>-</c>）和下划线（<c>_</c>）。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 人类可读的插件名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 插件版本号。
    /// </summary>
    public Version Version { get; set; } = new(1, 0, 0, 0);

    /// <summary>
    /// 作者。
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// 可选的插件描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 可选的主页 URL。
    /// </summary>
    public string? HomePageUrl { get; set; }

    /// <summary>
    /// 插件所需的最低 SDK API 版本。详见 <see cref="ApiVersions"/>。
    /// </summary>
    public Version? MinApiVersion { get; set; }

    /// <summary>
    /// 插件声明的最高兼容 SDK API 版本。为空表示不限制上限。
    /// 当宿主 API 在某次更新中发生不兼容变更时，旧插件可通过该字段阻止继续加载。
    /// </summary>
    public Version? MaxApiVersion { get; set; }

    /// <summary>
    /// 插件要求的最低启动器版本（SemVer 字符串，如 <c>2.15.0</c>）。为空表示不限制。
    /// </summary>
    public string? MinHostVersion { get; set; }

    /// <summary>
    /// 插件声明的最高兼容启动器版本（SemVer 字符串，如 <c>2.15.0-beta.1</c>）。为空表示不限制。
    /// </summary>
    public string? MaxHostVersion { get; set; }

    /// <summary>
    /// 实现了 <see cref="IPclPlugin"/> 的入口类型的程序集限定名。<br/>
    /// 若使用 <see cref="PluginAttribute"/> 标注入口类，则可留空由宿主自动发现。
    /// </summary>
    public string? EntryPointTypeName { get; set; }

    /// <summary>
    /// 需要启用的能力标志。未声明的能力在运行时不可用。
    /// </summary>
    public PluginCapabilities Capabilities { get; set; } = PluginCapabilities.None;

    /// <summary>
    /// 可选：插件加载时机。默认在主窗体加载阶段加载。
    /// </summary>
    public PluginLoadTiming LoadTiming { get; set; } = PluginLoadTiming.WindowCreated;
}

/// <summary>
/// 插件能力标志。
/// </summary>
[Flags]
public enum PluginCapabilities
{
    /// <summary>
    /// 仅拥有基础能力（日志、配置、事件总线、UI 提示）。所有插件默认拥有。
    /// </summary>
    None = 0,

    /// <summary>
    /// 允许向设置页贡献自定义设置项。
    /// </summary>
    ContributeSettings = 1 << 0,

    /// <summary>
    /// 允许向工具页贡献自定义工具入口。
    /// </summary>
    ContributeTools = 1 << 1,

    /// <summary>
    /// 允许读取实例（游戏版本文件夹）的只读信息。
    /// </summary>
    /// <remarks>
    /// 仅暴露元信息（名称、路径、版本），不暴露任何启动/主题相关内容。
    /// </remarks>
    ReadInstanceInfo = 1 << 2,

    /// <summary>
    /// 允许订阅事件总线上的实例生命周期事件。
    /// </summary>
    SubscribeInstanceEvents = 1 << 3,

    /// <summary>
    /// 允许注册命令行子命令。
    /// </summary>
    RegisterCliCommand = 1 << 4,

    /// <summary>
    /// 允许向插件页贡献自定义侧边栏入口及右侧面板。
    /// </summary>
    ContributePluginPage = 1 << 6,

    /// <summary>
    /// 允许向主导航栏贡献自定义页面。
    /// </summary>
    ContributeNavigationPage = 1 << 7,

    /// <summary>
    /// 允许向通用扩展点注册贡献项。
    /// </summary>
    RegisterExtension = 1 << 8,

    /// <summary>
    /// 允许注册可由 URI Scheme 触发的自定义动作。
    /// </summary>
    RegisterUriAction = 1 << 9,

    /// <summary>
    /// 允许向关于页法律信息区域贡献链接。
    /// </summary>
    ContributeAboutLegal = 1 << 10,
}

/// <summary>
/// 插件加载时机。
/// </summary>
public enum PluginLoadTiming
{
    /// <summary>
    /// 在主窗体加载完成后加载（推荐，多数插件使用）。
    /// </summary>
    WindowCreated,

    /// <summary>
    /// 在更早的 <c>Loaded</c> 阶段加载。此时 UI 尚未就绪，仅适用于不操作 UI 的插件。
    /// </summary>
    Loaded,
}
