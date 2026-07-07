using System.Threading.Tasks;
using PCL.Core.App.IoC;

namespace PCL.Core.App.Plugins;

[LifecycleScope("plugin-auto-update", "插件自动更新")]
[LifecycleService(LifecycleState.Running)]
public sealed partial class PluginAutoUpdateService
{
    [LifecycleStart]
    private static async Task _StartAsync()
    {
        if (!Config.Plugin.AutoUpdate) return;

        try
        {
            var updates = await PluginUpdateService.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates.Count == 0) return;

            Context.Info("发现 " + updates.Count + " 个插件更新，请在已安装插件页面手动更新。");
        }
        catch (System.Exception ex)
        {
            Context.Warn("自动更新插件失败", ex);
        }
    }
}