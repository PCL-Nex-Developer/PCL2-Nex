using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCL.Core.App.Plugins;

public static class PluginEnablementService
{
    private const string SelfProtectionDisabledDirectoryName = ".self-protection-disabled";

    private static readonly string[] SessionEnabledBaseline = NormalizeEnabledStates(ReadEnabledStates()).ToArray();

    public static bool IsEnabled(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return false;
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

    public static void SetEnabled(string pluginId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
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

    public static void MarkSelfProtectionDisabled(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        Directory.CreateDirectory(_GetSelfProtectionDirectory());
        File.WriteAllText(_GetSelfProtectionMarkerPath(pluginId), DateTimeOffset.UtcNow.ToString("O"));
    }

    public static void ClearSelfProtectionDisabled(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        var markerPath = _GetSelfProtectionMarkerPath(pluginId);
        if (File.Exists(markerPath)) File.Delete(markerPath);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
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

    private static string _SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}