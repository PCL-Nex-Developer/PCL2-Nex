using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 插件运行上下文，在 <see cref="IPclPlugin.LoadAsync"/> 时由宿主注入。
/// 通过本对象获取宿主暴露的各项能力门面。
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// 当前插件的清单。
    /// </summary>
    PluginManifest Manifest { get; }

    /// <summary>
    /// 插件专属数据目录（已存在，可直接读写）。
    /// 插件应将自身产生的所有持久化数据写入此处。
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// 主能力门面。所有受能力标志保护的服务均通过此对象获取。
    /// </summary>
    IPluginHost Host { get; }

    /// <summary>
    /// 宿主提供的取消令牌，在宿主关闭或插件即将被卸载时触发。
    /// </summary>
    System.Threading.CancellationToken HostStopping { get; }
}
