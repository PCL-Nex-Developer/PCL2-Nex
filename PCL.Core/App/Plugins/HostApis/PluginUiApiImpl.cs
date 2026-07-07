using System;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.HostApis;

/// <summary>
/// 插件 UI API 实现。桥接到宿主应用注册的 <see cref="IUiExtensionHost"/>。
/// </summary>
internal sealed class PluginUiApiImpl(string pluginId) : IPluginUiApi
{
    private IUiExtensionHost? Host => PluginHostBridge.Current?.Ui;

    public IDisposable ContributeSettingsPanel(SettingsPanelDescriptor descriptor)
    {
        var host = Host ?? throw new InvalidOperationException("UI 扩展宿主尚未就绪");
        return host.RegisterSettingsPanel(pluginId, descriptor.Id, descriptor.Title, descriptor.Group, descriptor.Order, descriptor.Icon, descriptor.Factory);
    }

    public IDisposable ContributeToolsPanel(ToolsPanelDescriptor descriptor)
    {
        var host = Host ?? throw new InvalidOperationException("UI 扩展宿主尚未就绪");
        return host.RegisterToolsPanel(pluginId, descriptor.Id, descriptor.Title, descriptor.Group, descriptor.Order, descriptor.Icon, descriptor.Factory);
    }

    public IDisposable ContributePluginPanel(PluginPanelDescriptor descriptor)
    {
        var host = Host ?? throw new InvalidOperationException("UI 扩展宿主尚未就绪");
        return host.RegisterPluginPanel(pluginId, descriptor.Id, descriptor.Title, descriptor.Order, descriptor.Icon, descriptor.Factory);
    }

    public IDisposable ContributeNavigationPage(NavigationPageDescriptor descriptor)
    {
        var host = Host ?? throw new InvalidOperationException("UI 扩展宿主尚未就绪");
        return host.RegisterNavigationPage(pluginId, descriptor.Id, descriptor.Title, descriptor.Order, descriptor.Icon, descriptor.Factory);
    }

    public IDisposable ContributeAboutLegalLink(AboutLegalLinkDescriptor descriptor)
    {
        var host = Host ?? throw new InvalidOperationException("UI 扩展宿主尚未就绪");
        return host.RegisterAboutLegalLink(pluginId, descriptor.Id, descriptor.Title, descriptor.Url, descriptor.Order, descriptor.IsHighlighted);
    }

    public void InvokeOnUi(Action action)
    {
        var host = Host;
        if (host is not null && !host.CheckAccess()) host.InvokeOnUi(action);
        else action();
    }

    public T InvokeOnUi<T>(Func<T> action)
    {
        var host = Host;
        if (host is null || host.CheckAccess()) return action();
        T result = default!;
        host.InvokeOnUi(() => result = action());
        return result!;
    }

    public bool CheckAccess() => Host?.CheckAccess() ?? true;
}
