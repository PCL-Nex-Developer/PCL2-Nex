using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace PCL.Mixin;

internal sealed record ShadowFieldBinding(
    FieldInfo Shadow,
    FieldInfo Target,
    bool Mutable,
    bool TargetFinal)
{
    public object? GetShadowValue(object? mixinInstance) => Shadow.GetValue(Shadow.IsStatic ? null : mixinInstance);
    public object? GetTargetValue(object? targetInstance) => Target.GetValue(Target.IsStatic ? null : targetInstance);
    public void SetShadowValue(object? mixinInstance, object? value) =>
        Shadow.SetValue(Shadow.IsStatic ? null : mixinInstance, value);
    public void SetTargetValue(object? targetInstance, object? value) =>
        Target.SetValue(Target.IsStatic ? null : targetInstance, value);
}

internal static class MixinShadowDispatch
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<MethodBase, MethodInfo> Methods = [];

    [ThreadStatic]
    private static Stack<object?>? _targetInstances;

    public static void Register(MethodInfo shadow, MethodInfo target)
    {
        lock (SyncRoot) Methods[shadow] = target;
    }

    public static void Unregister(MethodInfo shadow)
    {
        lock (SyncRoot) Methods.Remove(shadow);
    }

    public static IDisposable Enter(object? targetInstance)
    {
        (_targetInstances ??= new Stack<object?>()).Push(targetInstance);
        return new Scope();
    }

    public static void InvokeVoid(MethodBase shadow, object?[] arguments) => InvokeCore(shadow, arguments);

    public static object? InvokeResult(MethodBase shadow, object?[] arguments) => InvokeCore(shadow, arguments);

    private static object? InvokeCore(MethodBase shadow, object?[] arguments)
    {
        MethodInfo target;
        lock (SyncRoot)
        {
            if (!Methods.TryGetValue(shadow, out target!))
                throw new MixinApplyException($"Shadow 方法未绑定：{shadow.DeclaringType?.FullName}.{shadow.Name}");
        }

        if (_targetInstances is null || _targetInstances.Count == 0)
            throw new MixinApplyException($"Shadow 方法只能在 Mixin 处理器调用期间使用：{shadow.DeclaringType?.FullName}.{shadow.Name}");
        var instance = target.IsStatic ? null : _targetInstances.Peek();
        if (!target.IsStatic && (instance is null || !target.DeclaringType!.IsInstanceOfType(instance)))
            throw new MixinApplyException($"Shadow 方法缺少目标实例：{target.DeclaringType?.FullName}.{target.Name}");

        try
        {
            return target.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _targetInstances!.Pop();
        }
    }
}
