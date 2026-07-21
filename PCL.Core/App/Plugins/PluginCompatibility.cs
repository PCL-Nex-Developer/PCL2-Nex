using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;

namespace PCL.Core.App.Plugins;

public enum PluginCoreCompatibilityStatus
{
    Compatible,
    TooOld,
    Future,
    Unknown
}

public enum PluginCompatibilityAction
{
    Install,
    Enable
}

public sealed record PluginCompatibilityConfirmationContext(
    PluginCompatibilityAction Action,
    string PluginId,
    string PluginName,
    string? PclCoreVersion,
    PluginCoreCompatibilityStatus Status);

public static class PluginCompatibility
{
    public const string MinimumSupportedPclCoreVersion = "2026.07.1";

    public static string CurrentPclCoreVersion => Basics.VersionName;

    /// <summary>
    /// Launcher UI hook for Future and Unknown compatibility confirmations.
    /// </summary>
    public static Func<PluginCompatibilityConfirmationContext, CancellationToken, Task<bool>>? ConfirmationAsync { get; set; }

    public static PluginCoreCompatibilityStatus EvaluatePclCoreVersion(
        string? pclCoreVersion,
        string? currentPclCoreVersion = null,
        string? minimumSupportedPclCoreVersion = null)
    {
        if (!LauncherBaseVersion.TryParse(pclCoreVersion, out var pluginVersion))
            return PluginCoreCompatibilityStatus.Unknown;
        if (!LauncherBaseVersion.TryParse(minimumSupportedPclCoreVersion ?? MinimumSupportedPclCoreVersion, out var minimumVersion))
            return PluginCoreCompatibilityStatus.Unknown;
        if (!LauncherBaseVersion.TryParse(currentPclCoreVersion ?? CurrentPclCoreVersion, out var currentVersion))
            return PluginCoreCompatibilityStatus.Unknown;

        if (pluginVersion < minimumVersion) return PluginCoreCompatibilityStatus.TooOld;
        if (pluginVersion > currentVersion) return PluginCoreCompatibilityStatus.Future;
        return PluginCoreCompatibilityStatus.Compatible;
    }

    public static async Task<bool> ConfirmIfRequiredAsync(
        PluginPackageManifest manifest,
        PluginCompatibilityAction action,
        CancellationToken ct = default)
    {
        var status = EvaluatePclCoreVersion(manifest.PclCoreVersion);
        if (status == PluginCoreCompatibilityStatus.Compatible) return true;
        if (status == PluginCoreCompatibilityStatus.TooOld) return false;

        var handler = ConfirmationAsync;
        if (handler is null) return false;
        return await handler(
            new PluginCompatibilityConfirmationContext(action, manifest.Id, manifest.Name, manifest.PclCoreVersion, status),
            ct).ConfigureAwait(false);
    }

    public static string GetDisplayText(PluginCoreCompatibilityStatus status) => status switch
    {
        PluginCoreCompatibilityStatus.Compatible => Lang.Text("Plugins.Compatibility.Status.Compatible"),
        PluginCoreCompatibilityStatus.TooOld => Lang.Text("Plugins.Compatibility.Status.TooOld"),
        PluginCoreCompatibilityStatus.Future => Lang.Text("Plugins.Compatibility.Status.Future"),
        _ => Lang.Text("Plugins.Compatibility.Status.Unknown")
    };

    public static string GetBlockingMessage(PluginCoreCompatibilityStatus status, string? pclCoreVersion) => status switch
    {
        PluginCoreCompatibilityStatus.TooOld => Lang.Text("Plugins.Compatibility.Blocking.TooOld", pclCoreVersion ?? Lang.Text("Common.State.Unknown")),
        PluginCoreCompatibilityStatus.Future => Lang.Text("Plugins.Compatibility.Blocking.Future"),
        PluginCoreCompatibilityStatus.Unknown => Lang.Text("Plugins.Compatibility.Blocking.Unknown"),
        _ => string.Empty
    };
}
