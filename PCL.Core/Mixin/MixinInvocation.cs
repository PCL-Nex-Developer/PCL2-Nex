using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace PCL.Mixin;

internal static class MixinInvocation
{
    public static object? Invoke(
        HandlerPlan plan,
        MethodBase target,
        object? targetInstance,
        object?[] targetArguments,
        object? returnValue,
        CallbackInfo? callback)
    {
        var targetParameters = target.GetParameters();
        var handlerParameters = plan.Handler.GetParameters();
        var arguments = new object?[handlerParameters.Length];
        var copyBack = new List<(int HandlerIndex, int TargetIndex)>();
        var returnIndices = new List<int>();

        for (var index = 0; index < handlerParameters.Length; index++)
        {
            var parameter = handlerParameters[index];
            var parameterType = UnwrapByRef(parameter.ParameterType);
            var arg = parameter.GetCustomAttribute<ArgAttribute>();
            var isReturn = parameter.IsDefined(typeof(ReturnAttribute), false);

            if (typeof(CallbackInfo).IsAssignableFrom(parameterType))
            {
                if (callback is null || !parameterType.IsInstanceOfType(callback))
                    throw new MixinApplyException($"处理器 {Format(plan.Handler)} 的 CallbackInfo 类型与目标返回类型不匹配。");
                arguments[index] = callback;
            }
            else if (parameter.IsDefined(typeof(ThisAttribute), false) || parameter.Name is "__instance" or "instance")
            {
                arguments[index] = Coerce(targetInstance, parameterType, parameter);
            }
            else if (arg is not null)
            {
                if ((uint)arg.Index >= (uint)targetArguments.Length)
                    throw new MixinApplyException($"处理器 {Format(plan.Handler)} 引用了不存在的参数 {arg.Index}。");
                arguments[index] = Coerce(targetArguments[arg.Index], parameterType, parameter);
                if (parameter.ParameterType.IsByRef) copyBack.Add((index, arg.Index));
            }
            else if (isReturn)
            {
                arguments[index] = Coerce(returnValue, parameterType, parameter);
                if (parameter.ParameterType.IsByRef) returnIndices.Add(index);
            }
            else if (parameterType == typeof(MethodBase) || parameterType == typeof(MethodInfo))
            {
                arguments[index] = target;
            }
            else if (parameterType == typeof(object[]))
            {
                arguments[index] = targetArguments;
            }
            else
            {
                var targetIndex = Array.FindIndex(targetParameters, candidate => candidate.Name == parameter.Name);
                if (targetIndex >= 0)
                {
                    arguments[index] = Coerce(targetArguments[targetIndex], parameterType, parameter);
                    if (parameter.ParameterType.IsByRef) copyBack.Add((index, targetIndex));
                }
                else if (targetInstance is not null && parameterType.IsInstanceOfType(targetInstance))
                {
                    arguments[index] = targetInstance;
                }
                else if (parameter.HasDefaultValue)
                {
                    arguments[index] = parameter.DefaultValue;
                }
                else
                {
                    throw new MixinApplyException(
                        $"无法绑定处理器参数 {Format(plan.Handler)}({parameter.Name}: {parameter.ParameterType.FullName})。" +
                        "请使用 [This]、[Arg]、[Return]，或令参数名与目标方法一致。");
                }
            }
        }

        object? InvokeHandler()
        {
            var mixinInstance = plan.GetMixinInstance(targetInstance);
            var shadowStates = new List<(ShadowFieldBinding Binding, object? Previous, object? Initial)>();
            foreach (var binding in plan.ShadowFields)
            {
                var previous = binding.GetShadowValue(mixinInstance);
                var initial = binding.GetTargetValue(targetInstance);
                binding.SetShadowValue(mixinInstance, initial);
                shadowStates.Add((binding, previous, initial));
            }

            try
            {
                using var shadowScope = MixinShadowDispatch.Enter(targetInstance);
                return plan.Handler.Invoke(mixinInstance, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            finally
            {
                foreach (var (binding, previous, initial) in shadowStates)
                {
                    var current = binding.GetShadowValue(mixinInstance);
                    try
                    {
                        if (!Equals(current, initial))
                        {
                            if (binding.TargetFinal && !binding.Mutable)
                                throw new MixinApplyException(
                                    $"写入 Final Shadow 必须声明 [Mutable]：" +
                                    $"{binding.Target.DeclaringType?.FullName}.{binding.Target.Name}");
                            binding.SetTargetValue(targetInstance, current);
                        }
                    }
                    finally
                    {
                        binding.SetShadowValue(mixinInstance, previous);
                    }
                }
            }
        }

        object? result;
        var invocationInstance = plan.GetMixinInstance(targetInstance);
        var shadowLock = plan.ShadowFields.Count == 0 ? null : invocationInstance ?? plan.MixinType;
        if (shadowLock is null)
        {
            result = InvokeHandler();
        }
        else
        {
            lock (shadowLock) result = InvokeHandler();
        }

        foreach (var (handlerIndex, targetIndex) in copyBack)
            targetArguments[targetIndex] = arguments[handlerIndex];
        if (returnIndices.Count > 0) returnValue = arguments[returnIndices[^1]];
        return plan.Handler.ReturnType == typeof(void) ? returnValue : result;
    }

    public static CallbackInfo CreateCallback(MethodBase target, bool cancellable, object? result)
    {
        var returnType = GetReturnType(target);
        if (returnType == typeof(void)) return new CallbackInfo(target.Name, cancellable);
        var callbackType = typeof(CallbackInfo<>).MakeGenericType(returnType);
        var initial = result ?? (returnType.IsValueType ? Activator.CreateInstance(returnType) : null);
        return (CallbackInfo)Activator.CreateInstance(callbackType, target.Name, cancellable, initial)!;
    }

    public static object? GetCallbackResult(CallbackInfo callback, object? fallback)
    {
        if (!callback.IsReturnValueModified) return fallback;
        var property = callback.GetType().GetProperty(nameof(CallbackInfo<object>.ReturnValue));
        return property?.GetValue(callback) ?? fallback;
    }

    public static Type GetReturnType(MethodBase method)
        => method is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void);

    private static object? Coerce(object? value, Type type, ParameterInfo parameter)
    {
        if (value is null)
        {
            if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null) return null;
            throw new MixinApplyException($"不能把 null 绑定到 {parameter.Member.Name}.{parameter.Name}: {type.FullName}。");
        }
        if (type.IsInstanceOfType(value)) return value;
        throw new MixinApplyException(
            $"值 {value.GetType().FullName} 不能绑定到 {parameter.Member.Name}.{parameter.Name}: {type.FullName}。");
    }

    private static Type UnwrapByRef(Type type) => type.IsByRef ? type.GetElementType()! : type;
    private static string Format(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";
}
