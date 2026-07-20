using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件清单处理服务。
/// 负责读取插件目录内的 <c>plugin.json</c>，以及校验清单合法性。
/// </summary>
public static class PluginPackageService
{
    /// <summary>
    /// 校验包清单是否合法。
    /// </summary>
    /// <param name="manifest">要校验的清单。</param>
    /// <returns>校验结果。</returns>
    public static PluginPackageValidationResult ValidatePackageManifest(PluginPackageManifest manifest)
        => ValidatePackageManifest(manifest, PluginCompatibility.CurrentPclCoreVersion);

    /// <summary>
    /// 校验包清单是否合法。
    /// </summary>
    /// <param name="manifest">要校验的清单。</param>
    /// <param name="currentPclCoreVersion">当前 PCL.Core BaseVersion。</param>
    /// <returns>校验结果。</returns>
    public static PluginPackageValidationResult ValidatePackageManifest(PluginPackageManifest manifest, string? currentPclCoreVersion)
    {
        if (manifest is null)
            return new PluginPackageValidationResult(false, "清单对象为 null。");

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return new PluginPackageValidationResult(false, "缺少必填字段 Id。");

        if (!IsValidPluginId(manifest.Id))
            return new PluginPackageValidationResult(false, "插件 Id 无效。请使用至少两个非空点分段，且仅包含 ASCII 字母、数字、下划线和连字符。");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return new PluginPackageValidationResult(false, "缺少必填字段 Name。");

        var legacyField = manifest.AdditionalProperties?.Keys.FirstOrDefault(key => key.Equals("entryType", StringComparison.OrdinalIgnoreCase)
            || key.Equals("loadMethod", StringComparison.OrdinalIgnoreCase)
            || key.Equals("unloadMethod", StringComparison.OrdinalIgnoreCase)
            || key.Equals("entryScript", StringComparison.OrdinalIgnoreCase)
            || key.Equals("runtime", StringComparison.OrdinalIgnoreCase));
        if (legacyField is not null)
            return new PluginPackageValidationResult(
                false,
                $"旧插件入口字段 {legacyField} 已移除；LoadAsync/UnloadAsync 与 JavaScript 插件不再支持。");

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            return new PluginPackageValidationResult(false, "缺少必填字段 EntryAssembly。");

        if (manifest.GetMixinConfigurationPaths().Count == 0)
            return new PluginPackageValidationResult(
                false,
                "缺少必填字段 mixinConfig 或 mixinConfigs；LoadAsync/UnloadAsync 与 JavaScript 插件已不再支持。");

        if (!PluginUpdateService.TryParseVersion(manifest.Version, out _))
            return new PluginPackageValidationResult(false, "Version 无效或未设置。");

        var dependencyValidation = PluginDependencyService.ValidateDeclarations(manifest.Id, manifest.Dependencies);
        if (!dependencyValidation.IsValid)
            return new PluginPackageValidationResult(false, dependencyValidation.ErrorMessage);

        var logo = string.IsNullOrWhiteSpace(manifest.Logo) ? manifest.Icon : manifest.Logo;
        if (!string.IsNullOrWhiteSpace(logo)
            && !IsHttpUrl(logo)
            && !IsSafeRelativePackagePath(logo))
            return new PluginPackageValidationResult(false, "Logo 必须是 HTTP/HTTPS URL 或插件包内的安全相对路径。");

        var compatibility = ValidateRuntimeCompatibility(manifest, currentPclCoreVersion);
        if (!compatibility.IsValid) return compatibility;

        return new PluginPackageValidationResult(
            true,
            compatibility.ErrorMessage,
            compatibility.CompatibilityStatus);
    }

    public static bool IsValidPluginId(string? pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId.Length > 128) return false;
        var segments = pluginId.Split('.');
        if (segments.Length < 2) return false;
        foreach (var segment in segments)
        {
            if (segment.Length == 0) return false;
            foreach (var character in segment)
            {
                var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
                var isDigit = character is >= '0' and <= '9';
                if (!isAsciiLetter && !isDigit && character is not ('_' or '-')) return false;
            }
        }
        return true;
    }

    private static bool IsHttpUrl(string value)
        => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https";

    private static bool IsSafeRelativePackagePath(string value)
    {
        var path = value.Trim().Replace('\\', '/');
        return !Path.IsPathRooted(path)
               && path.Split('/', StringSplitOptions.RemoveEmptyEntries).All(segment => segment != "..");
    }

    /// <summary>
    /// 校验插件运行时兼容性。用于已安装插件在启动器升级后的加载前检查。
    /// </summary>
    public static PluginPackageValidationResult ValidateRuntimeCompatibility(PluginPackageManifest manifest)
        => ValidateRuntimeCompatibility(manifest, PluginCompatibility.CurrentPclCoreVersion);

    /// <summary>
    /// 校验插件运行时兼容性。用于已安装插件在启动器升级后的加载前检查。
    /// </summary>
    public static PluginPackageValidationResult ValidateRuntimeCompatibility(PluginPackageManifest manifest, string? currentPclCoreVersion)
    {
        if (manifest is null)
            return new PluginPackageValidationResult(false, "清单对象为 null。");

        var status = PluginCompatibility.EvaluatePclCoreVersion(manifest.PclCoreVersion, currentPclCoreVersion);
        return status == PluginCoreCompatibilityStatus.TooOld
            ? new PluginPackageValidationResult(false, PluginCompatibility.GetBlockingMessage(status, manifest.PclCoreVersion), status)
            : new PluginPackageValidationResult(true, status == PluginCoreCompatibilityStatus.Compatible
                ? null
                : PluginCompatibility.GetBlockingMessage(status, manifest.PclCoreVersion), status);
    }

    /// <summary>
    /// 从已解包的插件目录读取 <c>plugin.json</c> manifest。
    /// </summary>
    public static async Task<PluginPackageManifest?> ReadManifestFromDirectoryAsync(string pluginRoot, CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(pluginRoot, "plugin.json");
        if (!File.Exists(manifestPath)) return null;

        try
        {
            await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await JsonSerializer.DeserializeAsync<PluginPackageManifest>(stream, PluginJson.SerializerOptions, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从已解包的插件目录读取清单并执行校验。
    /// </summary>
    public static async Task<(PluginPackageManifest? Manifest, PluginPackageValidationResult Result)> ReadAndValidateDirectoryAsync(
        string pluginRoot, CancellationToken ct = default)
    {
        var manifest = await ReadManifestFromDirectoryAsync(pluginRoot, ct).ConfigureAwait(false);
        if (manifest is null)
            return (null, new PluginPackageValidationResult(false, "无法读取目录内的 plugin.json。"));

        var result = ValidatePackageManifest(manifest);
        return (manifest, result);
    }
}

/// <summary>
/// 包清单校验结果。
/// </summary>
/// <param name="IsValid">是否通过校验。</param>
/// <param name="ErrorMessage">未通过时的错误信息。</param>
public sealed record PluginPackageValidationResult(
    bool IsValid,
    string? ErrorMessage,
    PluginCoreCompatibilityStatus CompatibilityStatus = PluginCoreCompatibilityStatus.Compatible);
