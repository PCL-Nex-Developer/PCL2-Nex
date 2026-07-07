using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.IoC;
using PCL.Core.App.Plugins.JavaScript;
using PCL.Core.App.Plugins;
using PCL.Core.Logging;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件加载与生命周期管理。<br/>
/// 扫描 <see cref="Paths.PluginInstalled"/> 下的结构化插件目录，
/// 读取 <c>plugin.json</c> 定位入口程序集，在隔离的可收集
/// <see cref="AssemblyLoadContext"/> 中加载并运行。
/// 同时兼容旧布局（根目录平铺 DLL）。
/// </summary>
[LifecycleService(LifecycleState.WindowCreated, Priority = -100)]
public sealed class PluginLoaderService : GeneralService
{
    private static readonly List<PluginRecord> _Records = [];
    private static readonly object _Lock = new();
    private static LifecycleContext? _context;

    public PluginLoaderService() : base("plugin-loader", "插件加载器", asyncStart: false)
    {
        _context = ServiceContext;
    }

    /// <summary>
    /// 当前已加载的插件记录（只读快照）。
    /// </summary>
    public static IReadOnlyList<PluginRecord> LoadedPlugins
    {
        get { lock (_Lock) { return _Records.ToList(); } }
    }

    /// <inheritdoc />
    public override void Start()
    {
        LoadAll();
    }

    /// <inheritdoc />
    public override void Stop()
    {
        _UnloadAll();
    }

    /// <summary>
    /// 扫描插件目录并加载所有合法插件。
    /// 优先从结构化安装目录加载，兼容旧布局（根目录平铺 DLL）。
    /// </summary>
    public static void LoadAll()
    {
        var enabledOrder = PluginEnablementService.GetEnabledPluginOrder();
        var enabledOrderComparer = Comparer<string>.Create((left, right) =>
            PluginEnablementService.CompareByEnabledOrder(left, right, enabledOrder));

        // 从结构化安装目录加载
        var installedDir = Paths.PluginInstalled;
        if (!string.IsNullOrEmpty(installedDir) && Directory.Exists(installedDir))
        {
            foreach (var (manifest, pluginDir) in EnumerateInstalledPluginPackages(installedDir)
                         .OrderBy(item => item.Manifest.Id, enabledOrderComparer))
            {
                if (!PluginEnablementService.IsEnabled(manifest.Id)) continue;
                if (!_IsPackageCompatible(manifest)) continue;

                if (manifest.IsJavaScriptPlugin())
                {
                    _LoadJavaScriptPlugin(manifest, pluginDir);
                    continue;
                }

                if (!manifest.IsDotNetPlugin() || string.IsNullOrWhiteSpace(manifest.EntryAssembly)) continue;
                var assemblyPath = Path.Combine(pluginDir, manifest.EntryAssembly.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(assemblyPath))
                {
                    _context?.Warn($"插件入口程序集不存在: {assemblyPath}");
                    continue;
                }

                var loaded = _TryLoadAssembly(assemblyPath);
                if (loaded is null) continue;
                foreach (var (loadedManifest, entryType) in loaded.Value.Entries
                             .OrderBy(entry => entry.Manifest.Id, enabledOrderComparer))
                {
                    _LoadPlugin(assemblyPath, loaded.Value.Context, loadedManifest, entryType);
                }
            }
        }

        // 兼容旧布局：扫描根目录的平铺 DLL
        var dir = Paths.Plugins;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        string[] assemblies;
        try
        {
            assemblies = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _context?.Warn($"扫描插件目录失败: {dir}", ex);
            return;
        }

        var flatEntries = new List<(string AssemblyPath, CollectiblePluginLoadContext Context, PluginManifest Manifest, Type EntryType)>();
        foreach (var path in assemblies)
        {
            var loaded = _TryLoadAssembly(path);
            if (loaded is null) continue;
            foreach (var (manifest, entryType) in loaded.Value.Entries)
            {
                if (!PluginEnablementService.IsEnabled(manifest.Id)) continue;
                flatEntries.Add((path, loaded.Value.Context, manifest, entryType));
            }
            if (!flatEntries.Any(entry => ReferenceEquals(entry.Context, loaded.Value.Context))) loaded.Value.Context.Unload();
        }

        foreach (var entry in flatEntries.OrderBy(entry => entry.Manifest.Id, enabledOrderComparer))
        {
            _LoadPlugin(entry.AssemblyPath, entry.Context, entry.Manifest, entry.EntryType);
        }
    }

    /// <summary>
    /// 枚举已安装插件目录结构中的入口程序集路径。
    /// 遍历 <c>installed/*/plugin.json</c>，读取 <c>entryAssembly</c> 字段。
    /// </summary>
    public static IEnumerable<(PluginPackageManifest Manifest, string AssemblyPath, string PluginDir)> EnumerateInstalledPluginAssemblies(string installedDir)
    {
        foreach (var (manifest, pluginDir) in EnumerateInstalledPluginPackages(installedDir))
        {
            if (!manifest.IsDotNetPlugin()) continue;
            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) continue;

            var assemblyPath = Path.Combine(pluginDir, manifest.EntryAssembly.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(assemblyPath))
            {
                _context?.Warn($"插件入口程序集不存在: {assemblyPath}");
                continue;
            }

            yield return (manifest, assemblyPath, pluginDir);
        }
    }

    /// <summary>
    /// 枚举已安装插件目录结构中的包清单。
    /// </summary>
    public static IEnumerable<(PluginPackageManifest Manifest, string PluginDir)> EnumerateInstalledPluginPackages(string installedDir)
    {
        if (!Directory.Exists(installedDir)) yield break;

        foreach (var pluginDir in Directory.GetDirectories(installedDir))
        {
            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            PluginPackageManifest? manifest;
            try
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);
            }
            catch (Exception ex)
            {
                _context?.Warn($"读取插件清单失败: {manifestPath}", ex);
                continue;
            }

            if (manifest is null) continue;

            yield return (manifest, pluginDir);
        }
    }

    private static void _UnloadAll()
    {
        List<PluginRecord> snapshot;
        lock (_Lock) { snapshot = _Records.ToList(); _Records.Clear(); }

        foreach (var record in snapshot)
        {
            try
            {
                record.Instance.UnloadAsync().Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                _context?.Warn($"停止插件 {record.Id} 时出错", ex);
            }
        }
    }

    private readonly record struct LoadedAssembly(CollectiblePluginLoadContext Context, List<(PluginManifest Manifest, Type EntryType)> Entries);

    private static LoadedAssembly? _TryLoadAssembly(string path)
    {
        var context = new CollectiblePluginLoadContext(path);
        Assembly asm;
        try
        {
            asm = context.LoadFromAssemblyPath(path);
        }
        catch (Exception ex)
        {
            _context?.Warn($"加载插件程序集失败: {Path.GetFileName(path)}", ex);
            context.Unload();
            return null;
        }

        var entries = new List<(PluginManifest, Type)>();
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _context?.Warn($"读取插件类型失败: {Path.GetFileName(path)}", ex);
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type is null) continue;
            PluginAttribute? attr;
            try { attr = type.GetCustomAttribute<PluginAttribute>(); }
            catch (Exception ex) { _context?.Warn($"读取插件特性失败: {type.FullName}", ex); continue; }
            if (attr is null) continue;
            if (!typeof(IPclPlugin).IsAssignableFrom(type))
            {
                _context?.Warn($"插件入口 {type.FullName} 未实现 IPclPlugin，已跳过");
                continue;
            }
            var manifest = attr.ToManifest();
            if (!_IsRuntimeManifestCompatible(manifest)) continue;
            manifest.EntryPointTypeName = type.AssemblyQualifiedName;
            entries.Add((manifest, type));
        }

        if (entries.Count == 0)
        {
            context.Unload();
            return null;
        }
        return new LoadedAssembly(context, entries);
    }

    private static void _LoadPlugin(string assemblyPath, CollectiblePluginLoadContext context, PluginManifest manifest, Type entryType)
    {
        IPclPlugin instance;
        try
        {
            instance = (IPclPlugin)Activator.CreateInstance(entryType)!;
        }
        catch (Exception ex)
        {
            _context?.Error($"实例化插件入口失败: {manifest.Id}", ex);
            return;
        }

        _LoadPluginInstance(assemblyPath, entryType.Assembly, manifest, instance);
    }

    private static void _LoadJavaScriptPlugin(PluginPackageManifest packageManifest, string pluginDir)
    {
        var manifest = _ToRuntimeManifest(packageManifest);
        if (!_IsRuntimeManifestCompatible(manifest, "JavaScript 插件")) return;

        var entryPath = Path.Combine(pluginDir, packageManifest.EntryScript.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(entryPath))
        {
            _context?.Warn($"JavaScript 插件入口脚本不存在: {entryPath}");
            return;
        }

        var instance = new JavaScriptPlugin(packageManifest, pluginDir);
        _LoadPluginInstance(entryPath, typeof(JavaScriptPlugin).Assembly, manifest, instance);
    }

    private static void _LoadPluginInstance(string entryPath, Assembly assembly, PluginManifest manifest, IPclPlugin instance)
    {
        lock (_Lock)
        {
            if (_Records.Any(r => r.Id == manifest.Id))
            {
                _context?.Warn($"插件 {manifest.Id} 已加载，跳过重复条目");
                return;
            }
        }

        var dataDir = Path.Combine(Paths.Data, "PluginData", _SafeFolderName(manifest.Id));
        try { Directory.CreateDirectory(dataDir); }
        catch (Exception ex) { _context?.Warn($"创建插件数据目录失败: {dataDir}", ex); }

        var record = new PluginRecord
        {
            Manifest = manifest,
            Instance = instance,
            Assembly = assembly,
            AssemblyPath = entryPath,
            DataDirectory = dataDir,
            State = PluginState.Created
        };

        var host = new PluginHostImpl(record);
        var ctx = new PluginContextImpl(record, dataDir, host);

        try
        {
            _context?.Info($"正在加载插件: {manifest.Id}");
            var loadTask = instance.LoadAsync(ctx, CancellationToken.None);
            if (!loadTask.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException($"插件 {manifest.Id} 加载超过 30 秒，已中止等待。");
            record.State = PluginState.Running;
            _context?.Info($"插件已加载: {manifest.Id} v{manifest.Version}");
        }
        catch (Exception ex)
        {
            record.LastException = ex;
            record.State = PluginState.Disabled;
            _context?.Error($"插件加载失败: {manifest.Id}", ex);
        }

        lock (_Lock) { _Records.Add(record); }
    }

    private static PluginManifest _ToRuntimeManifest(PluginPackageManifest packageManifest)
    {
        return new PluginManifest
        {
            Id = packageManifest.Id,
            Name = packageManifest.Name,
            Version = packageManifest.Version,
            Author = packageManifest.Author,
            Description = packageManifest.Description ?? string.Empty,
            HomePageUrl = packageManifest.HomepageUrl,
            MinApiVersion = packageManifest.MinApiVersion,
            MaxApiVersion = packageManifest.MaxApiVersion,
            MinHostVersion = packageManifest.MinHostVersion,
            MaxHostVersion = packageManifest.MaxHostVersion,
            EntryPointTypeName = packageManifest.EntryScript,
            Capabilities = _CombineCapabilities(packageManifest.Capabilities),
            LoadTiming = PluginLoadTiming.WindowCreated
        };
    }

    private static bool _IsPackageCompatible(PluginPackageManifest manifest)
    {
        var result = PluginPackageService.ValidateRuntimeCompatibility(manifest);
        if (result.IsValid) return true;

        _context?.Warn($"插件 {manifest.Id} 运行兼容性检查失败：{result.ErrorMessage}，已跳过");
        return false;
    }

    private static bool _IsRuntimeManifestCompatible(PluginManifest manifest, string label = "插件")
    {
        if (PluginCompatibility.TryGetApiCompatibilityError(manifest.MinApiVersion, manifest.MaxApiVersion, out var apiError))
        {
            _context?.Warn($"{label} {manifest.Id} {apiError} 已跳过");
            return false;
        }

        if (PluginCompatibility.TryGetHostCompatibilityError(manifest.MinHostVersion, manifest.MaxHostVersion, PluginCompatibility.CurrentHostVersion, out var hostError))
        {
            _context?.Warn($"{label} {manifest.Id} {hostError} 已跳过");
            return false;
        }

        return true;
    }

    private static PluginCapabilities _CombineCapabilities(IEnumerable<PluginCapabilities>? capabilities)
    {
        var result = PluginCapabilities.None;
        if (capabilities is null) return result;
        foreach (var capability in capabilities) result |= capability;
        return result;
    }

    private static string _SafeFolderName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(id.Length);
        foreach (var c in id) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}

/// <summary>
/// 可收集的插件程序集加载上下文。<br/>
/// 卸载时调用 <see cref="Unload"/> 可释放程序集占用的内存与文件锁。
/// </summary>
internal sealed class CollectiblePluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 将共享契约与 BCL 委托给默认上下文，使插件与宿主持有同一份类型。
        if (assemblyName.Name == "PCL.Plugin.Abstractions") return null;
        if (assemblyName.Name?.StartsWith("PCL.Core", StringComparison.Ordinal) == true) return null;
        if (assemblyName.Name?.StartsWith("System.", StringComparison.Ordinal) == true) return null;
        if (assemblyName.Name?.StartsWith("Microsoft.", StringComparison.Ordinal) == true) return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
