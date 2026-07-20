using System;

namespace PCL.Mixin;

/// <summary>声明一个类型包含针对目标类型的运行时 Mixin。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class MixinAttribute : Attribute
{
    public MixinAttribute(Type target) => Target = target;
    public MixinAttribute(string targetName) => TargetName = targetName;

    public Type? Target { get; }
    public string? TargetName { get; }
    public int Priority { get; set; } = 1000;
    public bool Optional { get; set; }
}

public enum MixinAt
{
    Head,
    Return,
    Tail,
    Invoke,
    InvokeAssign,
    Field,
    New,
    Constant,
    Jump,
    Load,
    Store,
    LocalLoad = Load,
    LocalStore = Store
}

public enum AtShift
{
    Before,
    After,
    By
}

public enum LocalCapture
{
    NoCapture,
    FailSoft,
    FailHard
}

public abstract class MixinOperationAttribute : Attribute
{
    protected MixinOperationAttribute(string method) => Method = method;

    /// <summary>目标方法名，也可写成 <c>Name(System.Int32,System.String)</c>。</summary>
    public string Method { get; }

    /// <summary>用于精确选择重载。为空且存在多个重载时会拒绝应用。</summary>
    public Type[] ArgumentTypes { get; set; } = [];

    public int Priority { get; set; } = 1000;
    public int Require { get; set; } = -1;
    public int Expect { get; set; } = -1;
    public int Allow { get; set; } = -1;
}

/// <summary>在目标方法或目标 IL 指令处调用处理器。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InjectAttribute(string method) : MixinOperationAttribute(method)
{
    public MixinAt At { get; set; } = MixinAt.Head;
    public string? Target { get; set; }
    public int Ordinal { get; set; } = -1;
    public AtShift Shift { get; set; } = AtShift.Before;
    public int By { get; set; }
    public int Opcode { get; set; } = -1;
    public bool Cancellable { get; set; }
    public LocalCapture Locals { get; set; }
    public string? Slice { get; set; }
    public string? SliceFrom { get; set; }
    public string? SliceTo { get; set; }
}

/// <summary>完全替换一个目标方法。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OverwriteAttribute(string method = "") : MixinOperationAttribute(method)
{
}

/// <summary>把指定调用或字段访问重定向到静态处理器。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RedirectAttribute(string method) : MixinOperationAttribute(method)
{
    public MixinAt At { get; set; } = MixinAt.Invoke;
    public required string Target { get; set; }
    public int Ordinal { get; set; } = -1;
    public int Opcode { get; set; } = -1;
    public string? Slice { get; set; }
    public string? SliceFrom { get; set; }
    public string? SliceTo { get; set; }
}

/// <summary>修改一次方法调用的某个参数。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ModifyArgAttribute(string method) : MixinOperationAttribute(method)
{
    public required string Target { get; set; }
    public int Index { get; set; }
    public int Ordinal { get; set; } = -1;
    public string? Slice { get; set; }
    public string? SliceFrom { get; set; }
    public string? SliceTo { get; set; }
}

/// <summary>通过 <see cref="MixinArgs"/> 修改一次调用的全部参数。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ModifyArgsAttribute(string method) : MixinOperationAttribute(method)
{
    public required string Target { get; set; }
    public int Ordinal { get; set; } = -1;
    public string? Slice { get; set; }
    public string? SliceFrom { get; set; }
    public string? SliceTo { get; set; }
}

/// <summary>修改局部变量的加载值或写入值。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ModifyVariableAttribute(string method) : MixinOperationAttribute(method)
{
    public MixinAt At { get; set; } = MixinAt.Load;
    public int Index { get; set; } = -1;
    public int Ordinal { get; set; } = -1;
}

/// <summary>修改目标方法中的常量。Target 使用 int:1、long:1、float:1、double:1、string:text、null。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ModifyConstantAttribute(string method) : MixinOperationAttribute(method)
{
    public required string Target { get; set; }
    public int Ordinal { get; set; } = -1;
    public string? Slice { get; set; }
    public string? SliceFrom { get; set; }
    public string? SliceTo { get; set; }
}

/// <summary>声明处理器依赖的目标成员；运行时会在应用阶段验证它。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
public sealed class ShadowAttribute(string name = "") : Attribute
{
    public string Name { get; } = name;
    public bool Mutable { get; set; }
    public bool Optional { get; set; }
}

/// <summary>声明 Shadow 目标必须是只读字段或没有 setter 的属性。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class FinalAttribute : Attribute;

/// <summary>允许 Accessor 或 Shadow 写入原本只读的目标成员。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
public sealed class MutableAttribute : Attribute;

/// <summary>声明处理器在目标存在时使用目标实现，避免产生第二个覆盖实现。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class IntrinsicAttribute : Attribute;

/// <summary>声明一个可由注入器通过名称引用的 IL 范围。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class SliceAttribute(string id) : Attribute
{
    public string Id { get; } = id;
    public string From { get; set; } = "HEAD";
    public string To { get; set; } = "TAIL";
}

/// <summary>为 Mixin 类型或处理器声明优先级。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class PriorityAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class RequireAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ExpectAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AllowAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CancellableAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class OrdinalAttribute(int value) : Attribute
{
    public int Value { get; } = value;
}

/// <summary>标注访问器接口的字段/属性访问方法。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = true)]
public sealed class AccessorAttribute(string name = "") : Attribute
{
    public string Name { get; } = name;
}

/// <summary>标注访问器接口中的目标方法调用。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class InvokerAttribute(string name = "") : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ThisAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ArgAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class LocalAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ReturnAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class UniqueAttribute : Attribute;
