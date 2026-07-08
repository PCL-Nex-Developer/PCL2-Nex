using System;
using PCL.Core.App.Plugins;
using PCL.Core.App.Plugins.HostApis;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 单个插件的 <see cref="IPluginHost"/> 实现。<br/>
/// 根据插件的 <see cref="PluginCapabilities"/> 决定各能力门面是否可用。
/// </summary>
internal sealed class PluginHostImpl : IPluginHost
{
    private readonly PluginRecord _record;
    private readonly Lazy<IPluginCoreApi> _core;
    private readonly Lazy<IPluginConfigApi> _config;
    private readonly Lazy<IPluginEventBusApi> _events;
    private readonly Lazy<IPluginUiApi?> _ui;
    private readonly Lazy<IInstanceInfoProvider?> _instances;
    private readonly Lazy<ICliCommandRegistrar?> _commands;
    private readonly Lazy<IUriActionRegistrar?> _uriActions;
    private readonly Lazy<IPluginExtensionApi?> _extensions;

    public PluginHostImpl(PluginRecord record)
    {
        _record = record;
        _core = new(() => new PluginCoreApiImpl(record.Id));
        _config = new(() => new PluginConfigApiImpl(record.DataDirectory, record.Id));
        _events = new(() => new PluginEventBusApiImpl());
        _ui = new(() =>
        {
            if (PluginHostBridge.Current?.Ui is null) return null;
            var caps = record.Manifest.Capabilities;
            if ((caps & PluginCapabilities.ContributeSettings) == 0 &&
                (caps & PluginCapabilities.ContributeTools) == 0 &&
                (caps & PluginCapabilities.ContributePluginPage) == 0 &&
                (caps & PluginCapabilities.ContributeNavigationPage) == 0 &&
                (caps & PluginCapabilities.ContributeAboutLegal) == 0) return null;
            return new PluginUiApiImpl(record.Id);
        });
        _instances = new(() =>
        {
            var provider = PluginHostBridge.Current?.Instances;
            if (provider is null) return null;
            if ((_record.Manifest.Capabilities & PluginCapabilities.ReadInstanceInfo) == 0) return null;
            return new InstanceInfoProviderImpl(provider);
        });
        _commands = new(() =>
        {
            if (PluginHostBridge.Current?.Commands is null) return null;
            if ((_record.Manifest.Capabilities & PluginCapabilities.RegisterCliCommand) == 0) return null;
            return new CliCommandRegistrarImpl(record.Id);
        });
        _uriActions = new(() =>
        {
            if (PluginHostBridge.Current?.UriActions is null) return null;
            if ((_record.Manifest.Capabilities & PluginCapabilities.RegisterUriAction) == 0) return null;
            return new UriActionRegistrarImpl(record.Id);
        });
        _extensions = new(() =>
        {
            if (PluginHostBridge.Current?.Extensions is null) return null;
            if ((_record.Manifest.Capabilities & PluginCapabilities.RegisterExtension) == 0) return null;
            return new PluginExtensionApiImpl(record.Id);
        });
    }

    public IPluginCoreApi Core => _core.Value;
    public IPluginConfigApi Config => _config.Value;
    public IPluginEventBusApi Events => _events.Value;
    public IPluginUiApi? Ui => _ui.Value;
    public IInstanceInfoProvider? Instances => _instances.Value;
    public ICliCommandRegistrar? Commands => _commands.Value;
    public IUriActionRegistrar? UriActions => _uriActions.Value;
    public IPluginExtensionApi? Extensions => _extensions.Value;
}
