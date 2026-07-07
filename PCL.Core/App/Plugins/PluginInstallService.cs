using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件本地安装生命周期管理。
/// 负责安装、卸载、启用/禁用，以及安装记录的持久化。
/// </summary>
public static class PluginInstallService
{
    private static readonly object _lock = new();

    /// <summary>
    /// 获取所有已安装插件的记录。
    /// </summary>
    public static IReadOnlyList<PluginInstallRecord> GetInstalledPlugins()
    {
        lock (_lock)
        {
            return _LoadAllRecords().AsReadOnly();
        }
    }

    /// <summary>
    /// 获取指定插件的安装记录。
    /// </summary>
    public static PluginInstallRecord? GetRecord(string pluginId)
    {
        lock (_lock)
        {
            return _LoadAllRecords().FirstOrDefault(r => r.PluginId == pluginId);
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
        CancellationToken ct = default)
    {
        var pluginId = manifest.Id;
        var installDir = Path.Combine(PCL.Core.App.Paths.PluginInstalled, _SafeFolderName(pluginId));
        var tempDir = Path.Combine(PCL.Core.App.Paths.PluginTemp, _SafeFolderName(pluginId) + "_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            await Task.Run(() => _CopyDirectory(sourceDirectory, tempDir), ct).ConfigureAwait(false);

            var entryPath = manifest.IsJavaScriptPlugin()
                ? manifest.EntryScript
                : manifest.EntryAssembly;
            var entryFullPath = Path.Combine(tempDir, entryPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(entryFullPath))
                throw new FileNotFoundException($"插件入口文件不存在: {entryPath}");

            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);

            Directory.CreateDirectory(Path.GetDirectoryName(installDir)!);
            Directory.Move(tempDir, installDir);

            var record = new PluginInstallRecord
            {
                PluginId = pluginId,
                InstalledVersion = manifest.Version,
                InstalledFrom = sourceUrl,
                SourceType = sourceType,
                CapabilitiesSnapshot = manifest.Capabilities,
                TrustedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                Enabled = true
            };
            _SaveRecord(record);
            PluginEnablementService.SetEnabled(pluginId, true);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    /// <summary>
    /// 卸载指定插件。
    /// </summary>
    public static Task UninstallAsync(string pluginId)
    {
        lock (_lock)
        {
            // 删除安装目录
            var installDir = Path.Combine(PCL.Core.App.Paths.PluginInstalled, _SafeFolderName(pluginId));
            if (Directory.Exists(installDir))
            {
                try { Directory.Delete(installDir, recursive: true); }
                catch { /* 删除失败不阻断记录清理 */ }
            }

            // 删除数据目录
            var dataDir = Path.Combine(PCL.Core.App.Paths.Plugins, "data", _SafeFolderName(pluginId));
            if (Directory.Exists(dataDir))
            {
                try { Directory.Delete(dataDir, recursive: true); }
                catch { /* 删除失败不阻断 */ }
            }

            // 删除安装记录
            _DeleteRecord(pluginId);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置插件启用/禁用状态。
    /// </summary>
    public static void SetEnabled(string pluginId, bool enabled)
    {
        lock (_lock)
        {
            var record = _LoadAllRecords().FirstOrDefault(r => r.PluginId == pluginId);
            if (record is not null)
            {
                record.Enabled = enabled;
                record.LastUpdatedAt = DateTime.UtcNow;
                _SaveRecord(record);
            }

            PluginEnablementService.SetEnabled(pluginId, enabled);
        }
    }

    /// <summary>
    /// 检查指定插件 ID 是否已安装。
    /// </summary>
    public static bool IsInstalled(string pluginId)
    {
        lock (_lock)
        {
            return _LoadAllRecords().Any(r => r.PluginId == pluginId);
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
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(id.Length);
        foreach (var c in id) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
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
}
