using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace PCL.Mixin;

/// <summary>创建由 [Accessor]/[Invoker] 接口描述的私有成员访问器。</summary>
public static class MixinAccessors
{
    public static TAccessor Create<TAccessor>(object? target = null) where TAccessor : class
        => MixinAccessorProxy.Create<TAccessor>(target);
}

internal static class MixinAccessorProxy
{
    private static readonly ConditionalWeakTable<Type, ProxyFactory> Factories = new();
    private static long _proxyId;

    public static TAccessor Create<TAccessor>(object? target) where TAccessor : class
    {
        var accessorType = typeof(TAccessor);
        if (!accessorType.IsInterface)
            throw new MixinApplyException($"Accessor 必须是接口：{accessorType.FullName}");
        var mixin = accessorType.GetCustomAttributes<MixinAttribute>(false).SingleOrDefault()
            ?? throw new MixinApplyException($"Accessor 接口缺少 [Mixin]：{accessorType.FullName}");
        var targetType = MixinTargetResolver.ResolveType(mixin)
            ?? throw new MixinApplyException($"Accessor 目标类型不存在：{mixin.TargetName}");
        if (target is not null && !targetType.IsInstanceOfType(target))
            throw new MixinApplyException($"Accessor 实例类型不匹配：需要 {targetType.FullName}，实际 {target.GetType().FullName}。");

        var factory = Factories.GetValue(accessorType, BuildFactory);
        var state = new AccessorInvocationState(targetType, target);
        var invokers = factory.Methods
            .Select<MethodInfo, Func<object?[], object?>>(method => arguments => state.Invoke(method, arguments))
            .ToArray();
        return (TAccessor)factory.Create(invokers);
    }

    private static ProxyFactory BuildFactory(Type accessorType)
    {
        var interfaces = accessorType.GetInterfaces().Append(accessorType).Distinct().ToArray();
        var methods = interfaces
            .SelectMany(type => type.GetMethods())
            .Distinct(MethodInfoIdentityComparer.Instance)
            .ToArray();
        if (methods.Any(method => method.IsGenericMethodDefinition))
            throw new MixinApplyException($"Accessor 暂不支持泛型接口方法：{accessorType.FullName}");

        var id = Interlocked.Increment(ref _proxyId);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"PCL.Mixin.AccessorProxy.{id}"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule($"PCL.Mixin.AccessorProxy.{id}");
        var builder = module.DefineType(
            $"PCL.Mixin.Generated.AccessorProxy_{id}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(object),
            interfaces);
        var invokerType = typeof(Func<object?[], object?>);
        var invokersField = builder.DefineField(
            "_invokers",
            invokerType.MakeArrayType(),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        var constructor = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [invokerType.MakeArrayType()]);
        var constructorIl = constructor.GetILGenerator();
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Ldarg_1);
        constructorIl.Emit(OpCodes.Stfld, invokersField);
        constructorIl.Emit(OpCodes.Ret);

        for (var index = 0; index < methods.Length; index++)
            ImplementMethod(builder, invokersField, methods[index], index);

        return new ProxyFactory(builder.CreateType()!, methods);
    }

    private static void ImplementMethod(
        TypeBuilder builder,
        FieldBuilder invokersField,
        MethodInfo interfaceMethod,
        int methodIndex)
    {
        var parameters = interfaceMethod.GetParameters();
        var attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
                         MethodAttributes.HideBySig | MethodAttributes.NewSlot;
        if (interfaceMethod.IsSpecialName) attributes |= MethodAttributes.SpecialName;
        var method = builder.DefineMethod(
            interfaceMethod.Name,
            attributes,
            interfaceMethod.CallingConvention,
            interfaceMethod.ReturnType,
            parameters.Select(parameter => parameter.ParameterType).ToArray());
        for (var index = 0; index < parameters.Length; index++)
            method.DefineParameter(index + 1, parameters[index].Attributes, parameters[index].Name);

        var il = method.GetILGenerator();
        var arguments = il.DeclareLocal(typeof(object[]));
        var result = il.DeclareLocal(typeof(object));
        EmitInt(il, parameters.Length);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, arguments);
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var parameterType = parameter.ParameterType;
            var valueType = parameterType.IsByRef ? parameterType.GetElementType()! : parameterType;
            il.Emit(OpCodes.Ldloc, arguments);
            EmitInt(il, index);
            if (parameterType.IsByRef && parameter.IsOut && !parameter.IsIn)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg, index + 1);
                if (parameterType.IsByRef) il.Emit(OpCodes.Ldobj, valueType);
                if (valueType.IsValueType) il.Emit(OpCodes.Box, valueType);
            }
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, invokersField);
        EmitInt(il, methodIndex);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldloc, arguments);
        il.Emit(OpCodes.Callvirt, invokerTypeInvoke);
        il.Emit(OpCodes.Stloc, result);

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameterType = parameters[index].ParameterType;
            if (!parameterType.IsByRef) continue;
            var valueType = parameterType.GetElementType()!;
            il.Emit(OpCodes.Ldarg, index + 1);
            il.Emit(OpCodes.Ldloc, arguments);
            EmitInt(il, index);
            il.Emit(OpCodes.Ldelem_Ref);
            EmitObjectToType(il, valueType);
            il.Emit(OpCodes.Stobj, valueType);
        }

        if (interfaceMethod.ReturnType != typeof(void))
        {
            il.Emit(OpCodes.Ldloc, result);
            EmitObjectToType(il, interfaceMethod.ReturnType);
        }
        il.Emit(OpCodes.Ret);
        builder.DefineMethodOverride(method, interfaceMethod);
    }

    private static readonly MethodInfo invokerTypeInvoke = typeof(Func<object?[], object?>).GetMethod("Invoke")!;

    private static void EmitObjectToType(ILGenerator il, Type type)
    {
        if (type.IsValueType) il.Emit(OpCodes.Unbox_Any, type);
        else il.Emit(OpCodes.Castclass, type);
    }

    private static void EmitInt(ILGenerator il, int value)
    {
        switch (value)
        {
            case 0: il.Emit(OpCodes.Ldc_I4_0); break;
            case 1: il.Emit(OpCodes.Ldc_I4_1); break;
            case 2: il.Emit(OpCodes.Ldc_I4_2); break;
            case 3: il.Emit(OpCodes.Ldc_I4_3); break;
            case 4: il.Emit(OpCodes.Ldc_I4_4); break;
            case 5: il.Emit(OpCodes.Ldc_I4_5); break;
            case 6: il.Emit(OpCodes.Ldc_I4_6); break;
            case 7: il.Emit(OpCodes.Ldc_I4_7); break;
            case 8: il.Emit(OpCodes.Ldc_I4_8); break;
            default: il.Emit(OpCodes.Ldc_I4, value); break;
        }
    }

    private sealed record ProxyFactory(Type ProxyType, MethodInfo[] Methods)
    {
        public object Create(Func<object?[], object?>[] invokers)
            => Activator.CreateInstance(ProxyType, [invokers])!;
    }

    private sealed class MethodInfoIdentityComparer : IEqualityComparer<MethodInfo>
    {
        public static MethodInfoIdentityComparer Instance { get; } = new();
        public bool Equals(MethodInfo? x, MethodInfo? y) => x == y;
        public int GetHashCode(MethodInfo obj) => obj.GetHashCode();
    }
}

internal sealed class AccessorInvocationState(Type targetType, object? target)
{
    public object? Invoke(MethodInfo interfaceMethod, object?[] args)
    {
        var property = interfaceMethod.DeclaringType?.GetProperties().FirstOrDefault(candidate =>
            candidate.GetMethod == interfaceMethod || candidate.SetMethod == interfaceMethod);
        var accessor = interfaceMethod.GetCustomAttribute<AccessorAttribute>(true) ??
                       property?.GetCustomAttribute<AccessorAttribute>(true);
        if (accessor is not null)
            return InvokeAccessor(interfaceMethod, property, accessor, args);

        var invoker = interfaceMethod.GetCustomAttribute<InvokerAttribute>(true);
        if (invoker is not null)
            return InvokeTargetMethod(interfaceMethod, invoker, args);
        throw new MixinApplyException($"Accessor 接口方法缺少 [Accessor] 或 [Invoker]：{interfaceMethod.Name}");
    }

    private object? InvokeAccessor(
        MethodInfo interfaceMethod,
        PropertyInfo? interfaceProperty,
        AccessorAttribute accessor,
        object?[] args)
    {
        var inferred = interfaceProperty?.Name ?? InferAccessorName(interfaceMethod.Name);
        var name = string.IsNullOrWhiteSpace(accessor.Name) ? inferred : accessor.Name;
        var field = MixinTargetResolver.FindField(targetType, name);
        var property = MixinTargetResolver.FindProperty(targetType, name);
        if (field is null && property is null)
            throw new MissingMemberException(targetType.FullName, name);

        var isSetter = interfaceMethod.ReturnType == typeof(void) && args.Length == 1;
        if (field is not null)
        {
            var instance = field.IsStatic ? null : RequireTarget(name);
            if (isSetter)
            {
                var mutable = interfaceMethod.IsDefined(typeof(MutableAttribute), true) ||
                              interfaceProperty?.IsDefined(typeof(MutableAttribute), true) == true;
                if ((field.IsInitOnly || field.IsLiteral) && !mutable)
                    throw new MixinApplyException(
                        $"Accessor 写入 Final 字段必须声明 [Mutable]：{targetType.FullName}.{field.Name}");
                field.SetValue(instance, args[0]);
                return null;
            }
            return field.GetValue(instance);
        }

        var accessorMethod = isSetter ? property!.SetMethod : property!.GetMethod;
        if (accessorMethod is null) throw new MissingMethodException(targetType.FullName, interfaceMethod.Name);
        return InvokeReflected(accessorMethod, accessorMethod.IsStatic ? null : RequireTarget(name), args);
    }

    private object? InvokeTargetMethod(MethodInfo interfaceMethod, InvokerAttribute invoker, object?[] args)
    {
        var name = string.IsNullOrWhiteSpace(invoker.Name) ? InferInvokerName(interfaceMethod.Name) : invoker.Name;
        if (name is ".ctor" or "ctor")
        {
            var constructor = targetType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                interfaceMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
                modifiers: null)
                ?? throw new MissingMethodException(targetType.FullName, ".ctor");
            return InvokeReflected(constructor, null, args);
        }

        var parameterTypes = interfaceMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var method = MixinTargetResolver.EnumerateMethods(targetType).SingleOrDefault(candidate =>
            candidate.Name == name && candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes))
            ?? throw new MissingMethodException(targetType.FullName, name);
        return InvokeReflected(method, method.IsStatic ? null : RequireTarget(name), args);
    }

    private object RequireTarget(string member)
        => target ?? throw new MixinApplyException($"访问实例成员 {targetType.FullName}.{member} 时未提供目标实例。");

    private static object? InvokeReflected(MethodBase member, object? instance, object?[] args)
    {
        try
        {
            return member switch
            {
                MethodInfo method => method.Invoke(instance, args),
                ConstructorInfo constructor => constructor.Invoke(args),
                _ => throw new NotSupportedException(member.ToString())
            };
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static string InferAccessorName(string name)
        => name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal)
            ? name[4..]
            : name;

    private static string InferInvokerName(string name)
        => name.StartsWith("Invoke", StringComparison.Ordinal) && name.Length > 6 ? name[6..] : name;
}
