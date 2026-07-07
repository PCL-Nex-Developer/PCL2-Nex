using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace PCL.Core.App.Plugins.JavaScript;

public sealed class JavaScriptRawObject(object target, JavaScriptRuntime runtime)
{
    internal object Target => target;

    public string TypeName => target.GetType().Name;
    public string typeName => TypeName;
    public string FullName => target.GetType().FullName ?? target.GetType().Name;
    public string fullName => FullName;
    public string AssemblyName => target.GetType().Assembly.GetName().Name ?? string.Empty;
    public string assemblyName => AssemblyName;

    public object? Get(string name)
    {
        var property = FindProperty(name);
        if (property is not null && property.CanRead)
            return Wrap(property.GetValue(target));

        var field = FindField(name);
        if (field is not null)
            return Wrap(field.GetValue(target));

        throw new MissingMemberException(FullName, name);
    }
    public object? get(string name) => Get(name);

    public JavaScriptRawObject Set(string name, object? value)
    {
        var property = FindProperty(name);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(target, JavaScriptElement.ConvertForProperty(value, property.PropertyType));
            return this;
        }

        var field = FindField(name);
        if (field is not null && !field.IsInitOnly)
        {
            field.SetValue(target, JavaScriptElement.ConvertForProperty(value, field.FieldType));
            return this;
        }

        throw new MissingMemberException(FullName, name);
    }
    public JavaScriptRawObject set(string name, object? value) => Set(name, value);

    public object? Call(string name, params object?[] args)
    {
        var method = FindMethod(name, args);
        if (method is null) throw new MissingMethodException(FullName, name);

        var parameters = method.GetParameters();
        var converted = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            converted[i] = JavaScriptElement.ConvertForProperty(i < args.Length ? args[i] : parameters[i].DefaultValue, parameters[i].ParameterType);

        return Wrap(method.Invoke(target, converted));
    }
    public object? call(string name, params object?[] args) => Call(name, args);

    public object? GetField(string name)
    {
        var field = FindField(name) ?? throw new MissingFieldException(FullName, name);
        return Wrap(field.GetValue(target));
    }
    public object? getField(string name) => GetField(name);
    public object? field(string name) => GetField(name);

    public JavaScriptRawObject SetField(string name, object? value)
    {
        var field = FindField(name) ?? throw new MissingFieldException(FullName, name);
        if (field.IsInitOnly) throw new InvalidOperationException($"字段 {name} 是只读字段。");
        field.SetValue(target, JavaScriptElement.ConvertForProperty(value, field.FieldType));
        return this;
    }
    public JavaScriptRawObject setField(string name, object? value) => SetField(name, value);

    public object? GetDp(string name)
    {
        if (target is not DependencyObject dependencyObject)
            throw new InvalidOperationException($"{FullName} 不是 DependencyObject。");
        var property = FindDependencyProperty(name) ?? throw new MissingMemberException(FullName, name + "Property");
        return Wrap(dependencyObject.GetValue(property));
    }
    public object? getDp(string name) => GetDp(name);

    public JavaScriptRawObject SetDp(string name, object? value)
    {
        if (target is not DependencyObject dependencyObject)
            throw new InvalidOperationException($"{FullName} 不是 DependencyObject。");
        var property = FindDependencyProperty(name) ?? throw new MissingMemberException(FullName, name + "Property");
        dependencyObject.SetValue(property, JavaScriptElement.ConvertForProperty(value, property.PropertyType));
        return this;
    }
    public JavaScriptRawObject setDp(string name, object? value) => SetDp(name, value);

    public JavaScriptRawObject ClearDp(string name)
    {
        if (target is not DependencyObject dependencyObject)
            throw new InvalidOperationException($"{FullName} 不是 DependencyObject。");
        var property = FindDependencyProperty(name) ?? throw new MissingMemberException(FullName, name + "Property");
        dependencyObject.ClearValue(property);
        return this;
    }
    public JavaScriptRawObject clearDp(string name) => ClearDp(name);

    public JavaScriptRawObject SetResource(string dependencyPropertyName, string resourceKey)
    {
        if (target is not FrameworkElement frameworkElement)
            throw new InvalidOperationException($"{FullName} 不是 FrameworkElement。");
        var property = FindDependencyProperty(dependencyPropertyName) ?? throw new MissingMemberException(FullName, dependencyPropertyName + "Property");
        frameworkElement.SetResourceReference(property, resourceKey);
        return this;
    }
    public JavaScriptRawObject setResource(string dependencyPropertyName, string resourceKey) => SetResource(dependencyPropertyName, resourceKey);

    public bool Has(string name) => FindProperty(name) is not null || FindField(name) is not null || FindMethod(name, []) is not null;
    public bool has(string name) => Has(name);

    public string[] Properties()
    {
        return target.GetType().GetProperties(MemberFlags).Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
    }
    public string[] properties() => Properties();

    public string[] Fields()
    {
        return target.GetType().GetFields(MemberFlags).Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
    }
    public string[] fields() => Fields();

    public string[] Methods()
    {
        return target.GetType().GetMethods(MemberFlags).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
    }
    public string[] methods() => Methods();

    public override string ToString() => target.ToString() ?? FullName;

    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

    private PropertyInfo? FindProperty(string name) => FindDeclaredProperty(name) ?? FindDeclaredProperty(UpperFirst(name));

    private PropertyInfo? FindDeclaredProperty(string name)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperties(MemberFlags | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0 && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                .ThenByDescending(p => p.CanWrite)
                .ThenByDescending(p => p.CanRead)
                .FirstOrDefault();
            if (property is not null) return property;
        }

        return null;
    }

    private FieldInfo? FindField(string name) => target.GetType().GetField(name, MemberFlags) ?? target.GetType().GetField(UpperFirst(name), MemberFlags);

    private MethodInfo? FindMethod(string name, IReadOnlyList<object?> args)
    {
        foreach (var method in target.GetType().GetMethods(MemberFlags).Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var parameters = method.GetParameters();
            var required = parameters.Count(p => !p.HasDefaultValue);
            if (args.Count < required || args.Count > parameters.Length) continue;
            if (CanConvertArguments(parameters, args)) return method;
        }

        return null;
    }

    private static bool CanConvertArguments(IReadOnlyList<ParameterInfo> parameters, IReadOnlyList<object?> args)
    {
        try
        {
            for (var i = 0; i < args.Count; i++)
                JavaScriptElement.ConvertForProperty(args[i], parameters[i].ParameterType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private DependencyProperty? FindDependencyProperty(string name)
    {
        var normalized = Normalize(name);
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (!typeof(DependencyProperty).IsAssignableFrom(field.FieldType)) continue;
                var fieldName = field.Name.EndsWith("Property", StringComparison.OrdinalIgnoreCase)
                    ? field.Name[..^"Property".Length]
                    : field.Name;
                if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase) || Normalize(fieldName) == normalized)
                    return (DependencyProperty?)field.GetValue(null);
            }
        }

        return null;
    }

    private object? Wrap(object? value)
    {
        value = JavaScriptElement.Unwrap(value);
        if (value is null) return null;
        var type = value.GetType();
        if (type.IsPrimitive || value is string or decimal or DateTime or TimeSpan or Guid || type.IsEnum) return value;
        if (value is FrameworkElement frameworkElement) return new JavaScriptElement(frameworkElement, runtime);
        return new JavaScriptRawObject(value, runtime);
    }

    private static string UpperFirst(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsUpper(value[0])) return value;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}