using System;
using System.Collections.Generic;
using System.Reflection;

namespace PCL.Mixin;

internal sealed class TargetPlan(MethodBase target)
{
    public MethodBase Target { get; } = target;
    public List<HandlerPlan> Head { get; } = [];
    public List<HandlerPlan> Return { get; } = [];
    public List<HandlerPlan> Transpilers { get; } = [];
    public HandlerPlan? Overwrite { get; set; }
    public int Revision { get; set; }

    public bool IsEmpty => Head.Count == 0 && Return.Count == 0 && Transpilers.Count == 0 && Overwrite is null;

    public IEnumerable<HandlerPlan> AllHandlers()
    {
        foreach (var handler in Head) yield return handler;
        foreach (var handler in Return) yield return handler;
        foreach (var handler in Transpilers) yield return handler;
        if (Overwrite is not null) yield return Overwrite;
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
    object? MixinInstance,
    IReadOnlyList<ShadowFieldBinding> ShadowFields)
{
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
