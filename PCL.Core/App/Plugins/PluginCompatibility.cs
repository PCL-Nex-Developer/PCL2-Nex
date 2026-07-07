using System;
using PCL.Core.Utils;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

internal static class PluginCompatibility
{
    public static string? CurrentHostVersion
    {
        get
        {
            var bridgeVersion = PluginHostBridge.Current?.HostVersion;
            if (!string.IsNullOrWhiteSpace(bridgeVersion)) return bridgeVersion;

            try { return Basics.VersionName; }
            catch { return null; }
        }
    }

    public static bool TryGetApiCompatibilityError(Version? minApiVersion, Version? maxApiVersion, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!ApiVersions.IsCompatible(ApiVersions.Current, minApiVersion, maxApiVersion))
        {
            errorMessage = $"API 版本不兼容：插件支持 {FormatApiRange(minApiVersion, maxApiVersion)}，宿主提供 {ApiVersions.Current}。";
            return true;
        }

        return false;
    }

    public static bool TryGetHostCompatibilityError(
        string? minHostVersion,
        string? maxHostVersion,
        string? currentHostVersion,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        var hasMin = !string.IsNullOrWhiteSpace(minHostVersion);
        var hasMax = !string.IsNullOrWhiteSpace(maxHostVersion);
        if (!hasMin && !hasMax) return false;

        if (!TryParseOptionalSemVer(minHostVersion, nameof(PluginPackageManifest.MinHostVersion), out var minVersion, out errorMessage))
            return true;

        if (!TryParseOptionalSemVer(maxHostVersion, nameof(PluginPackageManifest.MaxHostVersion), out var maxVersion, out errorMessage))
            return true;

        if (string.IsNullOrWhiteSpace(currentHostVersion) || !SemVer.TryParse(currentHostVersion, out var hostVersion))
        {
            errorMessage = "无法获取当前启动器版本，不能确认插件运行兼容性。";
            return true;
        }

        if (minVersion is not null && hostVersion < minVersion)
        {
            errorMessage = $"启动器版本不兼容：插件要求 {FormatHostRange(minHostVersion, maxHostVersion)}，当前版本 {hostVersion}。";
            return true;
        }

        if (maxVersion is not null && hostVersion > maxVersion)
        {
            errorMessage = $"启动器版本不兼容：插件支持 {FormatHostRange(minHostVersion, maxHostVersion)}，当前版本 {hostVersion}。";
            return true;
        }

        return false;
    }

    private static bool TryParseOptionalSemVer(string? value, string fieldName, out SemVer? version, out string errorMessage)
    {
        version = null;
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (SemVer.TryParse(value.Trim(), out version)) return true;

        errorMessage = $"{fieldName} 不是有效的 SemVer 版本号：{value}。";
        return false;
    }

    private static string FormatApiRange(Version? minApiVersion, Version? maxApiVersion)
    {
        if (minApiVersion is not null && maxApiVersion is not null) return $"{minApiVersion} - {maxApiVersion}";
        if (minApiVersion is not null) return $">= {minApiVersion}";
        if (maxApiVersion is not null) return $"<= {maxApiVersion}";
        return "任意版本";
    }

    private static string FormatHostRange(string? minHostVersion, string? maxHostVersion)
    {
        var hasMin = !string.IsNullOrWhiteSpace(minHostVersion);
        var hasMax = !string.IsNullOrWhiteSpace(maxHostVersion);
        if (hasMin && hasMax) return $"{minHostVersion!.Trim()} - {maxHostVersion!.Trim()}";
        if (hasMin) return $">= {minHostVersion!.Trim()}";
        if (hasMax) return $"<= {maxHostVersion!.Trim()}";
        return "任意版本";
    }
}