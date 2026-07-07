using System;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

public interface IPluginExtensionHost
{
    IDisposable Register<TContribution>(string pluginId, PluginExtensionDescriptor<TContribution> descriptor)
        where TContribution : class;
}