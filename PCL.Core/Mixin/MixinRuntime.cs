using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PCL.Mixin;

internal sealed class MixinRuntime : IMixinRuntime, IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<MethodBase, TargetPlan> _plans = [];
    private readonly Dictionary<MethodInfo, ShadowMethodRegistration> _shadowMethods = [];
    private readonly HashSet<ApplicationKey> _appliedApplications = [];
    private readonly Harmony _harmony;
    private long _sequence;
    private bool _disposed;

    public MixinRuntime(string? ownerId = null)
    {
        OwnerId = string.IsNullOrWhiteSpace(ownerId)
            ? $"pclnex.mixin.{Guid.NewGuid():N}"
            : ownerId;
        _harmony = new Harmony(OwnerId);
    }

    public string OwnerId { get; }

    public IReadOnlyList<MixinPatchInfo> Patches
    {
        get
        {
            lock (_lock)
            {
                var patches = _plans.Values
                    .SelectMany(plan => plan.AllHandlers().Select(handler => new MixinPatchInfo(
                        handler.SourceAssembly,
                        handler.MixinType,
                        plan.Target,
                        handler.Handler,
                        handler.Kind,
                        handler.InjectionPoint,
                        handler.TargetDescriptor,
                        handler.Priority)))
                    .ToList();
                return new ReadOnlyCollection<MixinPatchInfo>(patches);
            }
        }
    }

    public IReadOnlyList<MixinConflictInfo> Conflicts
    {
        get
        {
            lock (_lock)
            {
                return _plans.Values
                    .Select(plan => new MixinConflictInfo(
                        plan.Target,
                        plan.AllHandlers()
                            .Select(handler => new MixinPatchInfo(
                                handler.SourceAssembly,
                                handler.MixinType,
                                plan.Target,
                                handler.Handler,
                                handler.Kind,
                                handler.InjectionPoint,
                                handler.TargetDescriptor,
                                handler.Priority))
                            .ToArray()))
                    .Where(conflict => conflict.ApplicationOrder
                        .Select(patch => patch.MixinType)
                        .Distinct()
                        .Skip(1)
                        .Any())
                    .ToArray();
            }
        }
    }

    public MixinApplyResult ApplyAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return ApplyCore(
            assembly,
            "assembly-scan",
            GetLoadableTypes(assembly),
            defaultPriority: 1000,
            defaultRequire: 1,
            processor: null,
            configurationName: assembly.GetName().Name ?? "assembly-scan");
    }

    public MixinApplyResult ApplyConfiguration(
        Assembly assembly,
        MixinConfiguration configuration,
        string configurationName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (configuration.Injectors.DefaultRequire < 0)
            throw new MixinApplyException($"Mixin 配置 {configurationName} 的 injectors.defaultRequire 不能小于 0。");

        var types = new List<Type>();
        foreach (var declaredName in configuration.Mixins)
        {
            if (string.IsNullOrWhiteSpace(declaredName)) continue;
            var typeName = declaredName.Contains('.', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(configuration.Package)
                ? declaredName.Trim()
                : configuration.Package.TrimEnd('.') + "." + declaredName.Trim();
            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is null)
                throw new MixinApplyException($"Mixin 配置 {configurationName} 声明的类型不存在：{typeName}");
            if (!type.IsDefined(typeof(MixinAttribute), false))
                throw new MixinApplyException($"Mixin 配置 {configurationName} 声明的类型缺少 [Mixin]：{typeName}");
            types.Add(type);
        }

        if (types.Count == 0)
            throw new MixinApplyException($"Mixin 配置 {configurationName} 没有声明任何 Mixin 类。");

        IMixinConfigPlugin? processor = null;
        if (!string.IsNullOrWhiteSpace(configuration.Plugin))
        {
            var processorType = assembly.GetType(configuration.Plugin.Trim(), throwOnError: false, ignoreCase: false)
                ?? throw new MixinApplyException($"Mixin 配置处理器不存在：{configuration.Plugin}");
            if (!typeof(IMixinConfigPlugin).IsAssignableFrom(processorType))
                throw new MixinApplyException($"Mixin 配置处理器未实现 IMixinConfigPlugin：{configuration.Plugin}");
            processor = Activator.CreateInstance(processorType, nonPublic: true) as IMixinConfigPlugin
                ?? throw new MixinApplyException($"无法创建 Mixin 配置处理器：{configuration.Plugin}");
        }

        return ApplyCore(
            assembly,
            configurationName,
            types,
            configuration.Priority,
            configuration.Injectors.DefaultRequire,
            processor,
            configurationName);
    }

    internal void RollbackAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (_lock)
        {
            _appliedApplications.RemoveWhere(application => application.Assembly == assembly);
            var affected = new HashSet<MethodBase>();
            RemoveAssemblyHandlers(assembly, affected);
            RemoveShadowMethods((_, registration) => registration.SourceAssembly == assembly);
            foreach (var target in affected) RefreshPatch(target);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var target in _plans.Keys.ToArray())
            {
                _harmony.Unpatch(target, HarmonyPatchType.All, OwnerId);
                MixinRuntimeDispatch.Unregister(target);
            }
            foreach (var shadow in _shadowMethods.Keys.ToArray())
            {
                _harmony.Unpatch(shadow, HarmonyPatchType.All, OwnerId);
                MixinShadowDispatch.Unregister(shadow);
            }
            _shadowMethods.Clear();
            _plans.Clear();
            _appliedApplications.Clear();
        }
    }

    private MixinApplyResult ApplyCore(
        Assembly assembly,
        string applicationId,
        IEnumerable<Type> mixinTypes,
        int defaultPriority,
        int defaultRequire,
        IMixinConfigPlugin? processor,
        string configurationName)
    {
        lock (_lock)
        {
            var key = new ApplicationKey(assembly, applicationId);
            if (_appliedApplications.Contains(key))
                return BuildResult(assembly, [], applicationId: applicationId);

            var warnings = new List<string>();
            var affected = new HashSet<MethodBase>();
            var mixinCount = 0;
            try
            {
                foreach (var mixinType in mixinTypes.Distinct())
                {
                    var mixins = mixinType.GetCustomAttributes<MixinAttribute>(false).ToArray();
                    foreach (var mixin in mixins)
                    {
                        var mixinAffected = new HashSet<MethodBase>();
                        var sequenceBefore = _sequence;
                        var counted = false;
                        try
                        {
                            var targetType = MixinTargetResolver.ResolveType(mixin);
                            if (targetType is null)
                                throw new MixinApplyException(
                                    $"Mixin 目标类型不存在：{mixin.TargetName ?? mixin.Target?.FullName ?? "<null>"}");
                            if (processor?.ShouldApplyMixin(targetType.FullName ?? targetType.Name, mixinType.FullName ?? mixinType.Name) == false)
                            {
                                warnings.Add($"Mixin 配置处理器跳过：{mixinType.FullName} -> {targetType.FullName}");
                                continue;
                            }

                            var context = new MixinApplyContext(configurationName, assembly, mixinType, targetType);
                            processor?.PreApply(context);
                            mixinCount++;
                            counted = true;
                            var shadows = ValidateShadows(mixinType, targetType, warnings);
                            RegisterMixin(
                                applicationId,
                                assembly,
                                mixinType,
                                targetType,
                            mixin,
                            mixinAffected,
                            defaultPriority,
                            defaultRequire,
                            warnings,
                            shadows.Fields);
                            processor?.PostApply(context);
                            RegisterShadowMethods(shadows.Methods, applicationId, assembly, mixinType);
                            // Harmony evaluates transpilers while Patch is applied. Refresh each
                            // mixin inside its own try/catch so an optional INVOKE/FIELD/etc.
                            // matcher or signature failure can be rolled back without aborting the
                            // rest of the configuration.
                            foreach (var target in mixinAffected) RefreshPatch(target);
                            affected.UnionWith(mixinAffected);
                        }
                        catch (Exception exception) when (mixin.Optional)
                        {
                            if (counted) mixinCount--;
                            RemoveHandlersAfterSequence(sequenceBefore, assembly, mixinAffected);
                            RemoveShadowMethods((_, registration) =>
                                registration.ApplicationId == applicationId && registration.MixinType == mixinType);
                            foreach (var target in mixinAffected) RefreshPatch(target);
                            warnings.Add(
                                $"可选 Mixin 失败：{mixinType.FullName} -> " +
                                $"{mixin.TargetName ?? mixin.Target?.FullName ?? "<null>"}：{DescribeFailure(exception)}");
                        }
                    }
                }

                _appliedApplications.Add(key);
                AddConflictWarnings(affected, warnings);
            }
            catch
            {
                _appliedApplications.Remove(key);
                RemoveApplicationHandlers(applicationId, assembly, affected);
                RemoveShadowMethods((_, registration) =>
                    registration.SourceAssembly == assembly && registration.ApplicationId == applicationId);
                foreach (var target in affected) RefreshPatch(target);
                throw;
            }

            return BuildResult(assembly, warnings, mixinCount, applicationId);
        }
    }

    private sealed record ApplicationKey(Assembly Assembly, string Id);

    private static string DescribeFailure(Exception exception)
    {
        var root = exception.GetBaseException();
        return string.IsNullOrWhiteSpace(root.Message) ? exception.Message : root.Message;
    }

    private void RegisterMixin(
        string applicationId,
        Assembly sourceAssembly,
        Type mixinType,
        Type targetType,
        MixinAttribute mixin,
        HashSet<MethodBase> affected,
        int defaultPriority,
        int defaultRequire,
        List<string> warnings,
        IReadOnlyList<ShadowFieldBinding> shadowFields)
    {
        MixinInstanceScope? mixinInstances = null;
        var methods = mixinType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                           BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var handler in methods)
        {
            var operations = handler.GetCustomAttributes<MixinOperationAttribute>(false).ToArray();
            if (operations.Length == 0) continue;
            if (!handler.IsStatic) mixinInstances ??= new MixinInstanceScope(mixinType);

            foreach (var operation in operations)
            {
                ApplyOperationMarkers(handler, operation, defaultRequire);
                var target = MixinTargetResolver.ResolveMethod(targetType, operation, handler);
                if (target.ContainsGenericParameters)
                    throw new MixinApplyException($"暂不支持对开放泛型方法应用 Mixin：{targetType.FullName}.{target.Name}");
                if (MixinInvocation.GetReturnType(target).IsByRef)
                    throw new MixinApplyException($"暂不支持对 ref-return 方法应用边界 Mixin：{targetType.FullName}.{target.Name}");
                if (operation is InjectAttribute inject &&
                    !ValidateLocalCapture(target, handler, inject, warnings))
                    continue;
                if (operation is OverwriteAttribute && handler.IsDefined(typeof(IntrinsicAttribute), false))
                {
                    warnings.Add(
                        $"Intrinsic 保留目标实现：{targetType.FullName}.{target.Name}；" +
                        $"已跳过 {mixinType.FullName}.{handler.Name}。");
                    continue;
                }

                if (!_plans.TryGetValue(target, out var plan))
                {
                    plan = new TargetPlan(target);
                    _plans.Add(target, plan);
                }

                var typePriority = mixinType.GetCustomAttribute<PriorityAttribute>(false)?.Value
                    ?? (mixin.Priority == 1000 ? defaultPriority : mixin.Priority);
                var priority = handler.GetCustomAttribute<PriorityAttribute>(false)?.Value
                    ?? (operation.Priority == 1000 ? typePriority : operation.Priority);
                var handlerPlan = new HandlerPlan(
                    applicationId,
                    sourceAssembly,
                    mixinType,
                    targetType,
                    handler,
                    operation,
                    priority,
                    ++_sequence,
                    handler.IsStatic ? null : mixinInstances,
                    shadowFields);
                AddHandler(plan, handlerPlan);
                plan.Sort();
                affected.Add(target);
            }
        }
    }

    internal static bool ValidateLocalCapture(
        MethodBase target,
        MethodInfo handler,
        InjectAttribute inject,
        List<string> warnings)
    {
        var localParameters = handler.GetParameters()
            .Select(parameter => (Parameter: parameter, Local: parameter.GetCustomAttribute<LocalAttribute>(false)))
            .Where(item => item.Local is not null)
            .ToArray();
        if (localParameters.Length == 0) return true;

        if (inject.Locals == LocalCapture.NoCapture)
            throw new MixinApplyException(
                $"Inject {handler.DeclaringType?.FullName}.{handler.Name} 使用了 [Local]，" +
                "但 Locals=NoCapture。请显式选择 FailSoft 或 FailHard。");

        string? failure = null;
        var localVariables = target.GetMethodBody()?.LocalVariables;
        if (inject.At == MixinAt.Head)
        {
            failure = "HEAD 注入点尚未建立可捕获的局部变量";
        }
        else if (localVariables is null)
        {
            failure = "目标方法没有可读取的方法体或局部变量表";
        }
        else
        {
            foreach (var (parameter, local) in localParameters)
            {
                var index = local!.Index;
                if (index < 0 || index >= localVariables.Count)
                {
                    failure = $"请求的局部变量索引 {index} 不存在（局部变量数量={localVariables.Count}）";
                    break;
                }

                var parameterType = parameter.ParameterType.IsByRef
                    ? parameter.ParameterType.GetElementType()!
                    : parameter.ParameterType;
                var localType = localVariables[index].LocalType;
                if (parameterType != localType)
                {
                    failure = $"局部变量 {index} 类型不匹配：目标={localType.FullName}，处理器={parameterType.FullName}";
                    break;
                }
            }
        }

        if (failure is null) return true;
        var message =
            $"Local Capture 失败：{target.DeclaringType?.FullName}.{target.Name} -> " +
            $"{handler.DeclaringType?.FullName}.{handler.Name}：{failure}";
        if (inject.Locals == LocalCapture.FailSoft)
        {
            warnings.Add(message + "；已跳过该注入。");
            return false;
        }

        throw new MixinApplyException(message);
    }

    private static void ApplyOperationMarkers(MethodInfo handler, MixinOperationAttribute operation, int defaultRequire)
    {
        operation.Require = handler.GetCustomAttribute<RequireAttribute>(false)?.Value
            ?? (operation.Require < 0 ? defaultRequire : operation.Require);
        operation.Expect = handler.GetCustomAttribute<ExpectAttribute>(false)?.Value ?? operation.Expect;
        operation.Allow = handler.GetCustomAttribute<AllowAttribute>(false)?.Value ?? operation.Allow;

        var ordinal = handler.GetCustomAttribute<OrdinalAttribute>(false)?.Value;
        var cancellable = handler.IsDefined(typeof(CancellableAttribute), false);
        switch (operation)
        {
            case InjectAttribute inject:
                if (ordinal.HasValue) inject.Ordinal = ordinal.Value;
                if (cancellable) inject.Cancellable = true;
                ResolveNamedSlice(handler, inject.Slice, out var injectFrom, out var injectTo);
                inject.SliceFrom ??= injectFrom;
                inject.SliceTo ??= injectTo;
                break;
            case RedirectAttribute redirect:
                if (ordinal.HasValue) redirect.Ordinal = ordinal.Value;
                ResolveNamedSlice(handler, redirect.Slice, out var redirectFrom, out var redirectTo);
                redirect.SliceFrom ??= redirectFrom;
                redirect.SliceTo ??= redirectTo;
                break;
            case ModifyArgAttribute modifyArg:
                if (ordinal.HasValue) modifyArg.Ordinal = ordinal.Value;
                ResolveNamedSlice(handler, modifyArg.Slice, out var modifyArgFrom, out var modifyArgTo);
                modifyArg.SliceFrom ??= modifyArgFrom;
                modifyArg.SliceTo ??= modifyArgTo;
                break;
            case ModifyArgsAttribute modifyArgs:
                if (ordinal.HasValue) modifyArgs.Ordinal = ordinal.Value;
                ResolveNamedSlice(handler, modifyArgs.Slice, out var modifyArgsFrom, out var modifyArgsTo);
                modifyArgs.SliceFrom ??= modifyArgsFrom;
                modifyArgs.SliceTo ??= modifyArgsTo;
                break;
            case ModifyVariableAttribute modifyVariable when ordinal.HasValue:
                modifyVariable.Ordinal = ordinal.Value;
                break;
            case ModifyConstantAttribute modifyConstant:
                if (ordinal.HasValue) modifyConstant.Ordinal = ordinal.Value;
                ResolveNamedSlice(handler, modifyConstant.Slice, out var constantFrom, out var constantTo);
                modifyConstant.SliceFrom ??= constantFrom;
                modifyConstant.SliceTo ??= constantTo;
                break;
        }
    }

    private static void ResolveNamedSlice(MethodInfo handler, string? id, out string? from, out string? to)
    {
        from = null;
        to = null;
        if (string.IsNullOrWhiteSpace(id)) return;
        var slice = handler.GetCustomAttributes<SliceAttribute>(false)
            .SingleOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (slice is null)
            throw new MixinApplyException($"处理器 {handler.DeclaringType?.FullName}.{handler.Name} 引用了未声明的 Slice：{id}");
        from = slice.From;
        to = slice.To;
    }

    private static void AddHandler(TargetPlan plan, HandlerPlan handler)
    {
        switch (handler.Operation)
        {
            case InjectAttribute inject when inject.At == MixinAt.Head:
                ValidateBoundaryCount(handler, inject);
                plan.Head.Add(handler);
                break;
            case OverwriteAttribute:
                ValidateBoundaryCount(handler, handler.Operation);
                var expected = MixinInvocation.GetReturnType(plan.Target);
                if (handler.Handler.ReturnType != expected)
                    throw new MixinApplyException($"Overwrite {handler.MixinType.FullName}.{handler.Handler.Name} 必须返回 {expected.FullName}。");
                plan.Overwrites.Add(handler);
                break;
            default:
                if (!handler.Handler.IsStatic)
                    throw new MixinApplyException($"IL 注入处理器必须是 static：{handler.MixinType.FullName}.{handler.Handler.Name}");
                plan.Transpilers.Add(handler);
                break;
        }
    }

    private static void ValidateBoundaryCount(HandlerPlan plan, MixinOperationAttribute operation)
    {
        const int count = 1;
        var context =
            $"目标={plan.TargetType.FullName}.{plan.TargetMethodName()}；" +
            $"处理器={plan.MixinType.FullName}.{plan.Handler.Name}；" +
            $"注入点={plan.InjectionPoint}；定位={plan.TargetDescriptor ?? "<method>"}";
        if (operation.Require > count)
            throw new MixinApplyException(
                $"{plan.Kind} 约束失败：{context}；只匹配 {count} 处，Require={operation.Require}。");
        if (operation.Allow >= 0 && count > operation.Allow)
            throw new MixinApplyException(
                $"{plan.Kind} 约束失败：{context}；匹配 {count} 处，超过 Allow={operation.Allow}。");
        if (operation.Expect >= 0 && count != operation.Expect)
            throw new MixinApplyException(
                $"{plan.Kind} 约束失败：{context}；匹配 {count} 处，Expect={operation.Expect}。");
    }

    private void RefreshPatch(MethodBase target)
    {
        _harmony.Unpatch(target, HarmonyPatchType.All, OwnerId);
        if (!_plans.TryGetValue(target, out var plan) || plan.IsEmpty)
        {
            _plans.Remove(target);
            MixinRuntimeDispatch.Unregister(target);
            return;
        }

        plan.Revision++;
        MixinRuntimeDispatch.Register(target, plan);
        var prefix = plan.Head.Count > 0 || plan.Overwrite is not null
            ? new HarmonyMethod(typeof(MixinPatchWrapperFactory).GetMethod(
                nameof(MixinPatchWrapperFactory.PrefixFactory),
                BindingFlags.Static | BindingFlags.Public)!)
            : null;
        var postfix = plan.Return.Count > 0
            ? new HarmonyMethod(typeof(MixinPatchWrapperFactory).GetMethod(
                nameof(MixinPatchWrapperFactory.PostfixFactory),
                BindingFlags.Static | BindingFlags.Public)!)
            : null;
        var transpiler = plan.Transpilers.Count > 0
            ? new HarmonyMethod(typeof(MixinRuntimeDispatch).GetMethod(
                nameof(MixinRuntimeDispatch.Transpiler),
                BindingFlags.Static | BindingFlags.Public)!)
            : null;

        _harmony.Patch(target, prefix, postfix, transpiler);
    }

    private void RegisterShadowMethods(
        IReadOnlyList<(MethodInfo Shadow, MethodInfo Target)> methods,
        string applicationId,
        Assembly sourceAssembly,
        Type mixinType)
    {
        foreach (var (shadow, target) in methods)
        {
            if (_shadowMethods.TryGetValue(shadow, out var existing))
            {
                if (existing.Target != target)
                    throw new MixinApplyException(
                        $"Shadow 方法重复绑定到不同目标：{shadow.DeclaringType?.FullName}.{shadow.Name}");
                continue;
            }

            MixinShadowDispatch.Register(shadow, target);
            var prefix = new HarmonyMethod(typeof(MixinShadowWrapperFactory).GetMethod(
                nameof(MixinShadowWrapperFactory.PrefixFactory), BindingFlags.Static | BindingFlags.Public)!);
            _harmony.Patch(shadow, prefix: prefix);
            _shadowMethods.Add(shadow, new ShadowMethodRegistration(target, applicationId, sourceAssembly, mixinType));
        }
    }

    private void RemoveShadowMethods(Func<MethodInfo, ShadowMethodRegistration, bool> predicate)
    {
        foreach (var shadow in _shadowMethods
                     .Where(pair => predicate(pair.Key, pair.Value))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _harmony.Unpatch(shadow, HarmonyPatchType.All, OwnerId);
            MixinShadowDispatch.Unregister(shadow);
            _shadowMethods.Remove(shadow);
        }
    }

    private sealed record ShadowMethodRegistration(
        MethodInfo Target,
        string ApplicationId,
        Assembly SourceAssembly,
        Type MixinType);

    private void RemoveAssemblyHandlers(Assembly assembly, HashSet<MethodBase> affected)
    {
        foreach (var pair in _plans.ToArray())
        {
            var plan = pair.Value;
            var before = plan.AllHandlers().Count();
            plan.Head.RemoveAll(handler => handler.SourceAssembly == assembly);
            plan.Return.RemoveAll(handler => handler.SourceAssembly == assembly);
            plan.Transpilers.RemoveAll(handler => handler.SourceAssembly == assembly);
            plan.Overwrites.RemoveAll(handler => handler.SourceAssembly == assembly);
            if (before != plan.AllHandlers().Count()) affected.Add(pair.Key);
        }
    }

    private void RemoveApplicationHandlers(string applicationId, Assembly assembly, HashSet<MethodBase> affected)
    {
        foreach (var pair in _plans.ToArray())
        {
            var plan = pair.Value;
            var before = plan.AllHandlers().Count();
            bool Match(HandlerPlan handler) => handler.SourceAssembly == assembly && handler.ApplicationId == applicationId;
            plan.Head.RemoveAll(handler => Match(handler));
            plan.Return.RemoveAll(handler => Match(handler));
            plan.Transpilers.RemoveAll(handler => Match(handler));
            plan.Overwrites.RemoveAll(handler => Match(handler));
            if (before != plan.AllHandlers().Count()) affected.Add(pair.Key);
        }
    }

    private void RemoveHandlersAfterSequence(long sequence, Assembly assembly, HashSet<MethodBase> affected)
    {
        foreach (var pair in _plans.ToArray())
        {
            var plan = pair.Value;
            var before = plan.AllHandlers().Count();
            bool Match(HandlerPlan handler) => handler.SourceAssembly == assembly && handler.Sequence > sequence;
            plan.Head.RemoveAll(handler => Match(handler));
            plan.Return.RemoveAll(handler => Match(handler));
            plan.Transpilers.RemoveAll(handler => Match(handler));
            plan.Overwrites.RemoveAll(handler => Match(handler));
            if (before != plan.AllHandlers().Count()) affected.Add(pair.Key);
        }
    }

    private void AddConflictWarnings(IEnumerable<MethodBase> affected, List<string> warnings)
    {
        foreach (var target in affected)
        {
            if (!_plans.TryGetValue(target, out var plan)) continue;
            var order = plan.AllHandlers().ToArray();
            if (order.Select(handler => handler.MixinType).Distinct().Count() < 2) continue;
            warnings.Add(
                $"Mixin 冲突：{target.DeclaringType?.FullName}.{target.Name}，应用顺序：" +
                string.Join(" -> ", order.Select(handler =>
                    $"{handler.MixinType.FullName}.{handler.Handler.Name}" +
                    $"[{handler.Kind}@{handler.InjectionPoint}, target={handler.TargetDescriptor ?? "<method>"}, " +
                    $"priority={handler.Priority}]")));
        }
    }

    private MixinApplyResult BuildResult(
        Assembly assembly,
        IReadOnlyList<string> warnings,
        int? mixinCount = null,
        string? applicationId = null)
    {
        var handlers = _plans.Values.SelectMany(plan => plan.AllHandlers())
            .Where(handler => handler.SourceAssembly == assembly &&
                              (applicationId is null || handler.ApplicationId == applicationId))
            .ToArray();
        return new MixinApplyResult(
            assembly,
            mixinCount ?? handlers.Select(handler => handler.MixinType).Distinct().Count(),
            handlers.Select(handler => MixinTargetResolver.ResolveMethod(handler.TargetType, handler.Operation, handler.Handler))
                .Distinct().Count(),
            warnings);
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
    }

    private sealed record ShadowValidation(
        IReadOnlyList<ShadowFieldBinding> Fields,
        IReadOnlyList<(MethodInfo Shadow, MethodInfo Target)> Methods);

    private static ShadowValidation ValidateShadows(Type mixinType, Type targetType, List<string> warnings)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                   BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var fields = new List<ShadowFieldBinding>();
        var methods = new List<(MethodInfo Shadow, MethodInfo Target)>();
        foreach (var member in mixinType.GetMembers(flags))
        {
            var shadow = member.GetCustomAttribute<ShadowAttribute>(false);
            if (member is MethodInfo intrinsic && intrinsic.IsDefined(typeof(IntrinsicAttribute), false) &&
                shadow is null && !intrinsic.IsDefined(typeof(OverwriteAttribute), false))
                throw new MixinApplyException(
                    $"Intrinsic 方法必须同时声明 [Shadow] 或 [Overwrite]：{mixinType.FullName}.{intrinsic.Name}");
            if (shadow is null)
            {
                if (member.IsDefined(typeof(UniqueAttribute), false))
                {
                    var collides = member switch
                    {
                        FieldInfo => MixinTargetResolver.FindField(targetType, member.Name) is not null,
                        PropertyInfo => MixinTargetResolver.FindProperty(targetType, member.Name) is not null,
                        MethodInfo method => MixinTargetResolver.EnumerateMethods(targetType).Any(candidate =>
                            candidate.Name == method.Name && candidate.GetParameters().Length == method.GetParameters().Length),
                        _ => false
                    };
                    if (collides)
                        warnings.Add($"Unique 成员与目标同名但保持 Mixin 私有隔离：{mixinType.FullName}.{member.Name}");
                }
                continue;
            }
            var name = string.IsNullOrWhiteSpace(shadow.Name) ? member.Name : shadow.Name;
            var targetMember = ResolveShadowTarget(member, targetType, name);
            if (targetMember is not null)
            {
                ValidateShadowSignature(member, targetMember, targetType, name);
                if (member.IsDefined(typeof(FinalAttribute), false))
                {
                    var isFinal = targetMember switch
                    {
                        FieldInfo field => field.IsInitOnly || field.IsLiteral,
                        PropertyInfo property => property.SetMethod is null,
                        _ => false
                    };
                    if (!isFinal)
                        throw new MixinApplyException($"Final Shadow 的目标可写：{targetType.FullName}::{name}");
                }
                var mutable = shadow.Mutable || member.IsDefined(typeof(MutableAttribute), false);
                switch (member, targetMember)
                {
                    case (FieldInfo shadowField, FieldInfo targetField):
                        fields.Add(new ShadowFieldBinding(
                            shadowField,
                            targetField,
                            mutable,
                            targetField.IsInitOnly || targetField.IsLiteral));
                        break;
                    case (MethodInfo shadowMethod, MethodInfo targetMethod):
                        methods.Add((shadowMethod, targetMethod));
                        break;
                    case (PropertyInfo shadowProperty, PropertyInfo targetProperty):
                        RegisterPropertyAccessors(shadowProperty, targetProperty, mutable, targetType, name, methods);
                        break;
                }
                continue;
            }

            var message = $"Shadow 成员不存在：{targetType.FullName}::{name}";
            if (shadow.Optional) warnings.Add(message);
            else throw new MixinApplyException(message);
        }
        return new ShadowValidation(fields, methods);
    }

    private static MemberInfo? ResolveShadowTarget(MemberInfo member, Type targetType, string name)
        => member switch
        {
            FieldInfo => MixinTargetResolver.FindField(targetType, name),
            PropertyInfo => MixinTargetResolver.FindProperty(targetType, name),
            MethodInfo method => MixinTargetResolver.EnumerateMethods(targetType).SingleOrDefault(candidate =>
                candidate.Name == name &&
                candidate.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(method.GetParameters().Select(parameter => parameter.ParameterType))),
            _ => null
        };

    private static void ValidateShadowSignature(MemberInfo shadow, MemberInfo target, Type targetType, string name)
    {
        static bool IsStatic(MemberInfo member) => member switch
        {
            FieldInfo field => field.IsStatic,
            PropertyInfo property => (property.GetMethod ?? property.SetMethod)?.IsStatic == true,
            MethodInfo method => method.IsStatic,
            _ => false
        };

        if (IsStatic(shadow) != IsStatic(target))
            throw new MixinApplyException($"Shadow 静态/实例不匹配：{targetType.FullName}::{name}");
        var compatible = (shadow, target) switch
        {
            (FieldInfo left, FieldInfo right) => left.FieldType == right.FieldType,
            (PropertyInfo left, PropertyInfo right) => left.PropertyType == right.PropertyType &&
                                                       left.GetIndexParameters().Select(p => p.ParameterType)
                                                           .SequenceEqual(right.GetIndexParameters().Select(p => p.ParameterType)),
            (MethodInfo left, MethodInfo right) => left.ReturnType == right.ReturnType &&
                                                   left.GetParameters().Select(p => p.ParameterType)
                                                       .SequenceEqual(right.GetParameters().Select(p => p.ParameterType)),
            _ => false
        };
        if (!compatible)
            throw new MixinApplyException($"Shadow 类型或签名不匹配：{targetType.FullName}::{name}");
    }

    private static void RegisterPropertyAccessors(
        PropertyInfo shadow,
        PropertyInfo target,
        bool mutable,
        Type targetType,
        string name,
        List<(MethodInfo Shadow, MethodInfo Target)> methods)
    {
        if (shadow.GetMethod is not null)
        {
            if (target.GetMethod is null)
                throw new MixinApplyException($"Shadow 属性目标不可读：{targetType.FullName}::{name}");
            methods.Add((shadow.GetMethod, target.GetMethod));
        }
        if (shadow.SetMethod is null) return;
        if (target.SetMethod is null)
        {
            if (!mutable)
                throw new MixinApplyException($"写入 Final Shadow 属性必须声明 [Mutable]：{targetType.FullName}::{name}");
            throw new MixinApplyException($"Mutable Shadow 属性目标没有 setter：{targetType.FullName}::{name}");
        }
        methods.Add((shadow.SetMethod, target.SetMethod));
    }
}

internal static class MixinRuntimeDispatch
{
    private static readonly ConcurrentDictionary<MethodBase, TargetPlan> Plans = new();
    private static readonly ConcurrentDictionary<long, (MethodBase Target, HandlerPlan Handler)> ReturnHandlers = new();
    private static readonly ConcurrentDictionary<MethodBase, long[]> ReturnSequences = new();

    public static void Register(MethodBase target, TargetPlan plan)
    {
        UnregisterReturnHandlers(target);
        Plans[target] = plan;
        var sequences = plan.Transpilers
            .Where(handler => handler.Operation is InjectAttribute { At: MixinAt.Return or MixinAt.Tail })
            .Select(handler => handler.Sequence)
            .ToArray();
        foreach (var handler in plan.Transpilers.Where(handler => sequences.Contains(handler.Sequence)))
            ReturnHandlers[handler.Sequence] = (target, handler);
        ReturnSequences[target] = sequences;
    }

    public static void Unregister(MethodBase target)
    {
        Plans.TryRemove(target, out _);
        UnregisterReturnHandlers(target);
    }

    public static void ReturnVoid(long sequence, object? instance, object?[] arguments)
    {
        var (target, handler) = GetReturnHandler(sequence);
        var inject = (InjectAttribute)handler.Operation;
        var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, null);
        MixinInvocation.Invoke(handler, target, instance, arguments, null, callback);
    }

    public static object? ReturnResult(long sequence, object? instance, object?[] arguments, object? currentResult)
    {
        var (target, handler) = GetReturnHandler(sequence);
        var inject = (InjectAttribute)handler.Operation;
        var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, currentResult);
        var invoked = MixinInvocation.Invoke(handler, target, instance, arguments, currentResult, callback);
        return MixinInvocation.GetCallbackResult(callback, invoked);
    }

    private static (MethodBase Target, HandlerPlan Handler) GetReturnHandler(long sequence)
        => ReturnHandlers.TryGetValue(sequence, out var handler)
            ? handler
            : throw new MixinApplyException($"RETURN/TAIL 处理器未注册：{sequence}");

    private static void UnregisterReturnHandlers(MethodBase target)
    {
        if (!ReturnSequences.TryRemove(target, out var sequences)) return;
        foreach (var sequence in sequences) ReturnHandlers.TryRemove(sequence, out _);
    }

    public static bool PrefixVoid(MethodBase target, object? instance, object?[] arguments)
    {
        if (!Plans.TryGetValue(target, out var plan)) return true;
        foreach (var handler in plan.Head.ToArray())
        {
            var inject = (InjectAttribute)handler.Operation;
            var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, null);
            MixinInvocation.Invoke(handler, target, instance, arguments, null, callback);
            if (callback.IsCancelled)
            {
                RunReturnHandlersAfterCancellation(plan, target, instance, arguments, null);
                return false;
            }
        }
        if (plan.Overwrite is null) return true;
        MixinInvocation.Invoke(plan.Overwrite, target, instance, arguments, null, null);
        return false;
    }

    public static object? PrefixResult(
        MethodBase target,
        object? instance,
        object?[] arguments,
        object? currentResult,
        out bool runOriginal)
    {
        runOriginal = true;
        if (!Plans.TryGetValue(target, out var plan)) return currentResult;
        foreach (var handler in plan.Head.ToArray())
        {
            var inject = (InjectAttribute)handler.Operation;
            var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, currentResult);
            MixinInvocation.Invoke(handler, target, instance, arguments, currentResult, callback);
            if (!callback.IsCancelled) continue;
            runOriginal = false;
            var cancelledResult = MixinInvocation.GetCallbackResult(callback, currentResult);
            return RunReturnHandlersAfterCancellation(plan, target, instance, arguments, cancelledResult);
        }
        if (plan.Overwrite is null) return currentResult;
        runOriginal = false;
        return MixinInvocation.Invoke(plan.Overwrite, target, instance, arguments, currentResult, null);
    }

    public static void PostfixVoid(MethodBase target, object? instance, object?[] arguments)
    {
        if (!Plans.TryGetValue(target, out var plan)) return;
        foreach (var handler in plan.Return.ToArray())
        {
            var inject = (InjectAttribute)handler.Operation;
            var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, null);
            MixinInvocation.Invoke(handler, target, instance, arguments, null, callback);
        }
    }

    public static object? PostfixResult(MethodBase target, object? instance, object?[] arguments, object? currentResult)
    {
        if (!Plans.TryGetValue(target, out var plan)) return currentResult;
        foreach (var handler in plan.Return.ToArray())
        {
            var inject = (InjectAttribute)handler.Operation;
            var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, currentResult);
            var invoked = MixinInvocation.Invoke(handler, target, instance, arguments, currentResult, callback);
            currentResult = MixinInvocation.GetCallbackResult(callback, invoked);
        }
        return currentResult;
    }

    private static object? RunReturnHandlersAfterCancellation(
        TargetPlan plan,
        MethodBase target,
        object? instance,
        object?[] arguments,
        object? currentResult)
    {
        // RETURN handlers normally live in the transpiled original body. A HEAD cancellation
        // skips that body entirely, so treat the cancellation as one synthetic RETURN site.
        // Local/ordinal/slice-sensitive handlers still require a real IL return site and are skipped.
        foreach (var handler in plan.Transpilers.Where(CanRunAtSyntheticReturn).ToArray())
        {
            var inject = (InjectAttribute)handler.Operation;
            var callback = MixinInvocation.CreateCallback(target, inject.Cancellable, currentResult);
            var invoked = MixinInvocation.Invoke(handler, target, instance, arguments, currentResult, callback);
            currentResult = MixinInvocation.GetCallbackResult(callback, invoked);
        }
        return currentResult;
    }

    private static bool CanRunAtSyntheticReturn(HandlerPlan handler)
    {
        if (handler.Operation is not InjectAttribute { At: MixinAt.Return } inject) return false;
        if (inject.Ordinal >= 0 || !string.IsNullOrWhiteSpace(inject.Slice) ||
            !string.IsNullOrWhiteSpace(inject.SliceFrom) || !string.IsNullOrWhiteSpace(inject.SliceTo))
            return false;
        if (inject.Shift is not AtShift.Before && !(inject.Shift == AtShift.By && inject.By == 0))
            return false;
        return handler.Handler.GetParameters().All(parameter =>
            !parameter.IsDefined(typeof(LocalAttribute), false));
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        System.Reflection.Emit.ILGenerator generator,
        MethodBase __originalMethod)
    {
        if (!Plans.TryGetValue(__originalMethod, out var plan)) return instructions;
        return MixinTranspiler.Apply(__originalMethod, instructions, generator, plan.Transpilers);
    }
}
