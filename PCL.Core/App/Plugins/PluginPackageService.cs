using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;

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
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.ManifestNull", "清单对象为 null。"));

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.MissingId", "缺少必填字段 Id。"));

        if (!IsValidPluginId(manifest.Id))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidId", "插件 Id 无效。请使用至少两个非空点分段，且仅包含 ASCII 字母、数字、下划线和连字符。"));

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.MissingName", "缺少必填字段 Name。"));

        var legacyField = manifest.AdditionalProperties?.Keys.FirstOrDefault(key => key.Equals("entryType", StringComparison.OrdinalIgnoreCase)
            || key.Equals("loadMethod", StringComparison.OrdinalIgnoreCase)
            || key.Equals("unloadMethod", StringComparison.OrdinalIgnoreCase)
            || key.Equals("entryScript", StringComparison.OrdinalIgnoreCase)
            || key.Equals("runtime", StringComparison.OrdinalIgnoreCase));
        if (legacyField is not null)
            return new PluginPackageValidationResult(
                false,
                Text("Plugins.Package.Error.LegacyFieldRemoved", "旧插件入口字段 {0} 已移除；LoadAsync/UnloadAsync 与 JavaScript 插件不再支持。", legacyField));

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.MissingEntryAssembly", "缺少必填字段 EntryAssembly。"));

        if (manifest.GetAllMixinConfigurationPaths().Count == 0)
            return new PluginPackageValidationResult(
                false,
                Text("Plugins.Package.Error.MissingMixinConfig", "缺少 Mixin 配置；请声明 mixinConfig、mixinConfigs 或 experimentalFeatures 中的功能配置。LoadAsync/UnloadAsync 与 JavaScript 插件已不再支持。"));

        if (manifest.GetAllMixinConfigurationPaths().Any(path => !IsSafeRelativePackagePath(path)))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidMixinConfigPath", "Mixin 配置必须是插件包内的安全相对路径。"));

        var experimentalFeatureValidation = ValidateExperimentalFeatures(manifest);
        if (!experimentalFeatureValidation.IsValid) return experimentalFeatureValidation;

        if (!PluginUpdateService.TryParseVersion(manifest.Version, out _))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidVersion", "Version 无效或未设置。"));

        var dependencyValidation = PluginDependencyService.ValidateDeclarations(manifest.Id, manifest.Dependencies);
        if (!dependencyValidation.IsValid)
            return new PluginPackageValidationResult(false, dependencyValidation.ErrorMessage);

        var logo = string.IsNullOrWhiteSpace(manifest.Logo) ? manifest.Icon : manifest.Logo;
        if (!string.IsNullOrWhiteSpace(logo)
            && !IsHttpUrl(logo)
            && !IsSafeRelativePackagePath(logo))
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidLogo", "Logo 必须是 HTTP/HTTPS URL 或插件包内的安全相对路径。"));

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

    /// <summary>校验实验功能在单个插件包内使用的稳定 Id。</summary>
    public static bool IsValidExperimentalFeatureId(string? featureId)
    {
        if (string.IsNullOrWhiteSpace(featureId) || featureId.Length > 96) return false;
        var trimmed = featureId.Trim();
        if (!string.Equals(featureId, trimmed, StringComparison.Ordinal)) return false;
        if (trimmed.Length == 0 || trimmed[0] is '-' or '_' or '.') return false;
        foreach (var character in trimmed)
        {
            var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (!isAsciiLetter && !isDigit && character is not ('-' or '_' or '.')) return false;
        }
        return true;
    }

    private static PluginPackageValidationResult ValidateExperimentalFeatures(PluginPackageManifest manifest)
    {
        var featureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseConfigurations = new HashSet<string>(manifest.GetMixinConfigurationPaths(), StringComparer.OrdinalIgnoreCase);
        var configurationOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in manifest.ExperimentalFeatures ?? [])
        {
            if (feature is null)
                return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.ExperimentalFeatureNull", "experimentalFeatures 中不能包含 null。"));
            if (!IsValidExperimentalFeatureId(feature.Id))
                return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidExperimentalFeatureId", "实验功能 Id 无效。仅可使用 ASCII 字母、数字、下划线、连字符和点号。"));
            if (!featureIds.Add(feature.Id.Trim()))
                return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.DuplicateExperimentalFeatureId", "实验功能 Id 不能重复。"));
            if (string.IsNullOrWhiteSpace(feature.Name))
                return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.MissingExperimentalFeatureName", "实验功能缺少显示名称。"));

            var configurations = feature.GetMixinConfigurationPaths();
            if (configurations.Count == 0)
                return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.MissingExperimentalFeatureMixinConfig", "每个实验功能都必须声明独立的 Mixin 配置。"));

            if (!string.IsNullOrWhiteSpace(feature.PullRequestUrl) && !IsHttpUrl(feature.PullRequestUrl))
                return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidExperimentalFeaturePullRequestUrl", "实验功能的 PR 地址必须是 HTTP/HTTPS URL。"));

            foreach (var configuration in configurations)
            {
                if (!IsSafeRelativePackagePath(configuration))
                    return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.InvalidMixinConfigPath", "Mixin 配置必须是插件包内的安全相对路径。"));
                if (baseConfigurations.Contains(configuration))
                    return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.ExperimentalFeatureConfigSharedWithBase", "实验功能不能与基础插件共享 Mixin 配置。"));
                if (configurationOwners.TryGetValue(configuration, out _))
                    return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.ExperimentalFeatureConfigShared", "两个实验功能不能共享同一个 Mixin 配置。"));
                configurationOwners[configuration] = feature.Id;
            }
        }

        return new PluginPackageValidationResult(true, null);
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
            return new PluginPackageValidationResult(false, Text("Plugins.Package.Error.ManifestNull", "清单对象为 null。"));

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
            return (null, new PluginPackageValidationResult(false, Text("Plugins.Package.Error.CannotReadPluginJson", "无法读取目录内的 plugin.json。")));

        var result = ValidatePackageManifest(manifest);
        return (manifest, result);
    }

    private static string Text(string key, string fallback, params object?[] args)
    {
        var template = Lang.Text(key);
        if (string.Equals(template, key, StringComparison.Ordinal)
            || string.Equals(template, $"!{key}!", StringComparison.Ordinal))
            template = fallback;
        return string.Format(Lang.Culture, template, args);
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
