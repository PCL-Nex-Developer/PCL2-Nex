using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 保存插件包内实验功能的启用状态。状态按插件隔离，安装包更新时会保留，卸载插件时由
/// <see cref="PluginInstallService"/> 一并清理其数据目录。
/// </summary>
public static class PluginExperimentalFeatureService
{
    private const string StateFileName = "experimental-features.json";
    private static readonly object SyncRoot = new();

    /// <summary>获取一个包当前真正可用且已选择的实验功能 Id。</summary>
    public static IReadOnlyList<string> GetEnabledFeatureIds(PluginPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!PluginPackageService.IsValidPluginId(manifest.Id)) return [];

        var features = manifest.ExperimentalFeatures ?? [];
        if (features.Count == 0) return [];

        lock (SyncRoot)
        {
            var selected = new HashSet<string>(ReadState(manifest.Id).EnabledFeatureIds ?? [], StringComparer.OrdinalIgnoreCase);
            return features
                .Where(feature => feature is not null && selected.Contains(feature.Id))
                .Select(feature => feature.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>设置包内一个实验功能是否在下一次启动时加载。</summary>
    public static void SetFeatureEnabled(PluginPackageManifest manifest, string featureId, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!PluginPackageService.IsValidPluginId(manifest.Id))
            throw new ArgumentException("插件 Id 无效。", nameof(manifest));
        if (string.IsNullOrWhiteSpace(featureId))
            throw new ArgumentException("实验功能 Id 不能为空。", nameof(featureId));

        var normalizedId = featureId.Trim();
        if (!(manifest.ExperimentalFeatures ?? []).Any(feature => feature is not null &&
                string.Equals(feature.Id, normalizedId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("实验功能不属于该插件包。", nameof(featureId));

        lock (SyncRoot)
        {
            var state = ReadState(manifest.Id);
            var selected = new HashSet<string>(state.EnabledFeatureIds ?? [], StringComparer.OrdinalIgnoreCase);
            if (enabled) selected.Add(normalizedId);
            else selected.Remove(normalizedId);

            state.EnabledFeatureIds = selected.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            WriteState(manifest.Id, state);
        }
    }

    private static ExperimentalFeatureState ReadState(string pluginId)
    {
        var path = GetStatePath(pluginId);
        if (!File.Exists(path)) return new ExperimentalFeatureState();

        try
        {
            return JsonSerializer.Deserialize<ExperimentalFeatureState>(File.ReadAllText(path), PluginJson.SerializerOptions)
                   ?? new ExperimentalFeatureState();
        }
        catch
        {
            // 用户数据损坏时保持所有实验功能关闭，不阻断启动器加载其他插件。
            return new ExperimentalFeatureState();
        }
    }

    private static void WriteState(string pluginId, ExperimentalFeatureState state)
    {
        var path = GetStatePath(pluginId);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, PluginJson.SerializerOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string GetStatePath(string pluginId)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId))
            throw new ArgumentException("插件 Id 无效。", nameof(pluginId));

        var dataRoot = Path.GetFullPath(Path.Combine(Paths.Plugins, "data"));
        var path = Path.GetFullPath(Path.Combine(dataRoot, pluginId, StateFileName));
        if (!path.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("实验功能状态文件必须位于插件数据目录内。");
        return path;
    }

    private sealed class ExperimentalFeatureState
    {
        public List<string> EnabledFeatureIds { get; set; } = [];
    }
}
