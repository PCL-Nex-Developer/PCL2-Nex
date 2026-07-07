using System;
using System.Collections.Generic;
using System.Linq;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.HostApis;

/// <summary>
/// 实例只读信息提供方实现。封装宿主应用注册的 <see cref="IHostInstanceInfoProvider"/>。
/// </summary>
internal sealed class InstanceInfoProviderImpl : IInstanceInfoProvider
{
    private readonly IHostInstanceInfoProvider _inner;

    public InstanceInfoProviderImpl(IHostInstanceInfoProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.InstancesChanged += (_, _) => InstancesChanged?.Invoke(this, EventArgs.Empty);
        _inner.SelectedChanged += (_, _) => SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<InstanceInfo> Instances =>
        _inner.GetInstances().Select(_ToInfo).ToList();

    public InstanceInfo? Selected
    {
        get
        {
            var id = _inner.GetSelectedInstanceId();
            if (string.IsNullOrEmpty(id)) return null;
            return Instances.FirstOrDefault(i => i.Id == id);
        }
    }

    public event EventHandler? InstancesChanged;
    public event EventHandler? SelectedChanged;

    private static InstanceInfo _ToInfo(HostInstanceSnapshot s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Path = s.Path,
        Version = s.Version,
        Loader = s.Loader,
        LoaderVersion = s.LoaderVersion,
        IconPath = s.IconPath
    };
}

/// <summary>
/// 命令行子命令注册器实现。
/// </summary>
internal sealed class CliCommandRegistrarImpl(string pluginId) : ICliCommandRegistrar
{
    public IDisposable RegisterCommand(CliCommandDescriptor descriptor)
    {
        var host = PluginHostBridge.Current?.Commands
            ?? throw new InvalidOperationException("命令行注册器尚未就绪");
        return host.RegisterCommand(pluginId, descriptor.Name, descriptor.Description, descriptor.Usage, args => descriptor.Handler(args));
    }
}

/// <summary>
/// URI Scheme 动作注册器实现。
/// </summary>
internal sealed class UriActionRegistrarImpl(string pluginId) : IUriActionRegistrar
{
    public IDisposable RegisterAction(UriActionDescriptor descriptor)
    {
        var host = PluginHostBridge.Current?.UriActions
            ?? throw new InvalidOperationException("URI 动作注册器尚未就绪");
        return host.RegisterAction(pluginId, descriptor.Name, descriptor.Description, descriptor.Usage, context =>
            descriptor.Handler(new PluginUriActionContext
            {
                Scheme = context.Scheme,
                RawUri = context.RawUri,
                Action = context.Action,
                Arguments = context.Arguments,
                Query = context.Query
            }));
    }
}
