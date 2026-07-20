using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace PCL.Mixin;

internal static class MixinTargetResolver
{
    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static Type? ResolveType(MixinAttribute attribute)
    {
        if (attribute.Target is not null) return attribute.Target;
        if (string.IsNullOrWhiteSpace(attribute.TargetName)) return null;

        var name = attribute.TargetName.Trim();
        var direct = Type.GetType(name, throwOnError: false, ignoreCase: false);
        if (direct is not null) return direct;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, throwOnError: false, ignoreCase: false);
            if (type is not null) return type;
        }
        return null;
    }

    public static MethodBase ResolveMethod(Type targetType, MixinOperationAttribute operation, MethodInfo handler)
    {
        var descriptor = string.IsNullOrWhiteSpace(operation.Method) ? handler.Name : operation.Method.Trim();
        var (name, descriptorTypes) = ParseMethodDescriptor(descriptor);
        var argumentTypes = operation.ArgumentTypes.Length > 0 ? operation.ArgumentTypes : descriptorTypes;

        IEnumerable<MethodBase> candidates;
        if (name is ".ctor" or "ctor")
        {
            candidates = targetType.GetConstructors(AllMembers).Cast<MethodBase>();
        }
        else if (name is ".cctor" or "cctor")
        {
            candidates = targetType.TypeInitializer is null ? [] : [targetType.TypeInitializer];
        }
        else
        {
            candidates = EnumerateMethods(targetType).Where(method => method.Name == name);
        }

        if (argumentTypes.Length > 0)
        {
            candidates = candidates.Where(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == argumentTypes.Length &&
                       parameters.Select(parameter => NormalizeByRef(parameter.ParameterType))
                           .SequenceEqual(argumentTypes.Select(NormalizeByRef));
            });
        }

        var matches = candidates.Distinct().ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length == 0)
            throw new MixinApplyException($"目标方法不存在：{targetType.FullName}::{descriptor}");
        throw new MixinApplyException(
            $"目标方法存在 {matches.Length} 个重载：{targetType.FullName}::{descriptor}。请设置 ArgumentTypes 或在方法名中写参数类型。");
    }

    public static MethodBase ResolveTargetDescriptor(string descriptor)
    {
        var separator = descriptor.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0 || separator >= descriptor.Length - 2)
            throw new MixinApplyException($"无效的目标描述符：{descriptor}");
        var targetType = ResolveTypeName(descriptor[..separator]);
        var operation = new DescriptorOperation(descriptor[(separator + 2)..]);
        return ResolveMethod(targetType, operation, DescriptorMarkerMethod);
    }

    public static MethodInfo ResolveHandlerDescriptor(Assembly assembly, string descriptor)
    {
        var separator = descriptor.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0 || separator >= descriptor.Length - 2)
            throw new MixinApplyException($"无效的处理器描述符：{descriptor}");
        var typeName = descriptor[..separator].Trim().Replace('/', '.');
        var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false)
            ?? throw new MixinApplyException($"处理器类型不存在：{typeName}");
        var (name, argumentTypes) = ParseMethodDescriptor(descriptor[(separator + 2)..]);
        var methods = type.GetMethods(AllMembers | BindingFlags.DeclaredOnly)
            .Where(method => method.IsStatic && method.Name == name);
        if (argumentTypes.Length > 0)
        {
            methods = methods.Where(method => method.GetParameters()
                .Select(parameter => NormalizeByRef(parameter.ParameterType))
                .SequenceEqual(argumentTypes.Select(NormalizeByRef)));
        }
        var matches = methods.ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length == 0)
            throw new MixinApplyException($"静态补丁处理器不存在：{descriptor}");
        throw new MixinApplyException($"补丁处理器存在 {matches.Length} 个重载：{descriptor}。请写出参数类型。");
    }

    public static IEnumerable<MethodInfo> EnumerateMethods(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMethods(AllMembers | BindingFlags.DeclaredOnly))
                yield return method;
        }
    }

    public static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, AllMembers | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        return null;
    }

    public static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, AllMembers | BindingFlags.DeclaredOnly);
            if (property is not null) return property;
        }
        return null;
    }

    public static Type ResolveTypeName(string name)
    {
        name = name.Trim();
        var aliases = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["bool"] = typeof(bool), ["byte"] = typeof(byte), ["sbyte"] = typeof(sbyte),
            ["short"] = typeof(short), ["ushort"] = typeof(ushort), ["int"] = typeof(int),
            ["uint"] = typeof(uint), ["long"] = typeof(long), ["ulong"] = typeof(ulong),
            ["float"] = typeof(float), ["double"] = typeof(double), ["decimal"] = typeof(decimal),
            ["char"] = typeof(char), ["string"] = typeof(string), ["object"] = typeof(object),
            ["void"] = typeof(void)
        };
        if (aliases.TryGetValue(name, out var alias)) return alias;

        var type = Type.GetType(name, false, false);
        if (type is not null) return type;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(name, false, false);
            if (type is not null) return type;
        }
        throw new MixinApplyException($"无法解析类型名：{name}");
    }

    public static bool MemberMatches(object? operand, string descriptor)
    {
        if (operand is not MemberInfo member || string.IsNullOrWhiteSpace(descriptor)) return false;
        descriptor = descriptor.Trim().Replace('/', '.');
        var separator = descriptor.IndexOf("::", StringComparison.Ordinal);
        var owner = separator >= 0 ? descriptor[..separator].Trim() : null;
        var memberDescriptor = separator >= 0 ? descriptor[(separator + 2)..].Trim() : descriptor;
        var (name, types) = ParseMethodDescriptor(memberDescriptor);

        if (!string.Equals(member.Name, name, StringComparison.Ordinal)) return false;
        if (owner is not null && !TypeNameMatches(member.DeclaringType, owner)) return false;
        if (types.Length == 0 || member is not MethodBase method) return true;

        var parameters = method.GetParameters();
        return parameters.Length == types.Length &&
               parameters.Select(parameter => NormalizeByRef(parameter.ParameterType))
                   .SequenceEqual(types.Select(NormalizeByRef));
    }

    public static (string Name, Type[] ArgumentTypes) ParseMethodDescriptor(string descriptor)
    {
        var open = descriptor.IndexOf('(');
        if (open < 0) return (descriptor.Trim(), []);
        var close = descriptor.LastIndexOf(')');
        if (close < open) throw new MixinApplyException($"无效的方法描述符：{descriptor}");

        var name = descriptor[..open].Trim();
        var arguments = descriptor[(open + 1)..close].Trim();
        if (arguments.Length == 0) return (name, []);
        return (name, SplitTypeNames(arguments).Select(ResolveTypeName).ToArray());
    }

    public static bool ConstantMatches(object? value, string descriptor)
    {
        var separator = descriptor.IndexOf(':');
        var kind = separator < 0 ? descriptor.Trim() : descriptor[..separator].Trim();
        var raw = separator < 0 ? string.Empty : descriptor[(separator + 1)..];
        return kind.ToLowerInvariant() switch
        {
            "null" => value is null,
            "string" => value is string text && text == raw,
            "int" => value is int number && number == int.Parse(raw, CultureInfo.InvariantCulture),
            "long" => value is long number && number == long.Parse(raw, CultureInfo.InvariantCulture),
            "float" => value is float number && number.Equals(float.Parse(raw, CultureInfo.InvariantCulture)),
            "double" => value is double number && number.Equals(double.Parse(raw, CultureInfo.InvariantCulture)),
            _ => Equals(value, descriptor)
        };
    }

    private static IEnumerable<string> SplitTypeNames(string text)
    {
        var start = 0;
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<': case '[': depth++; break;
                case '>': case ']': depth--; break;
                case ',' when depth == 0:
                    yield return text[start..index].Trim();
                    start = index + 1;
                    break;
            }
        }
        yield return text[start..].Trim();
    }

    private static bool TypeNameMatches(Type? type, string name)
        => type is not null && (type.FullName == name || type.AssemblyQualifiedName == name || type.Name == name);

    private static Type NormalizeByRef(Type type) => type.IsByRef ? type.GetElementType()! : type;

    private static readonly MethodInfo DescriptorMarkerMethod = typeof(MixinTargetResolver).GetMethod(
        nameof(DescriptorMarker),
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void DescriptorMarker() { }

    private sealed class DescriptorOperation(string method) : MixinOperationAttribute(method);
}
