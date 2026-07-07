using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.App.Plugins;

public static class PluginEnablementService
{
    private static readonly string[] SessionEnabledBaseline = NormalizeEnabledStates(ReadEnabledStates()).ToArray();

    public static bool IsEnabled(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return false;

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

        var states = NormalizeEnabledStates(ReadEnabledStates()).ToList();
        states.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (enabled) states.Add(pluginId);

        Config.Plugin.EnabledStates = states;
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
}