using System;
using System.Threading;
using System.Threading.Tasks;

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
        PluginCoreCompatibilityStatus.Compatible => "兼容",
        PluginCoreCompatibilityStatus.TooOld => "Core 版本过旧",
        PluginCoreCompatibilityStatus.Future => "插件使用未来 Core 版本",
        _ => "未知"
    };

    public static string GetBlockingMessage(PluginCoreCompatibilityStatus status, string? pclCoreVersion) => status switch
    {
        PluginCoreCompatibilityStatus.TooOld => $"插件引用的 PCL.Core 版本 {pclCoreVersion ?? "未知"} 已低于当前启动器支持范围。",
        PluginCoreCompatibilityStatus.Future => "该插件使用了比当前启动器更新的 PCL.Core 版本，可能无法正常使用或导致崩溃。",
        PluginCoreCompatibilityStatus.Unknown => "插件的 pclCoreVersion 缺失或格式错误，无法确认兼容性。",
        _ => string.Empty
    };
}
