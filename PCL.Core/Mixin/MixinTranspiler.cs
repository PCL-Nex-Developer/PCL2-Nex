using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PCL.Mixin;

internal static class MixinTranspiler
{
    private static readonly ConstructorInfo MixinArgsConstructor = typeof(MixinArgs).GetConstructor([typeof(object[])])!;
    private static readonly MethodInfo MixinArgsGet = typeof(MixinArgs).GetMethod(nameof(MixinArgs.Get))!;

    public static IEnumerable<CodeInstruction> Apply(
        MethodBase original,
        IEnumerable<CodeInstruction> source,
        ILGenerator generator,
        IReadOnlyList<HandlerPlan> handlers)
    {
        var instructions = source.Select(instruction => new CodeInstruction(instruction)).ToList();
        foreach (var handler in handlers)
        {
            try
            {
                var count = handler.Operation switch
                {
                    RedirectAttribute redirect => ApplyRedirect(instructions, handler, redirect),
                    ModifyArgAttribute modifyArg => ApplyModifyArg(instructions, generator, handler, modifyArg),
                    ModifyArgsAttribute modifyArgs => ApplyModifyArgs(instructions, generator, handler, modifyArgs),
                    ModifyVariableAttribute modifyVariable => ApplyModifyVariable(instructions, handler, modifyVariable),
                    ModifyConstantAttribute modifyConstant => ApplyModifyConstant(instructions, handler, modifyConstant),
                    InjectAttribute inject => ApplyInject(instructions, generator, original, handler, inject),
                    _ => 0
                };
                ValidateCount(original, handler, count);
            }
            catch (MixinApplyException exception)
            {
                throw new MixinApplyException(
                    $"Mixin 操作失败：目标={original.DeclaringType?.FullName}.{original.Name}；" +
                    $"处理器={handler.MixinType.FullName}.{handler.Handler.Name}；" +
                    $"操作={handler.Kind}；注入点={handler.InjectionPoint}；" +
                    $"定位={handler.TargetDescriptor ?? "<method>"}；原因={exception.Message}",
                    exception);
            }
        }
        return instructions;
    }

    private static int ApplyRedirect(List<CodeInstruction> instructions, HandlerPlan plan, RedirectAttribute attribute)
    {
        if (attribute.At is not (MixinAt.Invoke or MixinAt.Field or MixinAt.New))
            throw new MixinApplyException($"Redirect 只支持 INVOKE、FIELD 或 NEW：{Format(plan.Handler)}");

        var matches = SelectMatches(
            instructions,
            (instruction, _) => IsMemberInstruction(instruction, attribute.At) &&
                                OpcodeMatches(instruction, attribute.Opcode) &&
                                MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target),
            attribute.Ordinal,
            attribute.SliceFrom,
            attribute.SliceTo);

        foreach (var index in matches.OrderByDescending(value => value))
        {
            var instruction = instructions[index];
            ValidateRedirectSignature(instruction, plan.Handler);
            var replacement = new CodeInstruction(OpCodes.Call, plan.Handler);
            MoveMetadata(instruction, replacement);
            instructions[index] = replacement;
        }
        return matches.Count;
    }

    private static int ApplyModifyConstant(
        List<CodeInstruction> instructions,
        HandlerPlan plan,
        ModifyConstantAttribute attribute)
    {
        ValidateUnaryModifier(plan.Handler, null);
        var matches = SelectMatches(
            instructions,
            (instruction, _) => TryGetConstant(instruction, out var value) &&
                                MixinTargetResolver.ConstantMatches(value, attribute.Target),
            attribute.Ordinal,
            attribute.SliceFrom,
            attribute.SliceTo);

        var inserted = 0;
        foreach (var originalIndex in matches)
        {
            var index = originalIndex + inserted;
            var constantType = GetConstantType(instructions[index], plan.Handler.GetParameters()[0].ParameterType);
            ValidateUnaryModifier(plan.Handler, constantType);
            instructions.Insert(index + 1, new CodeInstruction(OpCodes.Call, plan.Handler));
            inserted++;
        }
        return matches.Count;
    }

    private static int ApplyModifyVariable(
        List<CodeInstruction> instructions,
        HandlerPlan plan,
        ModifyVariableAttribute attribute)
    {
        if (attribute.At is not (MixinAt.Load or MixinAt.Store))
            throw new MixinApplyException($"ModifyVariable.At 必须为 LOAD 或 STORE：{Format(plan.Handler)}");
        ValidateUnaryModifier(plan.Handler, null);

        var matches = SelectMatches(
            instructions,
            (instruction, _) =>
            {
                if (!TryGetLocalIndex(instruction, out var localIndex, out var isLoad, out var isStore)) return false;
                if (attribute.Index >= 0 && attribute.Index != localIndex) return false;
                return attribute.At == MixinAt.Load ? isLoad : isStore;
            },
            attribute.Ordinal,
            null,
            null);

        var inserted = 0;
        foreach (var originalIndex in matches)
        {
            var index = originalIndex + inserted;
            if (attribute.At == MixinAt.Load)
                instructions.Insert(index + 1, new CodeInstruction(OpCodes.Call, plan.Handler));
            else
                instructions.Insert(index, TransferMetadata(instructions[index], new CodeInstruction(OpCodes.Call, plan.Handler)));
            inserted++;
        }
        return matches.Count;
    }

    private static int ApplyModifyArg(
        List<CodeInstruction> instructions,
        ILGenerator generator,
        HandlerPlan plan,
        ModifyArgAttribute attribute)
    {
        ValidateUnaryModifier(plan.Handler, null);
        var matches = SelectMatches(
            instructions,
            (instruction, _) => IsCall(instruction) &&
                                MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target),
            attribute.Ordinal,
            attribute.SliceFrom,
            attribute.SliceTo);

        var inserted = 0;
        foreach (var originalIndex in matches)
        {
            var index = originalIndex + inserted;
            var call = instructions[index];
            var signature = GetCallSignature(call);
            if ((uint)attribute.Index >= (uint)signature.ArgumentTypes.Length)
                throw new MixinApplyException($"ModifyArg 参数索引越界：{Format(plan.Handler)} -> {attribute.Index}");
            var argumentType = signature.ArgumentTypes[attribute.Index];
            if (argumentType.IsByRef) throw new MixinApplyException("ModifyArg 暂不支持 ref/out 调用参数。");
            ValidateUnaryModifier(plan.Handler, argumentType);

            var replacement = BuildModifyArg(generator, signature, attribute.Index, plan.Handler);
            MoveMetadata(call, replacement[0]);
            instructions.InsertRange(index, replacement);
            inserted += replacement.Count;
        }
        return matches.Count;
    }

    private static int ApplyModifyArgs(
        List<CodeInstruction> instructions,
        ILGenerator generator,
        HandlerPlan plan,
        ModifyArgsAttribute attribute)
    {
        var parameters = plan.Handler.GetParameters();
        if (plan.Handler.ReturnType != typeof(void) || parameters.Length != 1 || parameters[0].ParameterType != typeof(MixinArgs))
            throw new MixinApplyException($"ModifyArgs 处理器必须是 static void Handler(MixinArgs)：{Format(plan.Handler)}");

        var matches = SelectMatches(
            instructions,
            (instruction, _) => IsCall(instruction) &&
                                MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target),
            attribute.Ordinal,
            attribute.SliceFrom,
            attribute.SliceTo);

        var inserted = 0;
        foreach (var originalIndex in matches)
        {
            var index = originalIndex + inserted;
            var call = instructions[index];
            var signature = GetCallSignature(call);
            if (signature.ArgumentTypes.Any(type => type.IsByRef))
                throw new MixinApplyException("ModifyArgs 暂不支持 ref/out 调用参数。");
            var replacement = BuildModifyArgs(generator, signature, plan.Handler);
            MoveMetadata(call, replacement[0]);
            instructions.InsertRange(index, replacement);
            inserted += replacement.Count;
        }
        return matches.Count;
    }

    private static int ApplyInject(
        List<CodeInstruction> instructions,
        ILGenerator generator,
        MethodBase original,
        HandlerPlan plan,
        InjectAttribute attribute)
    {
        if (attribute.At is MixinAt.Head or MixinAt.Return or MixinAt.Tail)
        {
            if (attribute.At == MixinAt.Head)
                throw new MixinApplyException($"带 Local 参数的 HEAD 注入不受支持：{Format(plan.Handler)}");
        }
        if (attribute.Cancellable && attribute.At is not (MixinAt.Return or MixinAt.Tail))
            throw new MixinApplyException($"中间指令注入不能 Cancellable；请改用 HEAD/RETURN：{Format(plan.Handler)}");
        if (plan.Handler.ReturnType != typeof(void))
            throw new MixinApplyException($"Inject 处理器必须返回 void：{Format(plan.Handler)}");

        Func<CodeInstruction, int, bool> predicate = attribute.At switch
        {
            MixinAt.Return or MixinAt.Tail => (instruction, _) => instruction.opcode == OpCodes.Ret,
            MixinAt.Invoke => (instruction, _) => IsCall(instruction) &&
                                                      OpcodeMatches(instruction, attribute.Opcode) &&
                                                      MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target ?? string.Empty),
            MixinAt.InvokeAssign => (instruction, instructionIndex) => IsCall(instruction) &&
                                                                  OpcodeMatches(instruction, attribute.Opcode) &&
                                                                  MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target ?? string.Empty) &&
                                                                  instructionIndex + 1 < instructions.Count &&
                                                                  IsAssignment(instructions[instructionIndex + 1]),
            MixinAt.Field => (instruction, _) => IsMemberInstruction(instruction, MixinAt.Field) &&
                                                     OpcodeMatches(instruction, attribute.Opcode) &&
                                                     MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target ?? string.Empty),
            MixinAt.New => (instruction, _) => instruction.opcode == OpCodes.Newobj &&
                                                   OpcodeMatches(instruction, attribute.Opcode) &&
                                                   MixinTargetResolver.MemberMatches(instruction.operand, attribute.Target ?? string.Empty),
            MixinAt.Constant => (instruction, _) => TryGetConstant(instruction, out var value) &&
                                                        MixinTargetResolver.ConstantMatches(value, attribute.Target ?? string.Empty),
            MixinAt.Jump => (instruction, _) => IsJump(instruction) && OpcodeMatches(instruction, attribute.Opcode),
            MixinAt.Load => (instruction, instructionIndex) => TryGetLocalIndex(instruction, out var index, out var load, out _) &&
                                                         load && MatchesLocal(attribute.Target, index),
            MixinAt.Store => (instruction, instructionIndex) => TryGetLocalIndex(instruction, out var index, out _, out var store) &&
                                                          store && MatchesLocal(attribute.Target, index),
            _ => throw new MixinApplyException($"不支持的 Inject.At：{attribute.At}")
        };

        var ordinal = attribute.At == MixinAt.Tail ? -1 : attribute.Ordinal;
        var matches = SelectMatches(
            instructions,
            predicate,
            ordinal,
            attribute.SliceFrom,
            attribute.SliceTo);
        if (attribute.At == MixinAt.Tail)
        {
            matches = matches.Count == 0 ? [] : [matches[^1]];
            if (attribute.Ordinal >= 0)
                matches = attribute.Ordinal == 0 ? matches : [];
        }

        var isReturnSite = attribute.At is MixinAt.Return or MixinAt.Tail;
        var hasLocals = plan.Handler.GetParameters().Any(parameter =>
            parameter.IsDefined(typeof(LocalAttribute), false));
        if (isReturnSite && attribute.Shift is not AtShift.Before &&
            !(attribute.Shift == AtShift.By && attribute.By == 0) &&
            (attribute.Cancellable || hasLocals || plan.Handler.GetParameters().Any(parameter =>
                typeof(CallbackInfo).IsAssignableFrom(parameter.ParameterType) ||
                parameter.IsDefined(typeof(ReturnAttribute), false))))
            throw new MixinApplyException(
                $"RETURN/TAIL 的 CallbackInfo、Return 或 Local 绑定只能使用 Shift BEFORE（或 BY 0）：{Format(plan.Handler)}");

        var callInstructions = isReturnSite &&
                               (attribute.Shift == AtShift.Before || attribute.Shift == AtShift.By && attribute.By == 0) &&
                               !hasLocals
            ? null
            : BuildDirectInjectCall(original, plan.Handler);
        var inserted = 0;
        foreach (var originalIndex in matches)
        {
            var index = originalIndex + inserted;
            var injectionPoint = attribute.At == MixinAt.InvokeAssign ? index + 2 : index;
            var insertionIndex = attribute.Shift switch
            {
                AtShift.Before => injectionPoint,
                AtShift.After => injectionPoint + 1,
                AtShift.By => injectionPoint + attribute.By,
                _ => injectionPoint
            };
            if (insertionIndex < 0 || insertionIndex > instructions.Count)
                throw new MixinApplyException(
                    $"Inject Shift BY 超出方法边界：{Format(plan.Handler)} -> {attribute.By}");
            var emitted = callInstructions is null
                ? BuildReturnInjectCall(generator, original, plan)
                : callInstructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            if (insertionIndex < instructions.Count && emitted.Count > 0)
                MoveMetadata(instructions[insertionIndex], emitted[0]);
            instructions.InsertRange(insertionIndex, emitted);
            inserted += emitted.Count;
        }
        return matches.Count;
    }

    private static List<CodeInstruction> BuildReturnInjectCall(
        ILGenerator generator,
        MethodBase original,
        HandlerPlan plan)
    {
        var result = new List<CodeInstruction>();
        var returnType = MixinInvocation.GetReturnType(original);
        LocalBuilder? returnLocal = null;
        if (returnType != typeof(void))
        {
            returnLocal = generator.DeclareLocal(returnType);
            result.Add(new CodeInstruction(OpCodes.Stloc, returnLocal));
        }

        result.Add(new CodeInstruction(OpCodes.Ldc_I8, plan.Sequence));
        EmitTargetInstance(result, original);
        EmitTargetArguments(result, generator, original);
        if (returnType == typeof(void))
        {
            result.Add(new CodeInstruction(OpCodes.Call, typeof(MixinRuntimeDispatch).GetMethod(
                nameof(MixinRuntimeDispatch.ReturnVoid), BindingFlags.Static | BindingFlags.Public)!));
            return result;
        }

        result.Add(new CodeInstruction(OpCodes.Ldloc, returnLocal!));
        if (returnType.IsValueType) result.Add(new CodeInstruction(OpCodes.Box, returnType));
        result.Add(new CodeInstruction(OpCodes.Call, typeof(MixinRuntimeDispatch).GetMethod(
            nameof(MixinRuntimeDispatch.ReturnResult), BindingFlags.Static | BindingFlags.Public)!));
        result.Add(new CodeInstruction(returnType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, returnType));
        result.Add(new CodeInstruction(OpCodes.Stloc, returnLocal!));
        result.Add(new CodeInstruction(OpCodes.Ldloc, returnLocal!));
        return result;
    }

    private static void EmitTargetInstance(List<CodeInstruction> result, MethodBase original)
    {
        if (original.IsStatic)
        {
            result.Add(new CodeInstruction(OpCodes.Ldnull));
            return;
        }
        result.Add(new CodeInstruction(OpCodes.Ldarg_0));
        if (original.DeclaringType!.IsValueType)
            result.Add(new CodeInstruction(OpCodes.Box, original.DeclaringType));
    }

    private static void EmitTargetArguments(
        List<CodeInstruction> result,
        ILGenerator generator,
        MethodBase original)
    {
        var parameters = original.GetParameters();
        var arguments = generator.DeclareLocal(typeof(object[]));
        result.Add(Ldc(parameters.Length));
        result.Add(new CodeInstruction(OpCodes.Newarr, typeof(object)));
        result.Add(new CodeInstruction(OpCodes.Stloc, arguments));
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameterType = parameters[index].ParameterType;
            var valueType = parameterType.IsByRef ? parameterType.GetElementType()! : parameterType;
            result.Add(new CodeInstruction(OpCodes.Ldloc, arguments));
            result.Add(Ldc(index));
            result.Add(new CodeInstruction(OpCodes.Ldarg, index + (original.IsStatic ? 0 : 1)));
            if (parameterType.IsByRef) result.Add(new CodeInstruction(OpCodes.Ldobj, valueType));
            if (valueType.IsValueType) result.Add(new CodeInstruction(OpCodes.Box, valueType));
            result.Add(new CodeInstruction(OpCodes.Stelem_Ref));
        }
        result.Add(new CodeInstruction(OpCodes.Ldloc, arguments));
    }

    private static List<CodeInstruction> BuildDirectInjectCall(MethodBase original, MethodInfo handler)
    {
        var result = new List<CodeInstruction>();
        var targetParameters = original.GetParameters();
        foreach (var parameter in handler.GetParameters())
        {
            var arg = parameter.GetCustomAttribute<ArgAttribute>();
            var local = parameter.GetCustomAttribute<LocalAttribute>();
            var byRef = parameter.ParameterType.IsByRef;
            if (typeof(CallbackInfo).IsAssignableFrom(parameter.ParameterType))
                throw new MixinApplyException($"中间指令注入不能接收 CallbackInfo：{Format(handler)}");
            if (parameter.IsDefined(typeof(ThisAttribute), false) || parameter.Name is "__instance" or "instance")
            {
                if (original.IsStatic) throw new MixinApplyException($"静态目标没有 [This]：{Format(handler)}");
                result.Add(new CodeInstruction(byRef ? OpCodes.Ldarga : OpCodes.Ldarg, 0));
            }
            else if (arg is not null)
            {
                if ((uint)arg.Index >= (uint)targetParameters.Length)
                    throw new MixinApplyException($"Inject 参数索引越界：{Format(handler)} -> {arg.Index}");
                result.Add(new CodeInstruction(byRef ? OpCodes.Ldarga : OpCodes.Ldarg, arg.Index + (original.IsStatic ? 0 : 1)));
            }
            else if (local is not null)
            {
                result.Add(new CodeInstruction(byRef ? OpCodes.Ldloca : OpCodes.Ldloc, local.Index));
            }
            else
            {
                var targetIndex = Array.FindIndex(targetParameters, candidate => candidate.Name == parameter.Name);
                if (targetIndex < 0)
                    throw new MixinApplyException($"无法绑定中间 Inject 参数：{Format(handler)}.{parameter.Name}");
                result.Add(new CodeInstruction(byRef ? OpCodes.Ldarga : OpCodes.Ldarg, targetIndex + (original.IsStatic ? 0 : 1)));
            }
        }
        result.Add(new CodeInstruction(OpCodes.Call, handler));
        return result;
    }

    private static List<CodeInstruction> BuildModifyArg(
        ILGenerator generator,
        CallSignature signature,
        int argumentIndex,
        MethodInfo handler)
    {
        var stackTypes = signature.StackTypes;
        var locals = stackTypes.Select(generator.DeclareLocal).ToArray();
        var result = new List<CodeInstruction>();
        for (var index = stackTypes.Length - 1; index >= 0; index--)
            result.Add(new CodeInstruction(OpCodes.Stloc, locals[index]));
        for (var index = 0; index < stackTypes.Length; index++)
        {
            result.Add(new CodeInstruction(OpCodes.Ldloc, locals[index]));
            var callArgument = index - (signature.HasInstance ? 1 : 0);
            if (callArgument == argumentIndex) result.Add(new CodeInstruction(OpCodes.Call, handler));
        }
        return result;
    }

    private static List<CodeInstruction> BuildModifyArgs(
        ILGenerator generator,
        CallSignature signature,
        MethodInfo handler)
    {
        var stackTypes = signature.StackTypes;
        var stackLocals = stackTypes.Select(generator.DeclareLocal).ToArray();
        var argsLocal = generator.DeclareLocal(typeof(MixinArgs));
        var result = new List<CodeInstruction>();
        for (var index = stackTypes.Length - 1; index >= 0; index--)
            result.Add(new CodeInstruction(OpCodes.Stloc, stackLocals[index]));

        result.Add(Ldc(signature.ArgumentTypes.Length));
        result.Add(new CodeInstruction(OpCodes.Newarr, typeof(object)));
        for (var index = 0; index < signature.ArgumentTypes.Length; index++)
        {
            result.Add(new CodeInstruction(OpCodes.Dup));
            result.Add(Ldc(index));
            result.Add(new CodeInstruction(OpCodes.Ldloc, stackLocals[index + (signature.HasInstance ? 1 : 0)]));
            if (signature.ArgumentTypes[index].IsValueType)
                result.Add(new CodeInstruction(OpCodes.Box, signature.ArgumentTypes[index]));
            result.Add(new CodeInstruction(OpCodes.Stelem_Ref));
        }
        result.Add(new CodeInstruction(OpCodes.Newobj, MixinArgsConstructor));
        result.Add(new CodeInstruction(OpCodes.Stloc, argsLocal));
        result.Add(new CodeInstruction(OpCodes.Ldloc, argsLocal));
        result.Add(new CodeInstruction(OpCodes.Call, handler));

        if (signature.HasInstance) result.Add(new CodeInstruction(OpCodes.Ldloc, stackLocals[0]));
        for (var index = 0; index < signature.ArgumentTypes.Length; index++)
        {
            result.Add(new CodeInstruction(OpCodes.Ldloc, argsLocal));
            result.Add(Ldc(index));
            result.Add(new CodeInstruction(OpCodes.Callvirt, MixinArgsGet.MakeGenericMethod(signature.ArgumentTypes[index])));
        }
        return result;
    }

    private static CallSignature GetCallSignature(CodeInstruction instruction)
    {
        return instruction.operand switch
        {
            MethodInfo method => new CallSignature(
                instruction.opcode != OpCodes.Newobj && !method.IsStatic,
                method.DeclaringType,
                method.GetParameters().Select(parameter => parameter.ParameterType).ToArray()),
            ConstructorInfo constructor => new CallSignature(
                false,
                null,
                constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray()),
            _ => throw new MixinApplyException("调用指令缺少 MethodInfo/ConstructorInfo operand。")
        };
    }

    private static void ValidateRedirectSignature(CodeInstruction instruction, MethodInfo handler)
    {
        if (!handler.IsStatic) throw new MixinApplyException($"Redirect 处理器必须为 static：{Format(handler)}");
        Type[] input;
        Type output;
        switch (instruction.operand)
        {
            case MethodInfo method:
                input = (method.IsStatic ? [] : new[] { method.DeclaringType! })
                    .Concat(method.GetParameters().Select(parameter => parameter.ParameterType)).ToArray();
                output = method.ReturnType;
                break;
            case ConstructorInfo constructor:
                input = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                output = constructor.DeclaringType!;
                break;
            case FieldInfo field when instruction.opcode == OpCodes.Ldfld:
                input = [field.DeclaringType!]; output = field.FieldType; break;
            case FieldInfo field when instruction.opcode == OpCodes.Stfld:
                input = [field.DeclaringType!, field.FieldType]; output = typeof(void); break;
            case FieldInfo field when instruction.opcode == OpCodes.Ldsfld:
                input = []; output = field.FieldType; break;
            case FieldInfo field when instruction.opcode == OpCodes.Stsfld:
                input = [field.FieldType]; output = typeof(void); break;
            default:
                throw new MixinApplyException($"不支持重定向指令 {instruction.opcode}。");
        }

        var actual = handler.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        if (!actual.SequenceEqual(input) || handler.ReturnType != output)
            throw new MixinApplyException(
                $"Redirect 签名不匹配：{Format(handler)} 应为 ({string.Join(", ", input.Select(type => type.Name))}) -> {output.Name}");
    }

    private static void ValidateUnaryModifier(MethodInfo handler, Type? valueType)
    {
        if (!handler.IsStatic)
            throw new MixinApplyException($"Modify 处理器必须为 static：{Format(handler)}");
        var parameters = handler.GetParameters();
        if (parameters.Length != 1 || handler.ReturnType != parameters[0].ParameterType)
            throw new MixinApplyException($"Modify 处理器必须为 static T Handler(T value)：{Format(handler)}");
        if (valueType is not null && parameters[0].ParameterType != valueType)
            throw new MixinApplyException($"Modify 类型不匹配：{Format(handler)} 应处理 {valueType.FullName}。");
    }

    private static List<int> SelectMatches(
        IReadOnlyList<CodeInstruction> instructions,
        Func<CodeInstruction, int, bool> predicate,
        int ordinal,
        string? sliceFrom,
        string? sliceTo)
    {
        var (start, end) = ResolveSlice(instructions, sliceFrom, sliceTo);
        var all = new List<int>();
        for (var index = start; index <= end && index < instructions.Count; index++)
        {
            if (predicate(instructions[index], index)) all.Add(index);
        }
        if (ordinal < 0) return all;
        return ordinal < all.Count ? [all[ordinal]] : [];
    }

    private static (int Start, int End) ResolveSlice(
        IReadOnlyList<CodeInstruction> instructions,
        string? from,
        string? to)
    {
        var start = string.IsNullOrWhiteSpace(from) ? 0 : FindBoundary(instructions, from, first: true);
        var end = string.IsNullOrWhiteSpace(to) ? instructions.Count - 1 : FindBoundary(instructions, to, first: false);
        if (start < 0 || end < start)
            throw new MixinApplyException($"无效的 Slice：{from ?? "HEAD"} .. {to ?? "TAIL"}");
        return (start, end);
    }

    private static int FindBoundary(IReadOnlyList<CodeInstruction> instructions, string descriptor, bool first)
    {
        if (descriptor.Equals("HEAD", StringComparison.OrdinalIgnoreCase)) return 0;
        if (descriptor.Equals("TAIL", StringComparison.OrdinalIgnoreCase)) return instructions.Count - 1;
        var indexes = Enumerable.Range(0, instructions.Count).Where(index =>
            MixinTargetResolver.MemberMatches(instructions[index].operand, descriptor) ||
            (TryGetConstant(instructions[index], out var value) && MixinTargetResolver.ConstantMatches(value, descriptor)) ||
            (descriptor.Equals("RETURN", StringComparison.OrdinalIgnoreCase) && instructions[index].opcode == OpCodes.Ret));
        return first ? indexes.DefaultIfEmpty(-1).First() : indexes.DefaultIfEmpty(-1).Last();
    }

    private static void ValidateCount(MethodBase original, HandlerPlan plan, int count)
    {
        var operation = plan.Operation;
        var context =
            $"目标={original.DeclaringType?.FullName}.{original.Name}；" +
            $"处理器={plan.MixinType.FullName}.{plan.Handler.Name}；" +
            $"注入点={plan.InjectionPoint}；定位={plan.TargetDescriptor ?? "<method>"}";
        if (count < operation.Require)
            throw new MixinApplyException($"{plan.Kind} 约束失败：{context}；只匹配 {count} 处，Require={operation.Require}。");
        if (operation.Allow >= 0 && count > operation.Allow)
            throw new MixinApplyException($"{plan.Kind} 约束失败：{context}；匹配 {count} 处，超过 Allow={operation.Allow}。");
        if (operation.Expect >= 0 && count != operation.Expect)
            throw new MixinApplyException($"{plan.Kind} 约束失败：{context}；匹配 {count} 处，Expect={operation.Expect}。");
    }

    private static bool IsCall(CodeInstruction instruction)
        => instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Newobj;

    private static bool IsMemberInstruction(CodeInstruction instruction, MixinAt at)
        => at switch
        {
            MixinAt.Invoke or MixinAt.InvokeAssign => IsCall(instruction),
            MixinAt.New => instruction.opcode == OpCodes.Newobj,
            _ => instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Stfld ||
                 instruction.opcode == OpCodes.Ldsfld || instruction.opcode == OpCodes.Stsfld
        };

    private static bool IsAssignment(CodeInstruction instruction)
        => instruction.opcode.Name?.StartsWith("stloc", StringComparison.Ordinal) == true ||
           instruction.opcode == OpCodes.Stfld || instruction.opcode == OpCodes.Stsfld ||
           instruction.opcode == OpCodes.Starg || instruction.opcode == OpCodes.Starg_S;

    private static bool IsJump(CodeInstruction instruction)
        => instruction.opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch;

    private static bool OpcodeMatches(CodeInstruction instruction, int opcode)
        => opcode < 0 || instruction.opcode.Value == opcode;

    private static bool MatchesLocal(string? target, int index)
        => string.IsNullOrWhiteSpace(target) || target == index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool TryGetConstant(CodeInstruction instruction, out object? value)
    {
        var opcode = instruction.opcode;
        if (opcode == OpCodes.Ldnull) { value = null; return true; }
        if (opcode == OpCodes.Ldstr) { value = instruction.operand; return true; }
        if (opcode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (opcode == OpCodes.Ldc_I4_0) { value = 0; return true; }
        if (opcode == OpCodes.Ldc_I4_1) { value = 1; return true; }
        if (opcode == OpCodes.Ldc_I4_2) { value = 2; return true; }
        if (opcode == OpCodes.Ldc_I4_3) { value = 3; return true; }
        if (opcode == OpCodes.Ldc_I4_4) { value = 4; return true; }
        if (opcode == OpCodes.Ldc_I4_5) { value = 5; return true; }
        if (opcode == OpCodes.Ldc_I4_6) { value = 6; return true; }
        if (opcode == OpCodes.Ldc_I4_7) { value = 7; return true; }
        if (opcode == OpCodes.Ldc_I4_8) { value = 8; return true; }
        if (opcode == OpCodes.Ldc_I4 || opcode == OpCodes.Ldc_I4_S) { value = Convert.ToInt32(instruction.operand); return true; }
        if (opcode == OpCodes.Ldc_I8) { value = (long)instruction.operand; return true; }
        if (opcode == OpCodes.Ldc_R4) { value = (float)instruction.operand; return true; }
        if (opcode == OpCodes.Ldc_R8) { value = (double)instruction.operand; return true; }
        value = null;
        return false;
    }

    private static Type GetConstantType(CodeInstruction instruction, Type nullFallback)
    {
        if (instruction.opcode == OpCodes.Ldnull) return nullFallback;
        if (instruction.opcode == OpCodes.Ldstr) return typeof(string);
        if (instruction.opcode == OpCodes.Ldc_I8) return typeof(long);
        if (instruction.opcode == OpCodes.Ldc_R4) return typeof(float);
        if (instruction.opcode == OpCodes.Ldc_R8) return typeof(double);
        return typeof(int);
    }

    private static bool TryGetLocalIndex(CodeInstruction instruction, out int index, out bool isLoad, out bool isStore)
    {
        isLoad = instruction.opcode.Name?.StartsWith("ldloc", StringComparison.Ordinal) == true;
        isStore = instruction.opcode.Name?.StartsWith("stloc", StringComparison.Ordinal) == true;
        if (!isLoad && !isStore) { index = -1; return false; }
        if (instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Stloc_0) index = 0;
        else if (instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Stloc_1) index = 1;
        else if (instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Stloc_2) index = 2;
        else if (instruction.opcode == OpCodes.Ldloc_3 || instruction.opcode == OpCodes.Stloc_3) index = 3;
        else index = instruction.operand switch
        {
            LocalBuilder builder => builder.LocalIndex,
            LocalVariableInfo variable => variable.LocalIndex,
            byte value => value,
            int value => value,
            _ => -1
        };
        return index >= 0;
    }

    private static CodeInstruction Ldc(int value) => value switch
    {
        -1 => new CodeInstruction(OpCodes.Ldc_I4_M1),
        0 => new CodeInstruction(OpCodes.Ldc_I4_0),
        1 => new CodeInstruction(OpCodes.Ldc_I4_1),
        2 => new CodeInstruction(OpCodes.Ldc_I4_2),
        3 => new CodeInstruction(OpCodes.Ldc_I4_3),
        4 => new CodeInstruction(OpCodes.Ldc_I4_4),
        5 => new CodeInstruction(OpCodes.Ldc_I4_5),
        6 => new CodeInstruction(OpCodes.Ldc_I4_6),
        7 => new CodeInstruction(OpCodes.Ldc_I4_7),
        8 => new CodeInstruction(OpCodes.Ldc_I4_8),
        _ => new CodeInstruction(OpCodes.Ldc_I4, value)
    };

    private static void MoveMetadata(CodeInstruction source, CodeInstruction destination)
    {
        destination.labels.AddRange(source.labels);
        destination.blocks.AddRange(source.blocks);
        source.labels.Clear();
        source.blocks.Clear();
    }

    private static CodeInstruction TransferMetadata(CodeInstruction source, CodeInstruction destination)
    {
        MoveMetadata(source, destination);
        return destination;
    }

    private static string Format(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    private sealed record CallSignature(bool HasInstance, Type? InstanceType, Type[] ArgumentTypes)
    {
        public Type[] StackTypes => HasInstance ? [InstanceType!, .. ArgumentTypes] : ArgumentTypes;
    }
}
