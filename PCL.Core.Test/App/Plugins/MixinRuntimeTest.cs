using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Mixin;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
[DoNotParallelize]
public class MixinRuntimeTest
{
    [TestMethod]
    public void BoundaryInject_ShouldCancelAndModifyReturnValue()
    {
        using var runtime = ApplyAll();

        Assert.AreEqual(7, BoundaryTarget.Compute(3));
        Assert.AreEqual(41, BoundaryTarget.Compute(-1));
        Assert.AreEqual(6, ReturnBindingTarget.Compute(3));
    }

    [TestMethod]
    public void Overwrite_ShouldReplaceOriginalBody()
    {
        using var runtime = ApplyAll();

        Assert.AreEqual(15, OverwriteTarget.Compute(5));
    }

    [TestMethod]
    public void RedirectAndModifyConstant_ShouldRewriteInstructions()
    {
        using var runtime = ApplyAll();

        Assert.AreEqual(-4, RedirectTarget.Run(-4));
        Assert.AreEqual(20, ConstantTarget.GetLimit());
    }

    [TestMethod]
    public void ModifyArgAndModifyArgs_ShouldRewriteCallOperands()
    {
        using var runtime = ApplyAll();

        Assert.AreEqual(8, ArgumentTarget.RunSingle(3));
        Assert.AreEqual(23, ArgumentTarget.RunAll(8, 9));
    }

    [TestMethod]
    public void FieldRedirectAndInvokeInject_ShouldPatchMidMethodInstructions()
    {
        using var runtime = ApplyAll();
        var target = new InstructionTarget { Value = 5 };
        InstructionTarget.Seen = 0;

        Assert.AreEqual(10, target.Read());
        Assert.AreEqual(6, InstructionTarget.Call(6));
        Assert.AreEqual(6, InstructionTarget.Seen);
    }

    [TestMethod]
    public void ModifyVariable_ShouldPatchLocalLoad()
    {
        using var runtime = ApplyAll();

        Assert.AreEqual(28, VariableTarget.Run(3));
    }

    [TestMethod]
    public void AccessorAndInvoker_ShouldReachPrivateMembers()
    {
        using var runtime = ApplyAll();
        var target = new AccessorTarget(2);
        var accessor = MixinAccessors.Create<IAccessorTarget>(target);

        Assert.AreEqual(2, accessor.Value);
        accessor.Value = 7;
        Assert.AreEqual(21, accessor.InvokeMultiply(3));

        var finalTarget = new FinalAccessorTarget(4);
        var readOnly = MixinAccessors.Create<IReadOnlyFinalAccessor>(finalTarget);
        Assert.Throws<MixinApplyException>(() => readOnly.Value = 8);
        var mutable = MixinAccessors.Create<IMutableFinalAccessor>(finalTarget);
        mutable.Value = 9;
        Assert.AreEqual(9, mutable.Value);
    }

    [TestMethod]
    public void AccessorProxy_ShouldSupportCollectibleInterfaces()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"PCL.Mixin.CollectibleAccessorTest.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("main");

        var targetBuilder = module.DefineType(
            "CollectibleAccessorTarget",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        var valueField = targetBuilder.DefineField("_value", typeof(int), FieldAttributes.Private);
        var targetConstructor = targetBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(int)]);
        var constructorIl = targetConstructor.GetILGenerator();
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Ldarg_1);
        constructorIl.Emit(OpCodes.Stfld, valueField);
        constructorIl.Emit(OpCodes.Ret);
        var multiply = targetBuilder.DefineMethod(
            "Multiply",
            MethodAttributes.Private,
            typeof(int),
            [typeof(int)]);
        var multiplyIl = multiply.GetILGenerator();
        multiplyIl.Emit(OpCodes.Ldarg_0);
        multiplyIl.Emit(OpCodes.Ldfld, valueField);
        multiplyIl.Emit(OpCodes.Ldarg_1);
        multiplyIl.Emit(OpCodes.Mul);
        multiplyIl.Emit(OpCodes.Ret);
        var targetType = targetBuilder.CreateType()!;

        var accessorBuilder = module.DefineType(
            "ICollectibleAccessor",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        accessorBuilder.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(MixinAttribute).GetConstructor([typeof(string)])!,
            [targetType.FullName!]));
        var getValue = DefineAccessorMethod(accessorBuilder, "GetValue", typeof(int), [], "_value");
        var setValue = DefineAccessorMethod(accessorBuilder, "SetValue", typeof(void), [typeof(int)], "_value");
        var invokeMultiply = accessorBuilder.DefineMethod(
            "InvokeMultiply",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.NewSlot | MethodAttributes.HideBySig,
            typeof(int),
            [typeof(int)]);
        invokeMultiply.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(InvokerAttribute).GetConstructor([typeof(string)])!,
            ["Multiply"]));
        var accessorType = accessorBuilder.CreateType()!;

        Assert.IsTrue(accessorType.Assembly.IsCollectible);
        var target = Activator.CreateInstance(targetType, [2]);
        var proxy = typeof(MixinAccessors).GetMethod(nameof(MixinAccessors.Create))!
            .MakeGenericMethod(accessorType)
            .Invoke(null, [target]);

        Assert.IsNotNull(proxy);
        Assert.IsTrue(proxy.GetType().Assembly.IsCollectible);
        Assert.AreEqual(2, accessorType.GetMethod(getValue.Name)!.Invoke(proxy, null));
        accessorType.GetMethod(setValue.Name)!.Invoke(proxy, [7]);
        Assert.AreEqual(21, accessorType.GetMethod(invokeMultiply.Name)!.Invoke(proxy, [3]));
    }

    [TestMethod]
    public void ApplyAssembly_ShouldReportMixinTargets()
    {
        using var runtime = new MixinRuntime();
        var result = runtime.ApplyAssembly(typeof(MixinRuntimeTest).Assembly);

        Assert.IsTrue(result.MixinCount >= 13);
        Assert.IsTrue(result.TargetMethodCount >= 13);
        Assert.IsTrue(runtime.Patches.Count >= 14);
    }

    [TestMethod]
    public void InternalRollback_ShouldRestoreOriginalMethods()
    {
        using var runtime = ApplyAll();
        Assert.AreEqual(7, BoundaryTarget.Compute(3));

        runtime.RollbackAssembly(typeof(MixinRuntimeTest).Assembly);

        Assert.AreEqual(6, BoundaryTarget.Compute(3));
        Assert.AreEqual(0, runtime.Patches.Count);
    }

    [TestMethod]
    public void NewJumpAndInvokeAssign_ShouldLocateTypedInjectionPoints()
    {
        using var runtime = ApplyAll();
        AdvancedAtTarget.NewSeen = 0;
        AdvancedAtTarget.AssignSeen = 0;
        AdvancedAtTarget.JumpSeen = 0;

        Assert.AreEqual(16, AdvancedAtTarget.CreateAndMeasure());
        Assert.AreEqual(1, AdvancedAtTarget.NewSeen);
        Assert.AreEqual(4, AdvancedAtTarget.Assign(4));
        Assert.AreEqual(1, AdvancedAtTarget.AssignSeen);
        Assert.AreEqual(1, AdvancedAtTarget.Branch(1));
        Assert.IsTrue(AdvancedAtTarget.JumpSeen > 0);
    }

    [TestMethod]
    public void TailConstantStoreSliceOrdinalAndLocalCapture_ShouldWork()
    {
        using var runtime = ApplyAll();
        ExtendedAtTarget.ConstantSeen = 0;
        ExtendedAtTarget.SliceSeen = 0;
        ExtendedAtTarget.CapturedLocal = 0;

        Assert.AreEqual(8, ExtendedAtTarget.Tail(3));
        Assert.AreEqual(10, ExtendedAtTarget.Constant());
        Assert.AreEqual(1, ExtendedAtTarget.ConstantSeen);
        Assert.AreEqual(9, ExtendedAtTarget.Store(3));
        Assert.AreEqual(3, ExtendedAtTarget.TwoCalls());
        Assert.AreEqual(1, ExtendedAtTarget.SliceSeen);
        Assert.AreEqual(8, ExtendedAtTarget.Capture(4));
        Assert.AreEqual(8, ExtendedAtTarget.CapturedLocal);
    }

    [TestMethod]
    public void Configuration_ShouldSelectMixinsAndCallDedicatedProcessor()
    {
        ConfigurationProcessor.PreApplyCount = 0;
        ConfigurationProcessor.PostApplyCount = 0;
        using var runtime = new MixinRuntime();
        var configuration = new MixinConfiguration
        {
            Required = true,
            Mixins = [typeof(ConfigurationMixin).FullName!],
            Priority = 1234,
            Plugin = typeof(ConfigurationProcessor).FullName,
            Injectors = new MixinInjectorConfiguration { DefaultRequire = 1 }
        };

        var result = runtime.ApplyConfiguration(typeof(MixinRuntimeTest).Assembly, configuration, "test.mixins.json");

        Assert.AreEqual(1, result.MixinCount);
        Assert.AreEqual(1, ConfigurationProcessor.PreApplyCount);
        Assert.AreEqual(1, ConfigurationProcessor.PostApplyCount);
        Assert.AreEqual(9, ConfigurationTarget.Run(8));
    }

    [TestMethod]
    public void PriorityAndConflictDiagnostics_ShouldShowApplicationOrder()
    {
        ConflictTarget.Order.Clear();
        using var runtime = ApplyAll();

        ConflictTarget.Run();

        CollectionAssert.AreEqual(new[] { "high", "low" }, ConflictTarget.Order);
        var conflict = runtime.Conflicts.Single(item => item.TargetMethod.Name == nameof(ConflictTarget.Run));
        Assert.IsTrue(conflict.ApplicationOrder[0].Priority > conflict.ApplicationOrder[1].Priority);
        Assert.AreEqual("HEAD", conflict.ApplicationOrder[0].InjectionPoint);

        var invokeDiagnostic = runtime.Patches.Single(patch =>
            patch.MixinType.Name == nameof(InvokeInjectMixin) && patch.Handler.Name == "BeforeEcho");
        Assert.AreEqual(typeof(InstructionTarget), invokeDiagnostic.TargetMethod.DeclaringType);
        Assert.AreEqual(nameof(InstructionTarget.Call), invokeDiagnostic.TargetMethod.Name);
        Assert.AreEqual("INVOKE", invokeDiagnostic.InjectionPoint);
        StringAssert.Contains(invokeDiagnostic.TargetDescriptor!, "InstructionTarget::Echo");
    }

    [TestMethod]
    public void PublicSurface_ShouldNotExposeHarmonyOrLegacyLifecycleApis()
    {
        var coreAssembly = typeof(MixinAttribute).Assembly;
        Assert.AreEqual("PCL.Core", coreAssembly.GetName().Name);
        Assert.IsFalse(coreAssembly.GetExportedTypes().Any(type =>
            type.Namespace == "PCL.Mixin" && type.Name is "MixinRuntime" or "IMixinRuntime" or "PatchDescriptor"));
        Assert.IsFalse(coreAssembly.GetExportedTypes()
            .Where(type => type.Namespace == "PCL.Mixin")
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Any(method => method.Name is "PatchRaw" or "LoadAsync" or "UnloadAsync" ||
                           method.GetParameters().Any(parameter =>
                               parameter.ParameterType.Namespace?.StartsWith("Harmony", StringComparison.Ordinal) == true)));

        var references = coreAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        CollectionAssert.DoesNotContain(references, "PCL.Plugin.Abstractions");
        CollectionAssert.DoesNotContain(references, "Jint");
        CollectionAssert.DoesNotContain(references, "Acornima");
    }

    [TestMethod]
    public void ShadowFinalMutableUniqueAndPublicMarkers_ShouldBeAvailable()
    {
        using var runtime = new MixinRuntime();
        var result = runtime.ApplyAssembly(typeof(MixinRuntimeTest).Assembly);

        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Unique", StringComparison.Ordinal)));
        Assert.IsNotNull(typeof(FinalAttribute));
        Assert.IsNotNull(typeof(MutableAttribute));
        Assert.IsNotNull(typeof(IntrinsicAttribute));
        Assert.IsNotNull(typeof(SliceAttribute));
        Assert.IsNotNull(typeof(PCL.Mixin.PriorityAttribute));
        Assert.IsNotNull(typeof(RequireAttribute));
        Assert.IsNotNull(typeof(ExpectAttribute));
        Assert.IsNotNull(typeof(AllowAttribute));
        Assert.IsNotNull(typeof(CancellableAttribute));
        Assert.IsNotNull(typeof(OrdinalAttribute));
        Assert.AreEqual(4, IntrinsicTarget.Run());
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Intrinsic 保留目标实现", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Shadow_ShouldBindHandlerFieldAndMethodAccessToTargetInstance()
    {
        ShadowMixin.SeenFinal = 0;
        ShadowMixin.SeenMethod = 0;
        using var runtime = ApplyAll();
        var target = new ShadowTarget();

        Assert.AreEqual(5, target.Apply(2));
        Assert.AreEqual(1, ShadowMixin.SeenFinal);
        Assert.AreEqual(3, ShadowMixin.SeenMethod);
    }

    [TestMethod]
    public void FinalShadow_ShouldRequireMutableBeforeWritingReadonlyTarget()
    {
        using var runtime = ApplyAll();
        var forbidden = new FinalShadowTarget(4);
        var exception = Assert.Throws<MixinApplyException>(() => forbidden.Forbidden());
        StringAssert.Contains(exception.Message, "[Mutable]");

        var mutable = new MutableShadowTarget(4);
        Assert.AreEqual(9, mutable.Change());
    }

    [TestMethod]
    public void ReturnAndTail_ShouldUseActualReturnSitesOrdinalsAndCounts()
    {
        var (assembly, target) = BuildMultipleReturnMixinAssembly();
        using var runtime = new MixinRuntime();
        runtime.ApplyAssembly(assembly);
        var choose = target.GetMethod("Choose")!;

        Assert.AreEqual(10, choose.Invoke(null, [1]));
        Assert.AreEqual(1, target.GetField("All")!.GetValue(null));
        Assert.AreEqual(1, target.GetField("First")!.GetValue(null));
        Assert.AreEqual(0, target.GetField("Second")!.GetValue(null));
        Assert.AreEqual(0, target.GetField("Tail")!.GetValue(null));

        Assert.AreEqual(20, choose.Invoke(null, [0]));
        Assert.AreEqual(2, target.GetField("All")!.GetValue(null));
        Assert.AreEqual(1, target.GetField("First")!.GetValue(null));
        Assert.AreEqual(1, target.GetField("Second")!.GetValue(null));
        Assert.AreEqual(1, target.GetField("Tail")!.GetValue(null));
    }

    [TestMethod]
    public void SliceAndShift_ShouldPlaceCallsBeforeAfterAndBySelectedInstruction()
    {
        using var runtime = ApplyAll();

        ShiftTarget.Events.Clear();
        ShiftTarget.Before();
        CollectionAssert.AreEqual(new[] { 1, 98, 2, 3 }, ShiftTarget.Events);

        ShiftTarget.Events.Clear();
        ShiftTarget.After();
        CollectionAssert.AreEqual(new[] { 1, 2, 99, 3 }, ShiftTarget.Events);

        ShiftTarget.Events.Clear();
        ShiftTarget.By();
        CollectionAssert.AreEqual(new[] { 1, 2, 97, 3 }, ShiftTarget.Events);
    }

    [TestMethod]
    public void OptionalMixinFailureAndRequireExpectAllow_ShouldNotBlockOtherMixins()
    {
        using var runtime = new MixinRuntime();
        var result = runtime.ApplyAssembly(typeof(MixinRuntimeTest).Assembly);

        Assert.AreEqual(7, BoundaryTarget.Compute(3));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Require=2", StringComparison.Ordinal)));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Allow=0", StringComparison.Ordinal)));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Expect=2", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RequiredConfiguration_ShouldFailClearlyWhenMixinClassIsMissing()
    {
        using var runtime = new MixinRuntime();
        var configuration = new MixinConfiguration
        {
            Required = true,
            Package = "Missing",
            Mixins = ["NoSuchMixin"]
        };

        var exception = Assert.Throws<MixinApplyException>(() =>
            runtime.ApplyConfiguration(typeof(MixinRuntimeTest).Assembly, configuration, "required.mixins.json"));
        StringAssert.Contains(exception.Message, "required.mixins.json");
        StringAssert.Contains(exception.Message, "Missing.NoSuchMixin");
    }

    [TestMethod]
    public void LocalCaptureModes_ShouldRejectSkipOrFailAsDeclared()
    {
        var target = typeof(ExtendedAtTarget).GetMethod(nameof(ExtendedAtTarget.Capture))!;
        var invalidHandler = typeof(LocalCaptureValidationHandlers).GetMethod(
            "Invalid",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var validHandler = typeof(LocalCaptureValidationHandlers).GetMethod(
            "Valid",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var noCapture = Assert.Throws<MixinApplyException>(() =>
            MixinRuntime.ValidateLocalCapture(
                target,
                invalidHandler,
                new InjectAttribute(nameof(ExtendedAtTarget.Capture)) { At = MixinAt.Return },
                []));
        StringAssert.Contains(noCapture.Message, "NoCapture");

        var warnings = new List<string>();
        Assert.IsFalse(MixinRuntime.ValidateLocalCapture(
            target,
            invalidHandler,
            new InjectAttribute(nameof(ExtendedAtTarget.Capture))
            {
                At = MixinAt.Return,
                Locals = LocalCapture.FailSoft
            },
            warnings));
        StringAssert.Contains(warnings.Single(), "已跳过该注入");

        var failHard = Assert.Throws<MixinApplyException>(() =>
            MixinRuntime.ValidateLocalCapture(
                target,
                invalidHandler,
                new InjectAttribute(nameof(ExtendedAtTarget.Capture))
                {
                    At = MixinAt.Return,
                    Locals = LocalCapture.FailHard
                },
                []));
        StringAssert.Contains(failHard.Message, "局部变量索引 99 不存在");

        Assert.IsTrue(MixinRuntime.ValidateLocalCapture(
            target,
            validHandler,
            new InjectAttribute(nameof(ExtendedAtTarget.Capture))
            {
                At = MixinAt.Return,
                Locals = LocalCapture.FailHard
            },
            []));
    }

    [TestMethod]
    public void SafeMode_ShouldSkipEveryThirdPartyPclxMixin()
    {
        var original = PluginLoaderService.SafeMode;
        try
        {
            PluginLoaderService.SafeMode = true;
            Assert.IsTrue(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest
            {
                Id = "third.party.one",
                Name = "One"
            }));
            Assert.IsTrue(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest
            {
                Id = "third.party.two",
                Name = "Two"
            }));

            PluginLoaderService.SafeMode = false;
            Assert.IsFalse(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest
            {
                Id = "third.party.three",
                Name = "Three"
            }));
        }
        finally
        {
            PluginLoaderService.SafeMode = original;
        }
    }

    private static MixinRuntime ApplyAll()
    {
        var runtime = new MixinRuntime();
        runtime.ApplyAssembly(typeof(MixinRuntimeTest).Assembly);
        return runtime;
    }

    private static MethodBuilder DefineAccessorMethod(
        TypeBuilder accessorBuilder,
        string name,
        Type returnType,
        Type[] parameterTypes,
        string targetName)
    {
        var method = accessorBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.NewSlot | MethodAttributes.HideBySig,
            returnType,
            parameterTypes);
        method.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AccessorAttribute).GetConstructor([typeof(string)])!,
            [targetName]));
        return method;
    }

    private static (Assembly Assembly, Type Target) BuildMultipleReturnMixinAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PCL.Mixin.MultipleReturns." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var targetBuilder = module.DefineType(
            "MultipleReturnTarget",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var counters = new Dictionary<string, FieldBuilder>();
        foreach (var name in new[] { "All", "First", "Second", "Tail" })
            counters[name] = targetBuilder.DefineField(name, typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var choose = targetBuilder.DefineMethod(
            "Choose",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var chooseIl = choose.GetILGenerator();
        var second = chooseIl.DefineLabel();
        chooseIl.Emit(OpCodes.Ldarg_0);
        chooseIl.Emit(OpCodes.Brfalse_S, second);
        chooseIl.Emit(OpCodes.Ldc_I4_S, 10);
        chooseIl.Emit(OpCodes.Ret);
        chooseIl.MarkLabel(second);
        chooseIl.Emit(OpCodes.Ldc_I4_S, 20);
        chooseIl.Emit(OpCodes.Ret);
        var target = targetBuilder.CreateType()!;

        var mixinBuilder = module.DefineType(
            "MultipleReturnMixin",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        mixinBuilder.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(MixinAttribute).GetConstructor([typeof(string)])!,
            [target.FullName!]));
        DefineReturnHandler(mixinBuilder, target, "All", MixinAt.Return, -1, require: 2, expect: 2, allow: 2);
        DefineReturnHandler(mixinBuilder, target, "First", MixinAt.Return, 0, require: 1, expect: 1, allow: 1);
        DefineReturnHandler(mixinBuilder, target, "Second", MixinAt.Return, 1, require: 1, expect: 1, allow: 1);
        DefineReturnHandler(mixinBuilder, target, "Tail", MixinAt.Tail, 0, require: 1, expect: 1, allow: 1);
        mixinBuilder.CreateType();
        return (assembly, target);
    }

    private static void DefineReturnHandler(
        TypeBuilder mixin,
        Type target,
        string counter,
        MixinAt at,
        int ordinal,
        int require,
        int expect,
        int allow)
    {
        var handler = mixin.DefineMethod(
            "On" + counter,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        var field = target.GetField(counter)!;
        var il = handler.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);
        handler.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(InjectAttribute).GetConstructor([typeof(string)])!,
            ["Choose"],
            [
                typeof(InjectAttribute).GetProperty(nameof(InjectAttribute.At))!,
                typeof(InjectAttribute).GetProperty(nameof(InjectAttribute.Ordinal))!,
                typeof(InjectAttribute).GetProperty(nameof(InjectAttribute.Require))!,
                typeof(InjectAttribute).GetProperty(nameof(InjectAttribute.Expect))!,
                typeof(InjectAttribute).GetProperty(nameof(InjectAttribute.Allow))!
            ],
            [at, ordinal, require, expect, allow]));
    }

    public static class BoundaryTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int value) => value * 2;
    }

    [Mixin(typeof(BoundaryTarget))]
    private static class BoundaryMixin
    {
        [Inject(nameof(BoundaryTarget.Compute), At = MixinAt.Head, Cancellable = true)]
        private static void Head(int value, CallbackInfo<int> callback)
        {
            if (value < 0) callback.SetReturnValue(40);
        }

        [Inject(nameof(BoundaryTarget.Compute), At = MixinAt.Return, Cancellable = true)]
        private static void Return(CallbackInfo<int> callback) => callback.ReturnValue++;
    }

    public static class ReturnBindingTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int value) => value + 1;
    }

    [Mixin(typeof(ReturnBindingTarget))]
    private static class ReturnBindingMixin
    {
        [Inject(nameof(ReturnBindingTarget.Compute), At = MixinAt.Return)]
        private static void Return([Return] ref int result) => result += 2;
    }

    public static class OverwriteTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int value) => value - 100;
    }

    [Mixin(typeof(OverwriteTarget))]
    private static class OverwriteMixin
    {
        [Overwrite(nameof(OverwriteTarget.Compute))]
        private static int Compute(int value) => value * 3;
    }

    public static class RedirectTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int value) => Math.Abs(value);
    }

    [Mixin(typeof(RedirectTarget))]
    private static class RedirectMixin
    {
        [Redirect(nameof(RedirectTarget.Run), Target = "System.Math::Abs(System.Int32)")]
        private static int KeepSign(int value) => value;
    }

    public static class ConstantTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GetLimit() => 10;
    }

    [Mixin(typeof(ConstantTarget))]
    private static class ConstantMixin
    {
        [ModifyConstant(nameof(ConstantTarget.GetLimit), Target = "int:10")]
        private static int ChangeLimit(int value) => value * 2;
    }

    public static class ArgumentTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int RunSingle(int value) => Twice(value);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int RunAll(int left, int right) => Combine(left, right);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Twice(int value) => value * 2;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Combine(int left, int right) => left * 10 + right;
    }

    [Mixin(typeof(ArgumentTarget))]
    private static class ArgumentMixin
    {
        [ModifyArg(nameof(ArgumentTarget.RunSingle),
            Target = "PCL.Core.Test.App.Plugins.MixinRuntimeTest+ArgumentTarget::Twice(System.Int32)", Index = 0)]
        private static int Increment(int value) => value + 1;

        [ModifyArgs(nameof(ArgumentTarget.RunAll),
            Target = "PCL.Core.Test.App.Plugins.MixinRuntimeTest+ArgumentTarget::Combine(System.Int32,System.Int32)")]
        private static void Replace(MixinArgs args)
        {
            args.Set(0, 2);
            args.Set(1, 3);
        }
    }

    public sealed class InstructionTarget
    {
        public int Value;
        public static int Seen;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read() => Value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Call(int value) => Echo(value);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Echo(int value) => value;
    }

    [Mixin(typeof(InstructionTarget))]
    private static class FieldRedirectMixin
    {
        [Redirect(nameof(InstructionTarget.Read), At = MixinAt.Field,
            Target = "PCL.Core.Test.App.Plugins.MixinRuntimeTest+InstructionTarget::Value")]
        private static int DoubleRead(InstructionTarget instance) => instance.Value * 2;
    }

    [Mixin(typeof(InstructionTarget))]
    private static class InvokeInjectMixin
    {
        [Inject(nameof(InstructionTarget.Call), At = MixinAt.Invoke,
            Target = "PCL.Core.Test.App.Plugins.MixinRuntimeTest+InstructionTarget::Echo(System.Int32)")]
        private static void BeforeEcho([Arg(0)] int value) => InstructionTarget.Seen = value;
    }

    public static class VariableTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int Run(int value)
        {
            var local = value + 1;
            return local * 2;
        }
    }

    [Mixin(typeof(VariableTarget))]
    private static class VariableMixin
    {
        [ModifyVariable(nameof(VariableTarget.Run), At = MixinAt.Load, Index = 0, Ordinal = 0)]
        private static int AddTen(int value) => value + 10;
    }

    public static class AdvancedAtTarget
    {
        public static int NewSeen;
        public static int AssignSeen;
        public static int JumpSeen;

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int CreateAndMeasure()
        {
            var builder = new System.Text.StringBuilder();
            return builder.Capacity;
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int Assign(int value)
        {
            var result = Echo(value);
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int Branch(int value)
        {
            if (value > 0) return 1;
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Echo(int value) => value;
    }

    [Mixin(typeof(AdvancedAtTarget))]
    private static class AdvancedAtMixin
    {
        [Inject(nameof(AdvancedAtTarget.CreateAndMeasure), At = MixinAt.New,
            Target = "System.Text.StringBuilder::.ctor()")]
        private static void OnNew() => AdvancedAtTarget.NewSeen++;

        [Inject(nameof(AdvancedAtTarget.Assign), At = MixinAt.InvokeAssign,
            Target = "PCL.Core.Test.App.Plugins.MixinRuntimeTest+AdvancedAtTarget::Echo(System.Int32)")]
        private static void OnAssigned() => AdvancedAtTarget.AssignSeen++;

        [Inject(nameof(AdvancedAtTarget.Branch), At = MixinAt.Jump, Ordinal = 0)]
        private static void OnJump() => AdvancedAtTarget.JumpSeen++;
    }

    public static class ExtendedAtTarget
    {
        public static int ConstantSeen;
        public static int SliceSeen;
        public static int CapturedLocal;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Tail(int value) => value;

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int Constant() => 10;

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int Store(int value)
        {
            var local = value;
            return local + 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int TwoCalls() => Echo(1) + Echo(2);

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static int Capture(int value)
        {
            var local = value * 2;
            return local;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Echo(int value) => value;
    }

    [Mixin(typeof(ExtendedAtTarget))]
    private static class ExtendedAtMixin
    {
        [Inject(nameof(ExtendedAtTarget.Tail), At = MixinAt.Tail, Cancellable = true)]
        private static void Tail(CallbackInfo<int> callback) => callback.ReturnValue += 5;

        [Inject(nameof(ExtendedAtTarget.Constant), At = MixinAt.Constant, Target = "int:10")]
        private static void Constant() => ExtendedAtTarget.ConstantSeen++;

        [ModifyVariable(nameof(ExtendedAtTarget.Store), At = MixinAt.Store, Index = 0, Ordinal = 0)]
        private static int Store(int value) => value + 5;

        [Slice("second", From = "int:2", To = "TAIL")]
        [Inject(nameof(ExtendedAtTarget.TwoCalls), At = MixinAt.Invoke,
            Target = "PCL.Core.Test.App.Plugins.MixinRuntimeTest+ExtendedAtTarget::Echo(System.Int32)",
            Slice = "second", Ordinal = 0)]
        private static void Slice() => ExtendedAtTarget.SliceSeen++;

        [Inject(nameof(ExtendedAtTarget.Capture), At = MixinAt.Return, Locals = LocalCapture.FailHard)]
        private static void Capture([Local(0)] int local) => ExtendedAtTarget.CapturedLocal = local;
    }

    private static class LocalCaptureValidationHandlers
    {
        private static void Invalid([Local(99)] int local) { }
        private static void Valid([Local(0)] int local) { }
    }

    [Mixin(typeof(ExtendedAtTarget))]
    private static class FailSoftLocalCaptureMixin
    {
        [Inject(nameof(ExtendedAtTarget.Capture), At = MixinAt.Return, Locals = LocalCapture.FailSoft)]
        private static void Capture([Local(99)] int local) => Assert.Fail("FailSoft 注入不应执行。");
    }

    public static class ConfigurationTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int value) => value;
    }

    [Mixin(typeof(ConfigurationTarget))]
    private static class ConfigurationMixin
    {
        [Inject(nameof(ConfigurationTarget.Run), At = MixinAt.Return, Cancellable = true)]
        private static void Increment(CallbackInfo<int> callback) => callback.ReturnValue++;
    }

    private sealed class ConfigurationProcessor : IMixinConfigPlugin
    {
        public static int PreApplyCount;
        public static int PostApplyCount;

        public void PreApply(MixinApplyContext context) => PreApplyCount++;
        public void PostApply(MixinApplyContext context) => PostApplyCount++;
    }

    public static class ConflictTarget
    {
        public static List<string> Order { get; } = [];

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run() { }
    }

    [Mixin(typeof(ConflictTarget), Priority = 2000)]
    private static class HighPriorityMixin
    {
        [Inject(nameof(ConflictTarget.Run))]
        private static void Head() => ConflictTarget.Order.Add("high");
    }

    [Mixin(typeof(ConflictTarget), Priority = 1000)]
    private static class LowPriorityMixin
    {
        [Inject(nameof(ConflictTarget.Run))]
        private static void Head() => ConflictTarget.Order.Add("low");
    }

    public sealed class ShadowTarget
    {
        private readonly int _fixed = 1;
        private int _value;
        public ShadowTarget() => _value = 0;
        public int Read() => _fixed + _value;
        private int IsolatedHelper() => 99;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Apply(int increment)
        {
            _value += increment;
            return Read();
        }
        private int Helper(int multiplier) => _fixed * multiplier;
    }

    [Mixin(typeof(ShadowTarget))]
    private sealed class ShadowMixin
    {
        public static int SeenFinal;
        public static int SeenMethod;

#pragma warning disable CS0169
        [Shadow("_fixed"), Final]
        private int Fixed;

        [Shadow("_value")]
        private int Value;
#pragma warning restore CS0169

        [Shadow("Helper")]
        private int Helper(int multiplier) => throw new NotSupportedException();

        [Inject(nameof(ShadowTarget.Apply), At = MixinAt.Head)]
        private void Before([Arg(0)] int increment)
        {
            SeenFinal = Fixed;
            SeenMethod = Helper(3);
            Value += increment;
        }

        [Unique]
        private static int IsolatedHelper() => 2;
    }

    public sealed class FinalShadowTarget
    {
        private readonly int _value;
        public FinalShadowTarget(int value) => _value = value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Forbidden() => _value;
    }

    [Mixin(typeof(FinalShadowTarget))]
    private sealed class FinalShadowMixin
    {
        [Shadow("_value"), Final]
        private int Value;

        [Inject(nameof(FinalShadowTarget.Forbidden), At = MixinAt.Head)]
        private void Write() => Value++;
    }

    public sealed class MutableShadowTarget
    {
        private readonly int _value;
        public MutableShadowTarget(int value) => _value = value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Change() => _value;
    }

    [Mixin(typeof(MutableShadowTarget))]
    private sealed class MutableShadowMixin
    {
        [Shadow("_value"), Final, Mutable]
        private int Value;

        [Inject(nameof(MutableShadowTarget.Change), At = MixinAt.Head)]
        private void Write() => Value = 9;
    }

    public static class ShiftTarget
    {
        public static List<int> Events { get; } = [];

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Before()
        {
            Mark(1);
            Mark(2);
            Mark(3);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void After()
        {
            Mark(1);
            Mark(2);
            Mark(3);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void By()
        {
            Mark(1);
            Mark(2);
            Mark(3);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Mark(int value) => Events.Add(value);
    }

    [Mixin(typeof(ShiftTarget))]
    private static class ShiftMixin
    {
        private const string MarkTarget =
            "PCL.Core.Test.App.Plugins.MixinRuntimeTest+ShiftTarget::Mark(System.Int32)";

        [Inject(nameof(ShiftTarget.Before), At = MixinAt.Invoke, Target = MarkTarget,
            Ordinal = 1, Shift = AtShift.Before)]
        private static void Before() => ShiftTarget.Events.Add(98);

        [Slice("middle", From = "int:2", To = "int:3")]
        [Inject(nameof(ShiftTarget.After), At = MixinAt.Invoke, Target = MarkTarget,
            Slice = "middle", Ordinal = 0, Shift = AtShift.After)]
        private static void After() => ShiftTarget.Events.Add(99);

        [Slice("middle", From = "int:2", To = "int:3")]
        [Inject(nameof(ShiftTarget.By), At = MixinAt.Invoke, Target = MarkTarget,
            Slice = "middle", Ordinal = 0, Shift = AtShift.By, By = 2)]
        private static void By() => ShiftTarget.Events.Add(97);
    }

    public static class IntrinsicTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run() => 4;
    }

    [Mixin(typeof(IntrinsicTarget))]
    private static class IntrinsicMixin
    {
        [Overwrite(nameof(IntrinsicTarget.Run)), Intrinsic]
        private static int Run() => 99;
    }

    public static class ConstraintTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run() { }
    }

    [Mixin(typeof(ConstraintTarget), Optional = true)]
    private static class RequireFailureMixin
    {
        [Inject(nameof(ConstraintTarget.Run)), Require(2)]
        private static void Head() { }
    }

    [Mixin(typeof(ConstraintTarget), Optional = true)]
    private static class AllowFailureMixin
    {
        [Inject(nameof(ConstraintTarget.Run)), Allow(0)]
        private static void Head() { }
    }

    [Mixin(typeof(ConstraintTarget), Optional = true)]
    private static class ExpectFailureMixin
    {
        [Inject(nameof(ConstraintTarget.Run)), Expect(2)]
        private static void Head() { }
    }

    public sealed class AccessorTarget
    {
        private int _value;

        public AccessorTarget(int value) => _value = value;
        private int Multiply(int multiplier) => _value * multiplier;
    }

    [Mixin(typeof(AccessorTarget))]
    public interface IAccessorTarget
    {
        [Accessor("_value")]
        int Value { get; set; }

        [Invoker("Multiply")]
        int InvokeMultiply(int multiplier);
    }

    public sealed class FinalAccessorTarget
    {
        private readonly int _value;
        public FinalAccessorTarget(int value) => _value = value;
    }

    [Mixin(typeof(FinalAccessorTarget))]
    public interface IReadOnlyFinalAccessor
    {
        [Accessor("_value")]
        int Value { get; set; }
    }

    [Mixin(typeof(FinalAccessorTarget))]
    public interface IMutableFinalAccessor
    {
        [Accessor("_value"), Mutable]
        int Value { get; set; }
    }
}
