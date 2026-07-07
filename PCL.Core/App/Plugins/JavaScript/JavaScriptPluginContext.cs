using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.JavaScript;

public sealed class JavaScriptPluginContext : IDisposable
{
    private readonly List<IDisposable> _registrations = [];

    internal JavaScriptPluginContext(IPluginContext context, string pluginDirectory, JavaScriptRuntime runtime)
    {
        Context = context;
        Runtime = runtime;
        Manifest = context.Manifest;
        Host = context.Host;
        PluginDirectory = pluginDirectory;
        DataDirectory = context.DataDirectory;
        Core = context.Host.Core;
        Config = context.Host.Config;
        Events = context.Host.Events;
        Extensions = context.Host.Extensions;
        Ui = new JavaScriptUiFacade(this, runtime);
        DotNet = new JavaScriptDotNetFacade(pluginDirectory);
        Vb = new JavaScriptVisualBasicFacade();
    }

    internal JavaScriptRuntime Runtime { get; }
    public IPluginContext Context { get; }
    public PluginManifest Manifest { get; }
    public IPluginHost Host { get; }
    public IPluginCoreApi Core { get; }
    public IPluginCoreApi core => Core;
    public IPluginConfigApi Config { get; }
    public IPluginConfigApi config => Config;
    public IPluginEventBusApi Events { get; }
    public IPluginEventBusApi events => Events;
    public IPluginExtensionApi? Extensions { get; }
    public IPluginExtensionApi? extensions => Extensions;
    public JavaScriptUiFacade Ui { get; }
    public JavaScriptUiFacade ui => Ui;
    public JavaScriptDotNetFacade DotNet { get; }
    public JavaScriptDotNetFacade dotnet => DotNet;
    public JavaScriptVisualBasicFacade Vb { get; }
    public JavaScriptVisualBasicFacade vb => Vb;
    public string PluginDirectory { get; }
    public string pluginDirectory => PluginDirectory;
    public string DataDirectory { get; }
    public string dataDirectory => DataDirectory;

    public object? Application => System.Windows.Application.Current;
    public object? application => Application;
    public object? MainWindow => System.Windows.Application.Current?.MainWindow;
    public object? mainWindow => MainWindow;

    public void Toast(string message) => Core.Hint(message);
    public void toast(string message) => Toast(message);
    public void Hint(string message) => Core.Hint(message);
    public void hint(string message) => Hint(message);
    public void Warn(string message) => Core.Hint(message, PluginHintType.Warning);
    public void warn(string message) => Warn(message);
    public void Log(string message) => Core.GetLogger("js").Info(message);
    public void log(string message) => Log(message);

    public void Track(IDisposable registration) => _registrations.Add(registration);

    public void Dispose()
    {
        for (var i = _registrations.Count - 1; i >= 0; i--)
        {
            try { _registrations[i].Dispose(); }
            catch { }
        }
        _registrations.Clear();
    }
}

public sealed class JavaScriptDotNetFacade
{
    private readonly string _pluginDirectory;
    private readonly List<Assembly> _loadedAssemblies = [];

    public JavaScriptDotNetFacade(string pluginDirectory = "")
    {
        _pluginDirectory = pluginDirectory;
    }

    public string LoadAssembly(string assemblyPath)
    {
        var loaded = TryGetLoadedAssembly(assemblyPath);
        if (loaded is null)
        {
            var fullPath = ResolveAssemblyPath(assemblyPath);
            loaded = _loadedAssemblies.FirstOrDefault(assembly => SameLocation(assembly, fullPath))
                ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => SameLocation(assembly, fullPath))
                ?? Assembly.LoadFrom(fullPath);
        }

        if (!_loadedAssemblies.Any(assembly => string.Equals(assembly.FullName, loaded.FullName, StringComparison.Ordinal)))
            _loadedAssemblies.Add(loaded);

        return loaded.FullName ?? loaded.GetName().Name ?? string.Empty;
    }

    public string loadAssembly(string assemblyPath) => LoadAssembly(assemblyPath);

    public string Reference(string assemblyPath) => LoadAssembly(assemblyPath);

    public string reference(string assemblyPath) => Reference(assemblyPath);

    public string AddReference(string assemblyPath) => LoadAssembly(assemblyPath);

    public string addReference(string assemblyPath) => AddReference(assemblyPath);

    public Type Type(string typeName)
    {
        var type = System.Type.GetType(typeName, throwOnError: false, ignoreCase: false);
        if (type is not null) return type;

        foreach (var assembly in _loadedAssemblies)
        {
            type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null) return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null) return type;
        }

        throw new TypeLoadException($"无法找到 .NET 类型: {typeName}");
    }

    public Type type(string typeName) => Type(typeName);

    public object? New(string typeName, params object?[] args)
    {
        var type = Type(typeName);
        return Activator.CreateInstance(type, args);
    }

    public object? newObject(string typeName, params object?[] args) => New(typeName, args);

    public object? newObj(string typeName, params object?[] args) => New(typeName, args);

    public object? Static(string typeName, string memberName)
    {
        var type = Type(typeName);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var property = type.GetProperty(memberName, flags);
        if (property is not null) return property.GetValue(null);
        var field = type.GetField(memberName, flags);
        if (field is not null) return field.GetValue(null);
        throw new MissingMemberException(type.FullName, memberName);
    }

    public object? @static(string typeName, string memberName) => Static(typeName, memberName);

    public object? staticMember(string typeName, string memberName) => Static(typeName, memberName);

    private Assembly? TryGetLoadedAssembly(string assemblyNameOrPath)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyNameOrPath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(assemblyName)) return null;
        return _loadedAssemblies.Concat(AppDomain.CurrentDomain.GetAssemblies())
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveAssemblyPath(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("程序集路径不能为空。", nameof(assemblyPath));

        var normalized = assemblyPath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new List<string>();
        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_pluginDirectory))
                candidates.Add(Path.Combine(_pluginDirectory, normalized));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, normalized));
            candidates.Add(normalized);
        }

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                if (!string.Equals(Path.GetExtension(fullPath), ".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("只能引用 .dll 程序集。");
                return fullPath;
            }
        }

        throw new FileNotFoundException($"无法找到 .NET 程序集: {assemblyPath}", assemblyPath);
    }

    private static bool SameLocation(Assembly assembly, string fullPath)
        => !assembly.IsDynamic
            && !string.IsNullOrWhiteSpace(assembly.Location)
            && string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase);
}