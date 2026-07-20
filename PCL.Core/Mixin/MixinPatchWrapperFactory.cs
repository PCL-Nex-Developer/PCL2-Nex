using System;
using System.Reflection;
using System.Reflection.Emit;

namespace PCL.Mixin;

internal static class MixinPatchWrapperFactory
{
    private static readonly MethodInfo PrefixVoidMethod = typeof(MixinRuntimeDispatch).GetMethod(
        nameof(MixinRuntimeDispatch.PrefixVoid), BindingFlags.Static | BindingFlags.Public)!;
    private static readonly MethodInfo PrefixResultMethod = typeof(MixinRuntimeDispatch).GetMethod(
        nameof(MixinRuntimeDispatch.PrefixResult), BindingFlags.Static | BindingFlags.Public)!;
    private static readonly MethodInfo PostfixVoidMethod = typeof(MixinRuntimeDispatch).GetMethod(
        nameof(MixinRuntimeDispatch.PostfixVoid), BindingFlags.Static | BindingFlags.Public)!;
    private static readonly MethodInfo PostfixResultMethod = typeof(MixinRuntimeDispatch).GetMethod(
        nameof(MixinRuntimeDispatch.PostfixResult), BindingFlags.Static | BindingFlags.Public)!;

    public static MethodInfo PrefixFactory(MethodBase original) => CreatePrefix(original);
    public static MethodInfo PostfixFactory(MethodBase original) => CreatePostfix(original);

    public static MethodInfo CreatePrefix(MethodBase target)
    {
        var returnType = MixinInvocation.GetReturnType(target);
        var types = BuildParameterTypes(target, returnType == typeof(void) ? null : returnType.MakeByRefType());
        var method = new DynamicMethod(
            $"PCL_Mixin_Prefix_{target.MetadataToken}_{Guid.NewGuid():N}",
            typeof(bool),
            types,
            typeof(MixinPatchWrapperFactory).Module,
            true);
        DefineNames(method, target, returnType != typeof(void));
        var il = method.GetILGenerator();

        EmitCommonArguments(il, target);
        if (returnType == typeof(void))
        {
            il.Emit(OpCodes.Call, PrefixVoidMethod);
            il.Emit(OpCodes.Ret);
            return method;
        }

        var resultArgument = types.Length - 1;
        var runOriginal = il.DeclareLocal(typeof(bool));
        var nextResult = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg, resultArgument);
        il.Emit(OpCodes.Ldobj, returnType);
        if (returnType.IsValueType) il.Emit(OpCodes.Box, returnType);
        il.Emit(OpCodes.Ldloca, runOriginal);
        il.Emit(OpCodes.Call, PrefixResultMethod);
        il.Emit(OpCodes.Stloc, nextResult);
        il.Emit(OpCodes.Ldarg, resultArgument);
        il.Emit(OpCodes.Ldloc, nextResult);
        EmitObjectToType(il, returnType);
        il.Emit(OpCodes.Stobj, returnType);
        il.Emit(OpCodes.Ldloc, runOriginal);
        il.Emit(OpCodes.Ret);
        return method;
    }

    public static MethodInfo CreatePostfix(MethodBase target)
    {
        var returnType = MixinInvocation.GetReturnType(target);
        var types = BuildParameterTypes(target, returnType == typeof(void) ? null : returnType.MakeByRefType());
        var method = new DynamicMethod(
            $"PCL_Mixin_Postfix_{target.MetadataToken}_{Guid.NewGuid():N}",
            typeof(void),
            types,
            typeof(MixinPatchWrapperFactory).Module,
            true);
        DefineNames(method, target, returnType != typeof(void));
        var il = method.GetILGenerator();

        EmitCommonArguments(il, target);
        if (returnType == typeof(void))
        {
            il.Emit(OpCodes.Call, PostfixVoidMethod);
            il.Emit(OpCodes.Ret);
            return method;
        }

        var resultArgument = types.Length - 1;
        il.Emit(OpCodes.Ldarg, resultArgument);
        il.Emit(OpCodes.Ldobj, returnType);
        if (returnType.IsValueType) il.Emit(OpCodes.Box, returnType);
        il.Emit(OpCodes.Call, PostfixResultMethod);
        var nextResult = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, nextResult);
        il.Emit(OpCodes.Ldarg, resultArgument);
        il.Emit(OpCodes.Ldloc, nextResult);
        EmitObjectToType(il, returnType);
        il.Emit(OpCodes.Stobj, returnType);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static Type[] BuildParameterTypes(MethodBase target, Type? resultType)
    {
        var count = target.IsStatic ? 2 : 3;
        if (resultType is not null) count++;
        var types = new Type[count];
        var index = 0;
        types[index++] = typeof(MethodBase);
        if (!target.IsStatic) types[index++] = typeof(object);
        types[index++] = typeof(object[]);
        if (resultType is not null) types[index] = resultType;
        return types;
    }

    private static void DefineNames(DynamicMethod method, MethodBase target, bool hasResult)
    {
        var index = 1;
        method.DefineParameter(index++, ParameterAttributes.None, "__originalMethod");
        if (!target.IsStatic) method.DefineParameter(index++, ParameterAttributes.None, "__instance");
        method.DefineParameter(index++, ParameterAttributes.None, "__args");
        if (hasResult) method.DefineParameter(index, ParameterAttributes.None, "__result");
    }

    private static void EmitCommonArguments(ILGenerator il, MethodBase target)
    {
        var index = 0;
        il.Emit(OpCodes.Ldarg, index++);
        if (target.IsStatic) il.Emit(OpCodes.Ldnull);
        else il.Emit(OpCodes.Ldarg, index++);
        il.Emit(OpCodes.Ldarg, index);
    }

    private static void EmitObjectToType(ILGenerator il, Type type)
    {
        if (type.IsValueType) il.Emit(OpCodes.Unbox_Any, type);
        else il.Emit(OpCodes.Castclass, type);
    }
}
