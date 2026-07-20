using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace PCL.Core.App.Plugins;

/// <summary>已应用 Mixin 的插件程序集记录。</summary>
public sealed class PluginRecord
{
    public required PluginPackageManifest Manifest { get; init; }
    public Assembly? Assembly { get; internal set; }
    public AssemblyLoadContext? LoadContext { get; internal set; }
    public required string AssemblyPath { get; init; }
    public required string PluginDirectory { get; init; }
    public IReadOnlyList<string> AppliedMixinConfigurations { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public PluginState State { get; set; } = PluginState.Created;
    public Exception? LastException { get; set; }
    public string Id => Manifest.Id;
}

public enum PluginState
{
    Created,
    Running,
    Disabled
}
