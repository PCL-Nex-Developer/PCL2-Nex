using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Plugin.Abstractions;

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
        => ValidatePackageManifest(manifest, PluginCompatibility.CurrentHostVersion);

    /// <summary>
    /// 校验包清单是否合法。
    /// </summary>
    /// <param name="manifest">要校验的清单。</param>
    /// <param name="currentHostVersion">当前启动器版本。传入 <c>null</c> 时仅在清单声明启动器版本约束时失败。</param>
    /// <returns>校验结果。</returns>
    public static PluginPackageValidationResult ValidatePackageManifest(PluginPackageManifest manifest, string? currentHostVersion)
    {
        if (manifest is null)
            return new PluginPackageValidationResult(false, "清单对象为 null。");

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return new PluginPackageValidationResult(false, "缺少必填字段 Id。");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return new PluginPackageValidationResult(false, "缺少必填字段 Name。");

        var runtime = string.IsNullOrWhiteSpace(manifest.Runtime)
            ? PluginPackageManifest.RuntimeDotNet
            : manifest.Runtime.Trim();

        if (string.Equals(runtime, PluginPackageManifest.RuntimeJavaScriptV8, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(manifest.EntryScript))
                return new PluginPackageValidationResult(false, "JavaScript 插件缺少必填字段 EntryScript。");
        }
        else if (string.Equals(runtime, PluginPackageManifest.RuntimeDotNet, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                return new PluginPackageValidationResult(false, "缺少必填字段 EntryAssembly。");
        }
        else
        {
            return new PluginPackageValidationResult(false, $"不支持的插件运行时：{manifest.Runtime}。");
        }

        if (manifest.Version is null || manifest.Version == new Version(0, 0, 0, 0))
            return new PluginPackageValidationResult(false, "Version 无效或未设置。");

        if (manifest.MinApiVersion is null || manifest.MinApiVersion == new Version(0, 0, 0, 0))
            return new PluginPackageValidationResult(false, "MinApiVersion 无效或未设置。");

        var compatibility = ValidateRuntimeCompatibility(manifest, currentHostVersion);
        if (!compatibility.IsValid) return compatibility;

        return new PluginPackageValidationResult(true, null);
    }

    /// <summary>
    /// 校验插件运行时兼容性。用于已安装插件在启动器升级后的加载前检查。
    /// </summary>
    public static PluginPackageValidationResult ValidateRuntimeCompatibility(PluginPackageManifest manifest)
        => ValidateRuntimeCompatibility(manifest, PluginCompatibility.CurrentHostVersion);

    /// <summary>
    /// 校验插件运行时兼容性。用于已安装插件在启动器升级后的加载前检查。
    /// </summary>
    public static PluginPackageValidationResult ValidateRuntimeCompatibility(PluginPackageManifest manifest, string? currentHostVersion)
    {
        if (manifest is null)
            return new PluginPackageValidationResult(false, "清单对象为 null。");

        if (manifest.MaxApiVersion == new Version(0, 0, 0, 0))
            return new PluginPackageValidationResult(false, "MaxApiVersion 无效。");

        if (PluginCompatibility.TryGetApiCompatibilityError(manifest.MinApiVersion, manifest.MaxApiVersion, out var apiError))
            return new PluginPackageValidationResult(false, apiError);

        if (PluginCompatibility.TryGetHostCompatibilityError(manifest.MinHostVersion, manifest.MaxHostVersion, currentHostVersion, out var hostError))
            return new PluginPackageValidationResult(false, hostError);

        return new PluginPackageValidationResult(true, null);
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
public sealed record PluginPackageValidationResult(bool IsValid, string? ErrorMessage);
