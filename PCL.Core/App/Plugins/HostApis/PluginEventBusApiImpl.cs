using System;
using System.Threading.Tasks;
using PCL.Core.App.EventBus;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.HostApis;

/// <summary>
/// 插件事件总线 API 实现。桥接到 PCL.Core 的 <see cref="EventBusService"/>。
/// </summary>
internal sealed class PluginEventBusApiImpl : IPluginEventBusApi
{
    public IDisposable Subscribe<TEvent>(string channel, Func<TEvent, Task> handler) where TEvent : PluginEvent
    {
        if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("channel required", nameof(channel));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        return EventBusService.Subscribe<PluginEventBridge>(channel, bridge =>
        {
            if (bridge.Payload is TEvent typed) return handler(typed);
            return Task.CompletedTask;
        });
    }

    public IDisposable Subscribe<TEvent>(string channel, Action<TEvent> handler) where TEvent : PluginEvent
    {
        return Subscribe<TEvent>(channel, ev => { handler(ev); return Task.CompletedTask; });
    }

    public Task PublishAsync<TEvent>(string channel, TEvent eventData) where TEvent : PluginEvent
    {
        if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("channel required", nameof(channel));
        if (eventData is null) throw new ArgumentNullException(nameof(eventData));

        var bridge = new PluginEventBridge(Guid.NewGuid(), channel, eventData);
        return EventBusService.PublishAsync(channel, bridge);
    }
}

/// <summary>
/// 承载任意 <see cref="PluginEvent"/> 的桥接事件数据。<br/>
/// 复用 PCL.Core 的强类型事件总线：插件订阅者注册此具体类型，
/// 在处理器内通过 <see cref="Payload"/> 取出真实负载。
/// </summary>
internal sealed record PluginEventBridge(Guid Id, string Channel, PluginEvent Payload) : EventDataBase(Id, Channel);

