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
/// 插件本地安装生命周期管理。
/// 负责安装、卸载、启用/禁用，以及安装记录的持久化。
/// </summary>
public static class PluginInstallService
{
    private static readonly object _lock = new();
    private static readonly SemaphoreSlim _installLock = new(1, 1);

    /// <summary>
    /// 获取所有已安装插件的记录。
    /// </summary>
    public static IReadOnlyList<PluginInstallRecord> GetInstalledPlugins()
    {
        lock (_lock)
        {
            return _LoadInstalledRecords().AsReadOnly();
        }
    }

    /// <summary>
    /// 获取指定插件的安装记录。
    /// </summary>
    public static PluginInstallRecord? GetRecord(string pluginId)
    {
        lock (_lock)
        {
            return _LoadInstalledRecords().FirstOrDefault(r => r.PluginId == pluginId);
        }
    }

    /// <summary>
    /// 安装已解包的插件目录到本地。
    /// </summary>
    public static async Task InstallFromDirectoryAsync(
        string sourceDirectory,
        PluginPackageManifest manifest,
        PluginInstallSourceType sourceType,
        string sourceUrl,
        CancellationToken ct = default,
        string? installedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var validation = PluginPackageService.ValidatePackageManifest(manifest);
        if (!validation.IsValid)
            throw new InvalidDataException(validation.ErrorMessage ?? Lang.Text("Plugins.Install.Error.ManifestValidationFailed"));

        var compatibility = validation.CompatibilityStatus;
        if (compatibility == PluginCoreCompatibilityStatus.TooOld)
            throw new InvalidDataException(PluginCompatibility.GetBlockingMessage(compatibility, manifest.PclCoreVersion));
        if (compatibility is PluginCoreCompatibilityStatus.Future or PluginCoreCompatibilityStatus.Unknown
            && !await PluginCompatibility.ConfirmIfRequiredAsync(manifest, PluginCompatibilityAction.Install, ct).ConfigureAwait(false))
            throw new OperationCanceledException(Lang.Text("Plugins.Install.Error.UserCancelledCompatibility"), ct);

        var dependencyCheck = PluginDependencyService.CheckInstalledDependencies(manifest);
        if (!dependencyCheck.IsValid)
            throw new InvalidDataException(dependencyCheck.ErrorMessage ?? Lang.Text("Plugins.Install.Error.DependencyCheckFailed"));

        var pluginId = manifest.Id;
        var safePluginId = _SafeFolderName(pluginId);
        var installDir = Path.Combine(PCL.Core.App.Paths.PluginInstalled, safePluginId);
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(PCL.Core.App.Paths.PluginTemp, safePluginId + "_" + operationId);
        var stagingDir = Path.Combine(PCL.Core.App.Paths.PluginInstalled, "." + safePluginId + ".staging-" + operationId);
        var backupDir = Path.Combine(PCL.Core.App.Paths.PluginInstalled, "." + safePluginId + ".backup-" + operationId);
        var recordPath = _RecordPath(pluginId);
        string? originalRecordJson = null;
        List<string>? originalEnabledStates = null;
        List<string>? originalManifestSubscriptions = null;
        var swapStarted = false;
        var backupCreated = false;

        await _installLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(recordPath)) originalRecordJson = File.ReadAllText(recordPath);
            try { originalEnabledStates = Config.Plugin.EnabledStates?.ToList(); }
            catch { }
            try { originalManifestSubscriptions = Config.Plugin.ManifestSubscriptions?.ToList(); }
            catch { }

            await Task.Run(() => _CopyDirectory(sourceDirectory, tempDir), ct).ConfigureAwait(false);

            var entryPath = manifest.EntryAssembly;
            var entryFullPath = PluginLoaderService.ResolvePackagePath(tempDir, entryPath);
            if (!File.Exists(entryFullPath))
                throw new FileNotFoundException(Lang.Text("Plugins.Install.Error.EntryAssemblyNotFound", entryPath));
            // 实验功能即使默认关闭，也必须在安装时验证其 Mixin 配置存在，避免用户后续
            // 勾选时才遇到损坏的插件包。
            foreach (var mixinConfig in manifest.GetAllMixinConfigurationPaths())
            {
                var configFullPath = PluginLoaderService.ResolvePackagePath(tempDir, mixinConfig);
                if (!File.Exists(configFullPath))
                    throw new FileNotFoundException(Lang.Text("Plugins.Install.Error.MixinConfigNotFound", mixinConfig));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(installDir)!);
            _CopyDirectory(tempDir, stagingDir);

            swapStarted = true;
            if (Directory.Exists(installDir))
            {
                Directory.Move(installDir, backupDir);
                backupCreated = true;
            }
            Directory.Move(stagingDir, installDir);

            var record = new PluginInstallRecord
            {
                PluginId = pluginId,
                InstalledVersion = manifest.Version,
                InstalledFrom = sourceUrl,
                SourceType = sourceType,
                InstalledSha256 = string.IsNullOrWhiteSpace(installedSha256) ? null : installedSha256.Trim(),
                TrustedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                Enabled = true
            };
            _SaveRecord(record);
            if (sourceType == PluginInstallSourceType.Manifest && !string.IsNullOrWhiteSpace(sourceUrl))
                AddManifestSubscription(sourceUrl);
            PluginEnablementService.SetEnabled(pluginId, true);

            if (backupCreated && Directory.Exists(backupDir)) Directory.Delete(backupDir, recursive: true);
        }
        catch
        {
            if (swapStarted)
            {
                try
                {
                    if (Directory.Exists(installDir)) Directory.Delete(installDir, recursive: true);
                    if (backupCreated && Directory.Exists(backupDir)) Directory.Move(backupDir, installDir);
                }
                catch { }

                try
                {
                    if (originalRecordJson is null)
                    {
                        if (File.Exists(recordPath)) File.Delete(recordPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
                        File.WriteAllText(recordPath, originalRecordJson);
                    }
                    if (originalEnabledStates is not null) Config.Plugin.EnabledStates = originalEnabledStates;
                    if (originalManifestSubscriptions is not null)
                        Config.Plugin.ManifestSubscriptions = originalManifestSubscriptions;
                }
                catch { }
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { }
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); }
            catch { }
            _installLock.Release();
        }
    }

    public static void AddManifestSubscription(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl)) return;
        var value = manifestUrl.Trim();
        var subscriptions = Config.Plugin.ManifestSubscriptions?.ToList() ?? [];
        if (subscriptions.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        subscriptions.Add(value);
        Config.Plugin.ManifestSubscriptions = subscriptions;
    }

    /// <summary>
    /// 卸载指定插件。
    /// </summary>
    public static Task UninstallAsync(string pluginId)
    {
        lock (_lock)
        {
            if (!PluginPackageService.IsValidPluginId(pluginId))
                throw new ArgumentException(Lang.Text("Plugins.Install.Error.InvalidPluginId"), nameof(pluginId));

            // 删除安装目录
            var installDir = Path.Combine(PCL.Core.App.Paths.PluginInstalled, _SafeFolderName(pluginId));
            if (Directory.Exists(installDir))
                // The install record must remain intact when the executable/plugin directory is
                // locked. This leaves a disabled, retryable installation instead of an orphan.
                Directory.Delete(installDir, recursive: true);

            // 删除数据目录
            var dataDir = Path.Combine(PCL.Core.App.Paths.Plugins, "data", _SafeFolderName(pluginId));
            if (Directory.Exists(dataDir))
            {
                try { Directory.Delete(dataDir, recursive: true); }
                catch { /* 删除失败不阻断 */ }
            }

            // 删除安装记录
            _DeleteRecord(pluginId);
            PluginEnablementService.ClearSelfProtectionDisabled(pluginId);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置插件启用/禁用状态。
    /// </summary>
    public static void SetEnabled(string pluginId, bool enabled)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId))
            throw new ArgumentException(Lang.Text("Plugins.Install.Error.InvalidPluginId"), nameof(pluginId));
        if (enabled)
            throw new InvalidOperationException(Lang.Text("Plugins.Install.Error.EnableThroughAsyncRequired"));

        SetEnabledState(pluginId, false);
    }

    private static void SetEnabledState(string pluginId, bool enabled)
    {
        lock (_lock)
        {
            var record = _LoadInstalledRecords().FirstOrDefault(r => r.PluginId == pluginId);
            if (record is not null)
            {
                record.Enabled = enabled;
                record.LastUpdatedAt = DateTime.UtcNow;
                _SaveRecord(record);
            }

            PluginEnablementService.SetEnabled(pluginId, enabled);
        }
    }

    public static async Task<bool> SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
    {
        if (enabled)
        {
            var pluginRoot = Path.Combine(PCL.Core.App.Paths.PluginInstalled, _SafeFolderName(pluginId));
            var manifest = await PluginPackageService.ReadManifestFromDirectoryAsync(pluginRoot, ct).ConfigureAwait(false);
            if (manifest is null) throw new InvalidDataException(Lang.Text("Plugins.Install.Error.CannotReadManifest"));

            var compatibility = PluginCompatibility.EvaluatePclCoreVersion(manifest.PclCoreVersion);
            if (compatibility == PluginCoreCompatibilityStatus.TooOld)
                throw new InvalidDataException(PluginCompatibility.GetBlockingMessage(compatibility, manifest.PclCoreVersion));
            if (compatibility is PluginCoreCompatibilityStatus.Future or PluginCoreCompatibilityStatus.Unknown
                && !await PluginCompatibility.ConfirmIfRequiredAsync(manifest, PluginCompatibilityAction.Enable, ct).ConfigureAwait(false))
                return false;

            var dependencyCheck = PluginDependencyService.CheckInstalledDependencies(manifest);
            if (!dependencyCheck.IsValid)
                throw new InvalidOperationException(dependencyCheck.ErrorMessage ?? Lang.Text("Plugins.Install.Error.DependencyCheckFailed"));
        }

        SetEnabledState(pluginId, enabled);
        return true;
    }

    /// <summary>
    /// 检查指定插件 ID 是否已安装。
    /// </summary>
    public static bool IsInstalled(string pluginId)
    {
        lock (_lock)
        {
            return _LoadInstalledRecords().Any(r => r.PluginId == pluginId);
        }
    }

    #region Record Persistence

    private static string _RecordsDir => PCL.Core.App.Paths.PluginTrust;

    private static string _RecordPath(string pluginId) =>
        Path.Combine(_RecordsDir, _SafeFolderName(pluginId) + ".json");

    private static List<PluginInstallRecord> _LoadAllRecords()
    {
        var dir = _RecordsDir;
        if (!Directory.Exists(dir)) return [];

        var records = new List<PluginInstallRecord>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            // 跳过 repositories.json（信任记录文件）
            if (Path.GetFileName(file) == "repositories.json") continue;

            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<PluginInstallRecord>(json, PluginJson.SerializerOptions);
                if (record is not null) records.Add(record);
            }
            catch
            {
                // 跳过损坏的记录文件
            }
        }
        return records;
    }

    private static List<PluginInstallRecord> _LoadInstalledRecords()
    {
        var installedIds = _GetInstalledPluginIds();
        if (installedIds.Count == 0)
        {
            foreach (var record in _LoadAllRecords())
                _DeleteRecord(record.PluginId);
            return [];
        }

        var records = new List<PluginInstallRecord>();
        foreach (var record in _LoadAllRecords())
        {
            if (installedIds.Contains(record.PluginId))
            {
                records.Add(record);
            }
            else
            {
                _DeleteRecord(record.PluginId);
            }
        }
        return records;
    }

    private static HashSet<string> _GetInstalledPluginIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var (manifest, _) in PluginLoaderService.EnumerateInstalledPluginPackages(PCL.Core.App.Paths.PluginInstalled))
            {
                if (!string.IsNullOrWhiteSpace(manifest.Id)) ids.Add(manifest.Id);
            }
        }
        catch { }

        try
        {
            foreach (var loaded in PluginLoaderService.LoadedPlugins)
            {
                if (!string.IsNullOrWhiteSpace(loaded.Id)) ids.Add(loaded.Id);
            }
        }
        catch { }

        return ids;
    }

    private static void _SaveRecord(PluginInstallRecord record)
    {
        Directory.CreateDirectory(_RecordsDir);
        var path = _RecordPath(record.PluginId);
        var json = JsonSerializer.Serialize(record, PluginJson.SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static void _DeleteRecord(string pluginId)
    {
        var path = _RecordPath(pluginId);
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch { /* 删除失败不阻断 */ }
        }
    }

    #endregion

    private static string _SafeFolderName(string id)
    {
        if (!PluginPackageService.IsValidPluginId(id))
            throw new ArgumentException(Text("Plugins.Install.Error.InvalidPluginId", "插件 Id 无效。"), nameof(id));
        return id;
    }

    private static void _CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
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
