using System;
using System.Threading.Tasks;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 事件总线能力。插件可订阅宿主广播的标准化事件，也可在插件之间发布自定义事件。
/// </summary>
public interface IPluginEventBusApi
{
    /// <summary>
    /// 订阅指定通道的事件。<paramref name="handler"/> 抛出的异常不会影响其他订阅者。
    /// </summary>
    /// <typeparam name="TEvent">事件数据类型</typeparam>
    /// <param name="channel">通道名称</param>
    /// <param name="handler">事件处理委托</param>
    /// <returns>用于取消订阅的 <see cref="IDisposable"/></returns>
    IDisposable Subscribe<TEvent>(string channel, Func<TEvent, Task> handler) where TEvent : PluginEvent;

    /// <summary>
    /// 订阅指定通道的事件（同步处理变体）。
    /// </summary>
    IDisposable Subscribe<TEvent>(string channel, Action<TEvent> handler) where TEvent : PluginEvent;

    /// <summary>
    /// 向指定通道发布事件。
    /// </summary>
    Task PublishAsync<TEvent>(string channel, TEvent eventData) where TEvent : PluginEvent;
}

/// <summary>
/// 所有插件事件数据的基类。
/// </summary>
public abstract class PluginEvent
{
    /// <summary>
    /// 事件发生时间（UTC）。
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 事件源标识（通常是发布者名称）。
    /// </summary>
    public string Source { get; init; } = string.Empty;
}

/// <summary>
/// 标准通道名称常量。插件应使用这些常量而非裸字符串订阅宿主事件。
/// </summary>
public static class PluginEventChannels
{
    /// <summary>宿主启动完成。</summary>
    public const string HostStarted = "pcl:host:started";

    /// <summary>宿主即将关闭。</summary>
    public const string HostStopping = "pcl:host:stopping";

    /// <summary>当前选择的实例发生改变。</summary>
    public const string SelectedInstanceChanged = "pcl:instance:selected-changed";

    /// <summary>实例列表刷新。</summary>
    public const string InstanceListChanged = "pcl:instance:list-changed";

    /// <summary>语言切换。</summary>
    public const string LanguageChanged = "pcl:ui:language-changed";

    /// <summary>插件之间自定义事件的推荐前缀。</summary>
    public const string CustomPrefix = "pcl:plugin:";
}
