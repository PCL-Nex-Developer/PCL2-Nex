using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 宿主能力门面。插件通过本对象访问宿主提供的服务。<br/>
/// 每个子服务接口的获取均受到 <see cref="PluginCapabilities"/> 的保护：未声明的能力在获取时将返回
/// <see langword="null"/>（或抛出由实现决定的安全异常）。
/// </summary>
/// <remarks>
/// <b>许可证与安全约束</b>：本门面有意<b>不</b>暴露任何与游戏启动、账户登录、主题/品牌资产相关的入口。
/// 插件无法通过本 SDK 触发 Minecraft 启动流程，也无法读取或修改主题外观。
/// </remarks>
public interface IPluginHost
{
    /// <summary>
    /// 基础能力：日志、提示、本地化、文件系统辅助。
    /// 所有插件均可获取，不受 <see cref="PluginCapabilities"/> 限制。
    /// </summary>
    IPluginCoreApi Core { get; }

    /// <summary>
    /// 基础能力：插件配置读写。
    /// </summary>
    IPluginConfigApi Config { get; }

    /// <summary>
    /// 基础能力：事件总线订阅/发布。
    /// </summary>
    IPluginEventBusApi Events { get; }

    /// <summary>
    /// UI 能力。在 <see cref="PluginLoadTiming.WindowCreated"/> 之后才可用。
    /// 需要 <see cref="PluginCapabilities.ContributeSettings"/> 或
    /// <see cref="PluginCapabilities.ContributeTools"/> 才能注册 UI 扩展。
    /// </summary>
    IPluginUiApi? Ui { get; }

    /// <summary>
    /// 实例只读信息。需要 <see cref="PluginCapabilities.ReadInstanceInfo"/>。
    /// </summary>
    IInstanceInfoProvider? Instances { get; }

    /// <summary>
    /// 命令行子命令注册。需要 <see cref="PluginCapabilities.RegisterCliCommand"/>。
    /// </summary>
    ICliCommandRegistrar? Commands { get; }

    /// <summary>
    /// URI Scheme 动作注册。需要 <see cref="PluginCapabilities.RegisterUriAction"/>。
    /// </summary>
    IUriActionRegistrar? UriActions { get; }

    /// <summary>
    /// 通用扩展点注册。需要 <see cref="PluginCapabilities.RegisterExtension"/>。
    /// </summary>
    IPluginExtensionApi? Extensions { get; }
}
