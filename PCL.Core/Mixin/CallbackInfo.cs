using System;

namespace PCL.Mixin;

public class CallbackInfo
{
    public CallbackInfo(string id, bool cancellable)
    {
        Id = id;
        IsCancellable = cancellable;
    }

    public string Id { get; }
    public bool IsCancellable { get; }
    public bool IsCancelled { get; private set; }

    internal bool IsReturnValueModified { get; set; }

    public void Cancel()
    {
        if (!IsCancellable)
            throw new InvalidOperationException($"回调 {Id} 未声明 Cancellable，不能取消目标方法。");
        IsCancelled = true;
    }
}

public sealed class CallbackInfo<TResult> : CallbackInfo
{
    private TResult _returnValue;

    public CallbackInfo(string id, bool cancellable, TResult result)
        : base(id, cancellable) => _returnValue = result;

    public TResult ReturnValue
    {
        get => _returnValue;
        set
        {
            _returnValue = value;
            IsReturnValueModified = true;
        }
    }

    public void SetReturnValue(TResult value)
    {
        ReturnValue = value;
        Cancel();
    }
}

/// <summary>ModifyArgs 处理器看到的可变调用参数数组。</summary>
public sealed class MixinArgs
{
    private readonly object?[] _values;

    public MixinArgs(object?[] values) => _values = values;

    public int Count => _values.Length;
    public object? this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    public T Get<T>(int index) => (T)_values[index]!;
    public void Set<T>(int index, T value) => _values[index] = value;
    public object?[] ToArray() => (object?[])_values.Clone();
}
