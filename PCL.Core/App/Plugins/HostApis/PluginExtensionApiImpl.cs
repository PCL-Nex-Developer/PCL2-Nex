using System;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.HostApis;

internal sealed class PluginExtensionApiImpl(string pluginId) : IPluginExtensionApi
{
    public IDisposable Register<TContribution>(PluginExtensionDescriptor<TContribution> descriptor)
        where TContribution : class
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var host = PluginHostBridge.Current?.Extensions;
        return host is null ? EmptyDisposable.Instance : host.Register(pluginId, descriptor);
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}