using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace PCL.Core.App.Plugins.JavaScript;

public sealed class JavaScriptRuntime : IDisposable
{
    private readonly Engine _engine = new();

    public void SetValue(string name, object? value) => _engine.SetValue(name, value);

    public void SetType(string name, Type type) => _engine.SetValue(name, type);

    public void Execute(string source, string sourceName) => _engine.Execute(source, sourceName);

    public object? Evaluate(string source, string sourceName) => ToObject(_engine.Evaluate(source, sourceName));

    public bool HasFunction(string name) => _engine.GetValue(name) is Function;

    public object? InvokeFunction(string name, params object?[] args)
        => ToObject(_engine.Invoke(name, args));

    public bool IsCallable(object? value) => UnwrapJsValue(value) is Function || value is Delegate;

    public object? InvokeCallback(object? callback, params object?[] args)
    {
        var unwrapped = UnwrapJsValue(callback);
        if (unwrapped is Function function)
            return ToObject(_engine.Invoke(function, args));
        if (unwrapped is Delegate del)
        {
            var parameters = del.Method.GetParameters();
            var converted = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(JsValue))
                    converted[i] = JsValue.Undefined;
                else if (parameterType == typeof(JsValue[]))
                    converted[i] = args.Select(arg => JsValue.FromObject(_engine, arg)).ToArray();
                else
                    converted[i] = i < args.Length ? args[i] : Type.Missing;
            }
            try
            {
                return ToObject(del.DynamicInvoke(converted));
            }
            catch (TargetParameterCountException) when (args.Length > 0)
            {
                return ToObject(del.DynamicInvoke());
            }
        }
        throw new ArgumentException("回调必须是 JavaScript 函数或 .NET 委托。", nameof(callback));
    }

    public static bool TryEnumerateObject(object? value, out IReadOnlyList<(string Name, object? Value)> properties)
    {
        if (UnwrapJsValue(value) is ObjectInstance objectInstance)
        {
            properties = objectInstance
                .GetOwnPropertyKeys(Types.String)
                .Select(key => (key.ToString(), ToObject(objectInstance.Get(key))))
                .ToArray();
            return true;
        }

        properties = [];
        return false;
    }

    public static bool TryGetProperty(object? value, string propertyName, out object? propertyValue)
    {
        if (UnwrapJsValue(value) is ObjectInstance objectInstance)
        {
            JsValue key = propertyName;
            if (objectInstance.HasProperty(key))
            {
                propertyValue = ToObject(objectInstance.Get(key));
                return true;
            }
        }

        propertyValue = null;
        return false;
    }

    public static object? ToObject(object? value)
    {
        if (value is Function) return value;
        if (value is ObjectWrapper wrapper)
            return wrapper.Target;
        if (value is JsValue jsValue)
        {
            if (jsValue.IsNull() || jsValue.IsUndefined()) return null;
            if (jsValue is ObjectWrapper objectWrapper) return objectWrapper.Target;
            return jsValue.ToObject();
        }

        return value;
    }

    private static object? UnwrapJsValue(object? value)
    {
        if (value is Function) return value;
        if (value is ObjectWrapper wrapper)
            return wrapper.Target;
        return value is JsValue jsValue ? jsValue : value;
    }

    public void Dispose() => _engine.Dispose();
}