using System.Threading;
using PCL.Core.App.IoC;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

/// <summary>
/// <see cref="IPluginContext"/> 实现。在插件加载时注入。
/// </summary>
internal sealed class PluginContextImpl(
    PluginRecord record,
    string dataDirectory,
    IPluginHost host)
    : IPluginContext
{
    public PluginManifest Manifest => record.Manifest;
    public string DataDirectory => dataDirectory;
    public IPluginHost Host => host;

    public CancellationToken HostStopping
    {
        get
        {
            var cts = new CancellationTokenSource();
            if (Lifecycle.CurrentState >= LifecycleState.Exiting)
            {
                cts.Cancel();
            }
            else
            {
                Lifecycle.When(LifecycleState.Exiting, () => cts.Cancel());
            }
            return cts.Token;
        }
    }
}
