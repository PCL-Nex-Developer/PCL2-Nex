using System;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 所有 PCL 插件必须实现的入口接口。<br/>
/// 一个插件程序集应有且仅有一个类型实现该接口，并使用 <see cref="PluginAttribute"/> 标注。
/// </summary>
/// <remarks>
/// 实现要点：
/// <list type="bullet">
///   <item><description>必须提供公共无参构造函数。</description></item>
///   <item><description><see cref="LoadAsync"/> 在宿主 UI 线程或工作线程调用，长任务应使用 <c>await</c>。</description></item>
///   <item><description><see cref="UnloadAsync"/> 必须释放所有已注册的资源（订阅、UI、文件句柄等）。</description></item>
///   <item><description>插件抛出的未处理异常将被宿主记录，并可能导致插件被禁用。</description></item>
/// </list>
/// </remarks>
public interface IPclPlugin
{
    /// <summary>
    /// 插件加载时调用。此处可注册 UI、订阅事件、读取配置等。
    /// </summary>
    /// <param name="context">插件运行上下文，提供宿主服务访问入口。</param>
    /// <param name="cancellationToken">加载取消令牌。</param>
    Task LoadAsync(IPluginContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 插件卸载时调用。必须清理所有已注册的资源。即使 <see cref="LoadAsync"/> 抛出异常，此方法也可能被调用。
    /// </summary>
    /// <param name="cancellationToken">卸载取消令牌。</param>
    Task UnloadAsync(CancellationToken cancellationToken = default);
}
