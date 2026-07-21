using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Mixin;

namespace PCL.Core.App.Plugins;

/// <summary>
/// PCLX Mixin 引擎入口。Core 只发现包、加载程序集、读取 Mixin 配置并应用注入，
/// 不反射调用通用插件生命周期，也不向插件提供 Host API 或服务注入。
/// </summary>
[LifecycleService(LifecycleState.Loaded, Priority = int.MaxValue)]
public sealed class PluginLoaderService : GeneralService
{
    private static readonly List<PluginRecord> Records = [];
    private static readonly object SyncRoot = new();
    private static LifecycleContext? _context;

    internal static MixinRuntime Runtime { get; } = new("pclnex.core.mixin");

    /// <summary>安全模式下跳过所有第三方 PCLX Mixin。</summary>
    public static bool SafeMode { get; set; } =
        string.Equals(Environment.GetEnvironmentVariable("PCL_NEX_SAFE_MODE"), "1", StringComparison.Ordinal);

    public PluginLoaderService() : base("plugin-loader", Lang.Text("Plugins.Loader.ServiceName"), asyncStart: false)
    {
        _context = ServiceContext;
    }

    public static IReadOnlyList<PluginRecord> LoadedPlugins
    {
        get { lock (SyncRoot) return Records.ToArray(); }
    }

    public override void Start() => LoadAll();

    // 不提供运行时热卸载。进程退出时由操作系统回收 Patch 和插件程序集。
    public override void Stop() { }

    public static void LoadAll() => LoadAllAsync().GetAwaiter().GetResult();

    public static async Task LoadAllAsync(CancellationToken cancellationToken = default)
        => await LoadAllFromDirectoryAsync(Paths.PluginInstalled, warnAboutFlatPlugins: true, cancellationToken)
            .ConfigureAwait(false);

    internal static async Task LoadAllFromDirectoryAsync(
        string installedDirectory,
        bool warnAboutFlatPlugins = false,
        CancellationToken cancellationToken = default,
        Func<string, bool>? isEnabled = null,
        IReadOnlyList<string>? enabledOrder = null,
        bool disableFailedPlugins = true)
    {
        isEnabled ??= PluginEnablementService.IsEnabled;
        enabledOrder ??= PluginEnablementService.GetEnabledPluginOrder();
        var installedPackages = EnumerateInstalledPluginPackages(installedDirectory)
            .Select(item => new PluginPackageLocation(item.Manifest, item.PluginDir))
            .ToArray();
        var loadPlan = PluginDependencyService.CreateLoadPlan(
            installedPackages,
            isEnabled,
            enabledOrder);
        foreach (var (pluginId, error) in loadPlan.Errors)
            _context?.Warn($"插件 {pluginId} 前置依赖检查失败，已跳过：{error}", actionLevel: ActionLevel.NormalLog);

        var loadResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in loadPlan.Packages)
        {
            var manifest = package.Manifest;
            var pluginDirectory = package.PluginDirectory;
            cancellationToken.ThrowIfCancellationRequested();
            var failedDependency = (manifest.Dependencies ?? []).FirstOrDefault(dependency =>
                !ContainsRecord(dependency.Id)
                && (!loadResults.TryGetValue(dependency.Id, out var loaded) || !loaded));
            if (failedDependency is not null)
            {
                loadResults[manifest.Id] = false;
                _context?.Warn(
                    $"插件 {manifest.Id} 的前置插件 {failedDependency.Id} 未成功加载，已跳过",
                    actionLevel: ActionLevel.NormalLog);
                continue;
            }
            if (ShouldSkipThirdPartyMixins(manifest))
            {
                loadResults[manifest.Id] = false;
                _context?.Warn($"安全模式已跳过第三方 Mixin：{manifest.Id}", actionLevel: ActionLevel.NormalLog);
                continue;
            }
            if (!await IsPackageCompatibleAsync(manifest, cancellationToken).ConfigureAwait(false))
            {
                loadResults[manifest.Id] = false;
                continue;
            }
            loadResults[manifest.Id] = await LoadPackageAsync(
                    manifest, pluginDirectory, cancellationToken, disableFailedPlugins)
                .ConfigureAwait(false);
        }

        if (warnAboutFlatPlugins) WarnAboutUnsupportedFlatPlugins();
    }

    internal static void RollbackLoadedPluginForTesting(string pluginId)
    {
        PluginRecord? record;
        lock (SyncRoot)
        {
            record = Records.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (record is not null) Records.Remove(record);
        }
        if (record?.Assembly is not null) Runtime.RollbackAssembly(record.Assembly);
        record?.LoadContext?.Unload();
    }

    public static IEnumerable<(PluginPackageManifest Manifest, string AssemblyPath, string PluginDir)>
        EnumerateInstalledPluginAssemblies(string installedDir)
    {
        foreach (var (manifest, pluginDir) in EnumerateInstalledPluginPackages(installedDir))
        {
            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) continue;
            var assemblyPath = ResolvePackagePath(pluginDir, manifest.EntryAssembly);
            if (File.Exists(assemblyPath)) yield return (manifest, assemblyPath, pluginDir);
        }
    }

    public static IEnumerable<(PluginPackageManifest Manifest, string PluginDir)>
        EnumerateInstalledPluginPackages(string installedDir)
    {
        if (string.IsNullOrWhiteSpace(installedDir) || !Directory.Exists(installedDir)) yield break;

        foreach (var pluginDir in Directory.GetDirectories(installedDir))
        {
            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            PluginPackageManifest? manifest;
            try
            {
                var json = File.ReadAllText(manifestPath);
                if (TryGetLegacyPluginReason(json, out var reason))
                {
                    _context?.Warn($"插件包不兼容，已跳过：{manifestPath}：{reason}", actionLevel: ActionLevel.NormalLog);
                    continue;
                }
                manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                _context?.Warn($"读取插件清单失败：{manifestPath}", exception);
                continue;
            }

            if (manifest is not null) yield return (manifest, pluginDir);
        }
    }

    private static Task<bool> LoadPackageAsync(
        PluginPackageManifest manifest,
        string pluginDirectory,
        CancellationToken cancellationToken,
        bool disableFailedPlugins)
    {
        if (ContainsRecord(manifest.Id))
        {
            _context?.Warn($"插件 {manifest.Id} 已应用，跳过重复条目");
            return Task.FromResult(true);
        }

        CollectiblePluginLoadContext? loadContext = null;
        Assembly? assembly = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assemblyPath = ResolvePackagePath(pluginDirectory, manifest.EntryAssembly);
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException(Text("Plugins.Loader.Error.EntryAssemblyNotFound", "插件主程序集不存在。"), assemblyPath);

            loadContext = new CollectiblePluginLoadContext(
                assemblyPath,
                GetSharedDependencyAssemblies(manifest));
            assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var configPaths = manifest.GetMixinConfigurationPaths();
            if (configPaths.Count == 0)
                throw new MixinApplyException(Text("Plugins.Loader.Error.MissingMixinConfig", "插件未声明 mixinConfig 或 mixinConfigs；旧 LoadAsync 插件不再受支持。"));

            var warnings = new List<string>();
            var appliedConfigs = new List<string>();
            foreach (var relativeConfigPath in configPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var configPath = ResolvePackagePath(pluginDirectory, relativeConfigPath);
                MixinConfiguration configuration;
                try
                {
                    configuration = ReadMixinConfiguration(configPath);
                    var result = Runtime.ApplyConfiguration(assembly, configuration, relativeConfigPath);
                    appliedConfigs.Add(relativeConfigPath);
                    warnings.AddRange(result.Warnings);
                    _context?.Info(
                        $"Mixin 配置已应用：{manifest.Id}/{relativeConfigPath}，" +
                        $"Mixin={result.MixinCount}，目标方法={result.TargetMethodCount}");
                }
                catch (Exception exception) when (TryReadOptionalConfiguration(configPath))
                {
                    warnings.Add($"可选 Mixin 配置失败：{relativeConfigPath}：{exception.Message}");
                    _context?.Warn(
                        $"可选 Mixin 配置失败，继续加载其他配置：{manifest.Id}/{relativeConfigPath}",
                        exception,
                        ActionLevel.NormalLog);
                }
            }

            var record = new PluginRecord
            {
                Manifest = manifest,
                Assembly = assembly,
                LoadContext = loadContext,
                AssemblyPath = assemblyPath,
                PluginDirectory = pluginDirectory,
                AppliedMixinConfigurations = appliedConfigs,
                Warnings = warnings,
                State = PluginState.Running
            };
            lock (SyncRoot) Records.Add(record);

            foreach (var warning in warnings)
                _context?.Warn($"Mixin 诊断：{manifest.Id}：{warning}", actionLevel: ActionLevel.NormalLog);
            foreach (var patch in Runtime.Patches.Where(patch => patch.SourceAssembly == assembly))
            {
                _context?.Info(
                    $"Mixin 已应用：插件={manifest.Id}；类={patch.MixinType.FullName}；" +
                    $"目标={patch.TargetMethod.DeclaringType?.FullName}.{patch.TargetMethod.Name}；" +
                    $"操作={patch.Kind}；注入点={patch.InjectionPoint}；" +
                    $"定位={patch.TargetDescriptor ?? "<method>"}；处理器={patch.Handler.Name}；优先级={patch.Priority}");
            }
            foreach (var conflict in Runtime.Conflicts.Where(conflict =>
                         conflict.ApplicationOrder.Any(patch => patch.SourceAssembly == assembly)))
            {
                _context?.Warn(
                    $"Mixin 冲突：{conflict.TargetMethod.DeclaringType?.FullName}.{conflict.TargetMethod.Name}；" +
                    "应用顺序=" + string.Join(" -> ", conflict.ApplicationOrder.Select(patch =>
                        $"{patch.MixinType.FullName}.{patch.Handler.Name}" +
                        $"[{patch.Kind}@{patch.InjectionPoint}, target={patch.TargetDescriptor ?? "<method>"}, {patch.Priority}]")),
                    actionLevel: ActionLevel.NormalLog);
            }
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            if (assembly is not null) Runtime.RollbackAssembly(assembly);
            loadContext?.Unload();
            if (disableFailedPlugins)
            {
                try { PluginEnablementService.MarkSelfProtectionDisabled(manifest.Id); }
                catch (Exception disableException)
                {
                    _context?.Warn($"写入插件自我保护禁用标记失败：{manifest.Id}", disableException);
                }
                try { PluginEnablementService.SetEnabled(manifest.Id, false); }
                catch (Exception disableException)
                {
                    _context?.Warn($"保存插件禁用状态失败：{manifest.Id}", disableException);
                }
            }
            _context?.Error(
                $"required Mixin 加载失败，插件已禁用：{manifest.Id}；" +
                $"程序集={manifest.EntryAssembly}；原因={exception.Message}",
                exception,
                ActionLevel.NormalLog);
            return Task.FromResult(false);
        }
    }

    private static MixinConfiguration ReadMixinConfiguration(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException(Text("Plugins.Loader.Error.MixinConfigNotFound", "Mixin 配置文件不存在。"), configPath);
        try
        {
            return JsonSerializer.Deserialize<MixinConfiguration>(
                       File.ReadAllText(configPath),
                       MixinJsonOptions)
                   ?? throw new JsonException(Text("Plugins.Loader.Error.MixinConfigRootEmpty", "Mixin 配置根对象为空。"));
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new MixinApplyException(Text("Plugins.Loader.Error.MixinConfigReadFailed", "读取 Mixin 配置失败：{0}", configPath), exception);
        }
    }

    private static bool TryReadOptionalConfiguration(string configPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath), JsonDocumentOptions);
            return document.RootElement.TryGetProperty("required", out var required) &&
                   required.ValueKind == JsonValueKind.False;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetLegacyPluginReason(string json, out string reason)
    {
        using var document = JsonDocument.Parse(json, JsonDocumentOptions);
        var root = document.RootElement;
        if (root.TryGetProperty("runtime", out var runtime) &&
            runtime.ValueKind == JsonValueKind.String &&
            runtime.GetString()?.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true)
        {
            reason = Text("Plugins.Loader.Error.JavaScriptRuntimeRemoved", "JavaScript/Jint 插件运行时已移除");
            return true;
        }

        foreach (var propertyName in new[] { "entryType", "loadMethod", "unloadMethod", "entryScript" })
        {
            if (!root.TryGetProperty(propertyName, out _)) continue;
            reason = Text("Plugins.Loader.Error.LegacyPropertyRemoved", "旧插件入口字段 {0} 已移除；插件必须声明 Mixin 配置", propertyName);
            return true;
        }

        reason = string.Empty;
        return false;
    }

    internal static string ResolvePackagePath(string pluginDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var root = Path.GetFullPath(pluginDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(
            pluginDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Text("Plugins.Loader.Error.FileOutsidePluginDir", "插件文件不能位于插件目录之外：{0}", relativePath));
        return fullPath;
    }

    private static bool ContainsRecord(string pluginId)
    {
        lock (SyncRoot)
            return Records.Any(record => string.Equals(record.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Assembly> GetSharedDependencyAssemblies(PluginPackageManifest manifest)
    {
        lock (SyncRoot)
        {
            var records = Records
                .Where(record => record.State == PluginState.Running && record.Assembly is not null)
                .ToDictionary(record => record.Id, StringComparer.OrdinalIgnoreCase);
            var result = new List<Assembly>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddDependencies(PluginPackageManifest current)
            {
                foreach (var dependency in current.Dependencies ?? [])
                {
                    if (!visited.Add(dependency.Id) || !records.TryGetValue(dependency.Id, out var record)) continue;
                    result.Add(record.Assembly!);
                    AddDependencies(record.Manifest);
                }
            }

            AddDependencies(manifest);
            return result;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool ShouldSkipThirdPartyMixins(PluginPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return SafeMode;
    }

    internal static async Task<bool> IsPackageCompatibleAsync(
        PluginPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var result = PluginPackageService.ValidatePackageManifest(manifest);
        if (!result.IsValid)
        {
            _context?.Warn(
                $"插件 {manifest.Id} Core 兼容性检查失败：{result.ErrorMessage}，已跳过",
                actionLevel: ActionLevel.NormalLog);
            return false;
        }

        if (result.CompatibilityStatus == PluginCoreCompatibilityStatus.Compatible) return true;

        var confirmed = await PluginCompatibility.ConfirmIfRequiredAsync(
            manifest,
            PluginCompatibilityAction.Enable,
            cancellationToken).ConfigureAwait(false);
        if (confirmed) return true;

        _context?.Warn(
            $"插件 {manifest.Id} Core 兼容性需要用户确认但未获允许：{result.ErrorMessage}，已跳过",
            actionLevel: ActionLevel.NormalLog);
        return false;
    }

    private static void WarnAboutUnsupportedFlatPlugins()
    {
        if (string.IsNullOrWhiteSpace(Paths.Plugins) || !Directory.Exists(Paths.Plugins)) return;
        foreach (var assemblyPath in Directory.GetFiles(Paths.Plugins, "*.dll", SearchOption.TopDirectoryOnly))
            _context?.Warn(
                Text("Plugins.Loader.Error.LegacyDllNotSupported", "旧平铺 DLL 插件不再受支持，已跳过：{0}。请改用包含 Mixin 配置的 PCLX 包。", Path.GetFileName(assemblyPath)),
                actionLevel: ActionLevel.NormalLog);
    }

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions MixinJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string Text(string key, string fallback, params object?[] args)
    {
        var template = Lang.Text(key);
        if (string.Equals(template, key, StringComparison.Ordinal)
            || string.Equals(template, $"!{key}!", StringComparison.Ordinal))
            template = fallback;
        return string.Format(Lang.Culture, template, args);
    }
}

/// <summary>插件程序集隔离上下文；PCL.Core/PCL.Mixin 类型始终由默认上下文共享。</summary>
internal sealed class CollectiblePluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyDictionary<string, Assembly> _sharedDependencyAssemblies;

    public CollectiblePluginLoadContext(
        string mainAssemblyPath,
        IEnumerable<Assembly>? sharedDependencyAssemblies = null) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        _sharedDependencyAssemblies = (sharedDependencyAssemblies ?? [])
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.GetName().Name))
            .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            !assembly.IsDynamic &&
            string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (shared is not null) return shared;

        if (assemblyName.Name is not null
            && _sharedDependencyAssemblies.TryGetValue(assemblyName.Name, out var dependencyAssembly))
            return dependencyAssembly;

        if (assemblyName.Name?.StartsWith("System.", StringComparison.Ordinal) == true ||
            assemblyName.Name?.StartsWith("Microsoft.", StringComparison.Ordinal) == true)
            return null;

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
