using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PCL.Core.App.Localization;

namespace PCL.Core.App.Plugins;

public static class PluginEnablementService
{
    private const string SelfProtectionDisabledDirectoryName = ".self-protection-disabled";
    private const int MaxFailureReasonLength = 1200;

    private static readonly string[] SessionEnabledBaseline = NormalizeEnabledStates(ReadEnabledStates()).ToArray();
    private static readonly object SelfProtectionSyncRoot = new();

    public static bool IsEnabled(string pluginId)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) return false;
        if (IsDisabledBySelfProtection(pluginId)) return false;

        try
        {
            return NormalizeEnabledStates(ReadEnabledStates())
                .Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    internal static void SetEnabled(string pluginId, bool enabled)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) throw new ArgumentException(Text("Plugins.Install.Error.InvalidPluginId", "插件 Id 无效。"), nameof(pluginId));
        if (enabled) ClearSelfProtectionDisabled(pluginId);

        var states = NormalizeEnabledStates(ReadEnabledStates()).ToList();
        states.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (enabled) states.Add(pluginId);

        Config.Plugin.EnabledStates = states;
    }

    public static bool IsDisabledBySelfProtection(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return false;
        return File.Exists(_GetSelfProtectionMarkerPath(pluginId));
    }

    public static void MarkSelfProtectionDisabled(
        string pluginId,
        string? pluginName = null,
        string? pluginVersion = null,
        string? reason = null)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) throw new ArgumentException(Text("Plugins.Install.Error.InvalidPluginId", "插件 Id 无效。"), nameof(pluginId));
        var record = new PluginSelfProtectionRecord
        {
            PluginId = pluginId,
            PluginName = string.IsNullOrWhiteSpace(pluginName) ? pluginId : pluginName.Trim(),
            PluginVersion = string.IsNullOrWhiteSpace(pluginVersion) ? null : pluginVersion.Trim(),
            Reason = NormalizeFailureReason(reason),
            DisabledAt = DateTimeOffset.UtcNow,
            NotificationShown = false
        };
        WriteSelfProtectionRecord(record);
    }

    public static IReadOnlyList<PluginSelfProtectionRecord> GetSelfProtectionDisabledPlugins()
    {
        var directory = _GetSelfProtectionDirectory();
        if (!Directory.Exists(directory)) return [];

        var records = new List<PluginSelfProtectionRecord>();
        foreach (var markerPath in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var pluginId = Path.GetFileName(markerPath);
            if (!PluginPackageService.IsValidPluginId(pluginId)) continue;
            var record = ReadSelfProtectionRecord(pluginId, markerPath);
            if (record is not null) records.Add(record);
        }
        return records.OrderByDescending(record => record.DisabledAt).ToArray();
    }

    public static PluginSelfProtectionRecord? GetSelfProtectionDisabledPlugin(string pluginId)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) return null;
        var markerPath = _GetSelfProtectionMarkerPath(pluginId);
        return File.Exists(markerPath) ? ReadSelfProtectionRecord(pluginId, markerPath) : null;
    }

    public static void MarkSelfProtectionNotificationShown(string pluginId)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) return;
        lock (SelfProtectionSyncRoot)
        {
            var markerPath = _GetSelfProtectionMarkerPath(pluginId);
            if (!File.Exists(markerPath)) return;
            var record = ReadSelfProtectionRecord(pluginId, markerPath);
            if (record is null || record.NotificationShown) return;
            record.NotificationShown = true;
            WriteSelfProtectionRecord(record);
        }
    }

    public static void ClearSelfProtectionDisabled(string pluginId)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) return;
        lock (SelfProtectionSyncRoot)
        {
            var markerPath = _GetSelfProtectionMarkerPath(pluginId);
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    public static IReadOnlyList<string> GetEnabledPluginOrder()
    {
        return NormalizeEnabledStates(ReadEnabledStates()).ToArray();
    }

    public static int CompareByEnabledOrder(string? leftPluginId, string? rightPluginId)
    {
        return CompareByEnabledOrder(leftPluginId, rightPluginId, GetEnabledPluginOrder());
    }

    public static int CompareByEnabledOrder(string? leftPluginId, string? rightPluginId, IReadOnlyList<string> enabledOrder)
    {
        var leftIndex = IndexOf(enabledOrder, leftPluginId);
        var rightIndex = IndexOf(enabledOrder, rightPluginId);
        if (leftIndex >= 0 && rightIndex >= 0) return leftIndex.CompareTo(rightIndex);
        if (leftIndex >= 0) return -1;
        if (rightIndex >= 0) return 1;
        return StringComparer.OrdinalIgnoreCase.Compare(leftPluginId, rightPluginId);
    }

    public static bool MoveEnabledPlugin(string pluginId, int offset)
    {
        if (!PluginPackageService.IsValidPluginId(pluginId)) throw new ArgumentException(Text("Plugins.Install.Error.InvalidPluginId", "插件 Id 无效。"), nameof(pluginId));
        if (offset == 0) return false;

        var states = NormalizeEnabledStates(ReadEnabledStates()).ToList();
        var index = IndexOf(states, pluginId);
        if (index < 0) return false;

        var newIndex = Math.Clamp(index + offset, 0, states.Count - 1);
        if (newIndex == index) return false;

        var item = states[index];
        states.RemoveAt(index);
        states.Insert(newIndex, item);
        Config.Plugin.EnabledStates = states;
        return true;
    }

    public static bool HasPendingRestartChanges()
    {
        var states = NormalizeEnabledStates(ReadEnabledStates()).ToArray();
        return states.Length != SessionEnabledBaseline.Length ||
               states.Where((state, index) => !string.Equals(state, SessionEnabledBaseline[index], StringComparison.OrdinalIgnoreCase)).Any();
    }

    private static IEnumerable<string> ReadEnabledStates()
    {
        try
        {
            return Config.Plugin.EnabledStates ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> NormalizeEnabledStates(IEnumerable<string> states)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in states)
        {
            if (string.IsNullOrWhiteSpace(state)) continue;
            var trimmed = state.Trim();
            if (set.Add(trimmed)) yield return trimmed;
        }
    }

    private static int IndexOf(IReadOnlyList<string> states, string? pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return -1;
        for (var i = 0; i < states.Count; i++)
        {
            if (string.Equals(states[i], pluginId, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string _GetSelfProtectionDirectory()
        => Path.Combine(Paths.Plugins, SelfProtectionDisabledDirectoryName);

    private static string _GetSelfProtectionMarkerPath(string pluginId)
        => Path.Combine(_GetSelfProtectionDirectory(), _SafeFileName(pluginId));

    private static PluginSelfProtectionRecord? ReadSelfProtectionRecord(string pluginId, string markerPath)
    {
        try
        {
            var content = File.ReadAllText(markerPath);
            try
            {
                var record = JsonSerializer.Deserialize<PluginSelfProtectionRecord>(content, PluginJson.SerializerOptions);
                if (record is not null)
                {
                    record.PluginId = pluginId;
                    if (string.IsNullOrWhiteSpace(record.PluginName)) record.PluginName = pluginId;
                    record.Reason = NormalizeFailureReason(record.Reason);
                    return record;
                }
            }
            catch (JsonException)
            {
                // Older versions stored only an ISO-8601 timestamp in this marker.
            }

            var disabledAt = DateTimeOffset.TryParse(content, out var timestamp)
                ? timestamp
                : new DateTimeOffset(File.GetLastWriteTimeUtc(markerPath));
            return new PluginSelfProtectionRecord
            {
                PluginId = pluginId,
                PluginName = pluginId,
                DisabledAt = disabledAt,
                NotificationShown = false
            };
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteSelfProtectionRecord(PluginSelfProtectionRecord record)
    {
        lock (SelfProtectionSyncRoot)
        {
            var directory = _GetSelfProtectionDirectory();
            Directory.CreateDirectory(directory);
            var markerPath = _GetSelfProtectionMarkerPath(record.PluginId);
            var temporaryPath = Path.Combine(directory, "." + record.PluginId + ".tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record, PluginJson.SerializerOptions));
            File.Move(temporaryPath, markerPath, overwrite: true);
        }
    }

    private static string? NormalizeFailureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var normalized = reason.Trim();
        return normalized.Length <= MaxFailureReasonLength
            ? normalized
            : normalized[..MaxFailureReasonLength] + "...";
    }

    private static string _SafeFileName(string value)
    {
        if (!PluginPackageService.IsValidPluginId(value))
            throw new ArgumentException(Text("Plugins.Install.Error.InvalidPluginId", "插件 Id 无效。"), nameof(value));
        return value;
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

public sealed class PluginSelfProtectionRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public string? PluginVersion { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset DisabledAt { get; set; }
    public bool NotificationShown { get; set; }
}
