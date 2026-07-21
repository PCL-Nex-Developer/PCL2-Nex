using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PCL.Mixin;

internal sealed class TargetPlan(MethodBase target)
{
    public MethodBase Target { get; } = target;
    public List<HandlerPlan> Head { get; } = [];
    public List<HandlerPlan> Return { get; } = [];
    public List<HandlerPlan> Transpilers { get; } = [];
    public List<HandlerPlan> Overwrites { get; } = [];
    public HandlerPlan? Overwrite => Overwrites.FirstOrDefault();
    public int Revision { get; set; }

    public bool IsEmpty => Head.Count == 0 && Return.Count == 0 && Transpilers.Count == 0 && Overwrites.Count == 0;

    public IEnumerable<HandlerPlan> AllHandlers()
    {
        foreach (var handler in Head) yield return handler;
        foreach (var handler in Return) yield return handler;
        foreach (var handler in Transpilers) yield return handler;
        foreach (var handler in Overwrites) yield return handler;
    }

    public void Sort()
    {
        static int Compare(HandlerPlan left, HandlerPlan right)
        {
            var priority = right.Priority.CompareTo(left.Priority);
            return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
        }

        Head.Sort(Compare);
        Return.Sort(Compare);
        Transpilers.Sort(Compare);
        Overwrites.Sort(Compare);
    }
}

internal sealed record HandlerPlan(
    string ApplicationId,
    Assembly SourceAssembly,
    Type MixinType,
    Type TargetType,
    MethodInfo Handler,
    MixinOperationAttribute Operation,
    int Priority,
    long Sequence,
    MixinInstanceScope? MixinInstances,
    IReadOnlyList<ShadowFieldBinding> ShadowFields)
{
    public object? GetMixinInstance(object? targetInstance)
        => Handler.IsStatic ? null : MixinInstances!.Get(targetInstance);

    public string Kind => Operation.GetType().Name.Replace("Attribute", string.Empty, StringComparison.Ordinal);

    public string InjectionPoint => Operation switch
    {
        InjectAttribute inject => inject.At.ToString().ToUpperInvariant(),
        RedirectAttribute redirect => redirect.At.ToString().ToUpperInvariant(),
        ModifyArgAttribute or ModifyArgsAttribute => "INVOKE",
        ModifyVariableAttribute variable => variable.At.ToString().ToUpperInvariant(),
        ModifyConstantAttribute => "CONSTANT",
        OverwriteAttribute => "OVERWRITE",
        _ => Kind.ToUpperInvariant()
    };

    public string? TargetDescriptor => Operation switch
    {
        InjectAttribute inject => inject.Target,
        RedirectAttribute redirect => redirect.Target,
        ModifyArgAttribute modifyArg => modifyArg.Target,
        ModifyArgsAttribute modifyArgs => modifyArgs.Target,
        ModifyVariableAttribute variable when variable.Index >= 0 => $"local:{variable.Index}",
        ModifyConstantAttribute constant => constant.Target,
        _ => null
    };

    public string TargetMethodName() => Operation.Method;
}

internal sealed class MixinInstanceScope
{
    private readonly Type _mixinType;
    private readonly object _staticTargetInstance;
    private readonly ConditionalWeakTable<object, object> _targetInstances = new();

    public MixinInstanceScope(Type mixinType)
    {
        _mixinType = mixinType;
        _staticTargetInstance = CreateInstance();
    }

    public object Get(object? targetInstance)
        => targetInstance is null
            ? _staticTargetInstance
            : _targetInstances.GetValue(targetInstance, _ => CreateInstance());

    private object CreateInstance()
        => Activator.CreateInstance(_mixinType, nonPublic: true)
           ?? throw new MixinApplyException($"无法创建 Mixin 类型实例：{_mixinType.FullName}");
}
