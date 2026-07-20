using System;
using System.Reflection;
using System.Reflection.Emit;

namespace PCL.Mixin;

internal static class MixinShadowWrapperFactory
{
    private static readonly MethodInfo InvokeVoidMethod = typeof(MixinShadowDispatch).GetMethod(
        nameof(MixinShadowDispatch.InvokeVoid), BindingFlags.Static | BindingFlags.Public)!;
    private static readonly MethodInfo InvokeResultMethod = typeof(MixinShadowDispatch).GetMethod(
        nameof(MixinShadowDispatch.InvokeResult), BindingFlags.Static | BindingFlags.Public)!;

    public static MethodInfo PrefixFactory(MethodBase original)
    {
        var returnType = original is MethodInfo method ? method.ReturnType : typeof(void);
        var parameterTypes = returnType == typeof(void)
            ? new[] { typeof(MethodBase), typeof(object[]) }
            : new[] { typeof(MethodBase), typeof(object[]), returnType.MakeByRefType() };
        var wrapper = new DynamicMethod(
            $"PCL_Mixin_Shadow_{original.MetadataToken}_{Guid.NewGuid():N}",
            typeof(bool),
            parameterTypes,
            typeof(MixinShadowWrapperFactory).Module,
            true);
        wrapper.DefineParameter(1, ParameterAttributes.None, "__originalMethod");
        wrapper.DefineParameter(2, ParameterAttributes.None, "__args");
        if (returnType != typeof(void)) wrapper.DefineParameter(3, ParameterAttributes.None, "__result");

        var il = wrapper.GetILGenerator();
        if (returnType == typeof(void))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, InvokeVoidMethod);
        }
        else
        {
            var result = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, InvokeResultMethod);
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, result);
            EmitObjectToType(il, returnType);
            il.Emit(OpCodes.Stobj, returnType);
        }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return wrapper;
    }

    private static void EmitObjectToType(ILGenerator il, Type type)
    {
        if (type.IsValueType) il.Emit(OpCodes.Unbox_Any, type);
        else il.Emit(OpCodes.Castclass, type);
    }
}
