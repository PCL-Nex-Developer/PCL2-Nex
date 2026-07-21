using System;
using System.Collections.Generic;
using System.Linq;
using PCL.Core.App.Localization;
using PCL.Core.Utils;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件前置依赖解析。Core 仅管理依赖关系、版本与加载顺序；具体 Bridge 能力由前置插件实现。
/// </summary>
public static class PluginDependencyService
{
    public static PluginDependencyCheckResult ValidateDeclarations(
        string ownerPluginId,
        IEnumerable<PluginDependency>? dependencies)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies ?? [])
        {
            if (dependency is null || !PluginPackageService.IsValidPluginId(dependency.Id))
                return PluginDependencyCheckResult.Failure(Text("Plugins.Dependency.Error.InvalidDependencyId", "前置插件包含无效的插件 ID。"));
            if (string.Equals(ownerPluginId, dependency.Id, StringComparison.OrdinalIgnoreCase))
                return PluginDependencyCheckResult.Failure(Text("Plugins.Dependency.Error.SelfDependency", "插件 {0} 不能依赖自身。", ownerPluginId));
            if (!seen.Add(dependency.Id.Trim()))
                return PluginDependencyCheckResult.Failure(Text("Plugins.Dependency.Error.DuplicateDependency", "前置插件 {0} 被重复声明。", dependency.Id));
            if (!TryValidateVersionExpression(dependency.Version, out var error))
                return PluginDependencyCheckResult.Failure(
                    Text("Plugins.Dependency.Error.InvalidVersionConstraint", "前置插件 {0} 的版本约束无效：{1}", dependency.Id, error));
        }

        return PluginDependencyCheckResult.Success;
    }

    /// <summary>检查当前机器上是否已安装并启用了清单要求的全部前置插件。</summary>
    public static PluginDependencyCheckResult CheckInstalledDependencies(
        PluginPackageManifest manifest,
        bool requireEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var installed = PluginLoaderService.EnumerateInstalledPluginPackages(Paths.PluginInstalled)
            .GroupBy(item => item.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Manifest, StringComparer.OrdinalIgnoreCase);
        return CheckDependencies(
            manifest,
            installed,
            requireEnabled ? PluginEnablementService.IsEnabled : null);
    }

    internal static PluginDependencyCheckResult CheckDependencies(
        PluginPackageManifest manifest,
        IReadOnlyDictionary<string, PluginPackageManifest> installed,
        Func<string, bool>? isEnabled)
    {
        var declarationResult = ValidateDeclarations(manifest.Id, manifest.Dependencies);
        if (!declarationResult.IsValid) return declarationResult;

        foreach (var dependency in manifest.Dependencies ?? [])
        {
            var id = dependency.Id.Trim();
            if (!installed.TryGetValue(id, out var installedManifest))
                return PluginDependencyCheckResult.Failure(Text("Plugins.Dependency.Error.MissingDependency", "缺少前置插件 {0}。", id));
            if (isEnabled is not null && !isEnabled(id))
                return PluginDependencyCheckResult.Failure(Text("Plugins.Dependency.Error.NotEnabled", "前置插件 {0} 已安装但未启用。", id));
            if (!IsVersionSatisfied(installedManifest.Version, dependency.Version, out var versionError))
                return PluginDependencyCheckResult.Failure(
                    versionError ?? Text("Plugins.Dependency.Error.VersionNotSatisfied", "前置插件 {0} 的版本不满足要求 {1}。", id, NormalizeExpression(dependency.Version)));
        }

        return PluginDependencyCheckResult.Success;
    }

    public static bool IsVersionSatisfied(
        string? installedVersion,
        string? expression,
        out string? error)
    {
        error = null;
        if (!SemVer.TryParse(installedVersion ?? string.Empty, out var installed))
        {
            error = Text("Plugins.Dependency.Error.InvalidInstalledVersion", "已安装的前置插件版本 {0} 不是有效 SemVer。", installedVersion ?? "<空>");
            return false;
        }

        var normalized = NormalizeExpression(expression);
        if (normalized == "*") return true;
        foreach (var token in SplitExpression(normalized))
        {
            if (!TryParseComparator(token, out var comparator, out var expected, out var parseError))
            {
                error = parseError;
                return false;
            }

            var comparison = installed!.CompareTo(expected);
            var matches = comparator switch
            {
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                "=" => comparison == 0,
                _ => false
            };
            if (!matches)
            {
                error = Text("Plugins.Dependency.Error.VersionConstraintNotSatisfied", "前置插件版本 {0} 不满足约束 {1}。", installedVersion, normalized);
                return false;
            }
        }

        return true;
    }

    internal static PluginLoadPlan CreateLoadPlan(
        IReadOnlyList<PluginPackageLocation> installedPackages,
        Func<string, bool> isEnabled,
        IReadOnlyList<string> enabledOrder)
    {
        var packages = new Dictionary<string, PluginPackageLocation>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in installedPackages)
        {
            var id = package.Manifest.Id?.Trim() ?? string.Empty;
            if (!PluginPackageService.IsValidPluginId(id)) continue;
            if (!packages.TryAdd(id, package))
                errors[id] = Text("Plugins.Dependency.Error.DuplicatePluginId", "检测到重复安装的插件 ID：{0}。", id);
        }

        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var ordered = new List<PluginPackageLocation>();
        var comparer = Comparer<string>.Create((left, right) =>
            PluginEnablementService.CompareByEnabledOrder(left, right, enabledOrder));

        bool Visit(string id)
        {
            if (states.TryGetValue(id, out var state))
            {
                if (state == 2) return !errors.ContainsKey(id);
                if (state == 1)
                {
                    var start = stack.FindIndex(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
                    var cycle = (start < 0 ? stack : stack.Skip(start)).Append(id).ToArray();
                    var message = Text("Plugins.Dependency.Error.CircularDependency", "检测到插件循环依赖：{0}。", string.Join(" -> ", cycle));
                    foreach (var cycleId in cycle) errors[cycleId] = message;
                    return false;
                }
            }

            states[id] = 1;
            stack.Add(id);
            var package = packages[id];
            var declarationResult = ValidateDeclarations(id, package.Manifest.Dependencies);
            if (!declarationResult.IsValid)
                errors[id] = declarationResult.ErrorMessage!;

            IEnumerable<PluginDependency> orderedDependencies = declarationResult.IsValid
                ? (package.Manifest.Dependencies ?? []).OrderBy(item => item.Id, comparer)
                : Enumerable.Empty<PluginDependency>();
            foreach (var dependency in orderedDependencies)
            {
                var dependencyId = dependency.Id.Trim();
                if (!packages.TryGetValue(dependencyId, out var dependencyPackage))
                {
                    errors[id] = Text("Plugins.Dependency.Error.MissingDependency", "缺少前置插件 {0}。", dependencyId);
                    continue;
                }
                if (!isEnabled(dependencyId))
                {
                    errors[id] = Text("Plugins.Dependency.Error.NotEnabled", "前置插件 {0} 已安装但未启用。", dependencyId);
                    continue;
                }
                if (!IsVersionSatisfied(dependencyPackage.Manifest.Version, dependency.Version, out var versionError))
                {
                    errors[id] = versionError ?? Text("Plugins.Dependency.Error.VersionNotSatisfiedGeneric", "前置插件 {0} 的版本不满足要求。", dependencyId);
                    continue;
                }
                if (!Visit(dependencyId) && !errors.ContainsKey(id))
                    errors[id] = Text("Plugins.Dependency.Error.DependencyLoadFailed", "前置插件 {0} 无法加载。", dependencyId);
            }

            stack.RemoveAt(stack.Count - 1);
            states[id] = 2;
            if (!errors.ContainsKey(id)) ordered.Add(package);
            return !errors.ContainsKey(id);
        }

        foreach (var id in packages.Keys.Where(isEnabled).OrderBy(id => id, comparer)) Visit(id);
        return new PluginLoadPlan(ordered, errors);
    }

    internal static bool DependencyListsEqual(
        IEnumerable<PluginDependency>? left,
        IEnumerable<PluginDependency>? right)
    {
        static string Normalize(PluginDependency dependency) =>
            dependency.Id.Trim().ToLowerInvariant() + "\n" + NormalizeExpression(dependency.Version);

        return (left ?? []).Select(Normalize).OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                (right ?? []).Select(Normalize).OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static bool TryValidateVersionExpression(string? expression, out string? error)
    {
        error = null;
        var normalized = NormalizeExpression(expression);
        if (normalized == "*") return true;
        foreach (var token in SplitExpression(normalized))
        {
            if (TryParseComparator(token, out _, out _, out error)) continue;
            return false;
        }
        return true;
    }

    private static IEnumerable<string> SplitExpression(string expression)
        => expression.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeExpression(string? expression)
        => string.IsNullOrWhiteSpace(expression) ? "*" : expression.Trim();

    private static bool TryParseComparator(
        string token,
        out string comparator,
        out SemVer? version,
        out string? error)
    {
        comparator = "=";
        version = null;
        error = null;
        var value = token.Trim();
        foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
        {
            if (!value.StartsWith(candidate, StringComparison.Ordinal)) continue;
            comparator = candidate;
            value = value[candidate.Length..];
            break;
        }

        if (value.StartsWith('v') || !SemVer.TryParse(value, out version))
        {
            error = Text("Plugins.Dependency.Error.InvalidVersionExpression", "无法解析版本约束 {0}。请使用完整 SemVer，例如 >=1.0.0 <2.0.0。", token);
            return false;
        }
        return true;
    }

    private static string Text(string key, string fallback, params object?[] args)
    {
        var template = Lang.Text(key);
        if (string.Equals(template, key, StringComparison.Ordinal)
            || string.Equals(template, $"!{key}!", StringComparison.Ordinal))
            template = fallback;
        return string.Format(Lang.Culture, template, args);
    }
}

public sealed record PluginDependencyCheckResult(bool IsValid, string? ErrorMessage)
{
    public static PluginDependencyCheckResult Success { get; } = new(true, null);
    public static PluginDependencyCheckResult Failure(string message) => new(false, message);
}

internal sealed record PluginPackageLocation(PluginPackageManifest Manifest, string PluginDirectory);

internal sealed record PluginLoadPlan(
    IReadOnlyList<PluginPackageLocation> Packages,
    IReadOnlyDictionary<string, string> Errors);
