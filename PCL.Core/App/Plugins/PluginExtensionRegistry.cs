using System;
using System.Collections.Generic;
using System.Linq;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

public sealed class PluginExtensionRegistry : IPluginExtensionHost
{
    private readonly List<PluginExtensionEntry> _entries = [];
    private readonly object _lock = new();

    public event EventHandler? Changed;

    public IDisposable Register<TContribution>(string pluginId, PluginExtensionDescriptor<TContribution> descriptor)
        where TContribution : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ExtensionPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);
        ArgumentNullException.ThrowIfNull(descriptor.Contribution);

        var entry = new PluginExtensionEntry(
            pluginId,
            descriptor.ExtensionPoint,
            descriptor.Id,
            descriptor.DisplayName,
            descriptor.Order,
            descriptor.Metadata,
            descriptor.Contribution);

        lock (_lock)
        {
            _entries.RemoveAll(e =>
                string.Equals(e.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.ExtensionPoint, descriptor.ExtensionPoint, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase));
            _entries.Add(entry);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new Remover(this, entry);
    }

    public IReadOnlyList<PluginExtensionEntry> GetAll(string extensionPoint)
    {
        lock (_lock)
        {
            return _entries
                .Where(e => string.Equals(e.ExtensionPoint, extensionPoint, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Order)
                .ThenBy(e => e.PluginId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<PluginExtensionEntry<TContribution>> GetAll<TContribution>(string extensionPoint)
        where TContribution : class
    {
        lock (_lock)
        {
            return _entries
                .Where(e => string.Equals(e.ExtensionPoint, extensionPoint, StringComparison.OrdinalIgnoreCase))
                .Where(e => e.Contribution is TContribution)
                .OrderBy(e => e.Order)
                .ThenBy(e => e.PluginId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .Select(e => new PluginExtensionEntry<TContribution>(
                    e.PluginId,
                    e.ExtensionPoint,
                    e.Id,
                    e.DisplayName,
                    e.Order,
                    e.Metadata,
                    (TContribution)e.Contribution))
                .ToList();
        }
    }

    public TContribution? GetDefault<TContribution>(string extensionPoint)
        where TContribution : class
        => GetAll<TContribution>(extensionPoint).FirstOrDefault()?.Contribution;

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Remover(PluginExtensionRegistry registry, PluginExtensionEntry entry) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _done, 1) == 1) return;
            lock (registry._lock) { registry._entries.Remove(entry); }
            registry.Changed?.Invoke(registry, EventArgs.Empty);
        }
    }
}

public sealed record PluginExtensionEntry(
    string PluginId,
    string ExtensionPoint,
    string Id,
    string DisplayName,
    int Order,
    IReadOnlyDictionary<string, string> Metadata,
    object Contribution);

public sealed record PluginExtensionEntry<TContribution>(
    string PluginId,
    string ExtensionPoint,
    string Id,
    string DisplayName,
    int Order,
    IReadOnlyDictionary<string, string> Metadata,
    TContribution Contribution)
    where TContribution : class;