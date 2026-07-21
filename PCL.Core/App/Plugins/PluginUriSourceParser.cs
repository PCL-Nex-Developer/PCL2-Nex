using PCL.Core.App.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using PCL.Core.App.Essentials;

namespace PCL.Core.App.Plugins;

/// <summary>URI Scheme 中声明的插件安装来源类型。</summary>
public enum PluginUriInstallSourceKind
{
    LocalPackage,
    RemotePackage,
    Manifest,
    Git
}

/// <summary>经过校验和归一化的 URI 插件安装来源。</summary>
public sealed record PluginUriInstallSource(string Value, PluginUriInstallSourceKind Kind);

/// <summary>经过校验和归一化的 URI 插件商店来源。</summary>
public sealed record PluginUriRepositorySource(
    string Value,
    string Name,
    PluginRepositorySourceKind Kind);

/// <summary>
/// 解析插件相关 URI 参数。该类型不执行安装或持久化，便于在执行前完成一致的来源分类与安全校验。
/// </summary>
public static class PluginUriSourceParser
{
    public static bool TryParseInstallSource(
        UriActionRequest request,
        out PluginUriInstallSource? source,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        source = null;
        error = null;

        if (TryGetValue(request.Query, out var value, "manifest"))
            return TryCreateInstallSource(value, PluginUriInstallSourceKind.Manifest, out source, out error);
        if (TryGetValue(request.Query, out value, "package"))
            return TryCreatePackageSource(value, out source, out error);
        if (TryGetValue(request.Query, out value, "git"))
            return TryCreateInstallSource(value, PluginUriInstallSourceKind.Git, out source, out error);

        if (!TryGetValue(request.Query, out value, "file", "path", "source", "url"))
            value = request.PathArguments.Count > 0 ? request.PathArguments[0] : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = Text("Plugins.UriParser.Error.MissingInstallParam", "URI 安装插件缺少 source、manifest 或 package 参数。");
            return false;
        }

        value = value.Trim();
        if (File.Exists(value)) return TryCreatePackageSource(value, out source, out error);
        if (!IsAbsoluteHttpUri(value) && !LooksLikeGitSource(value))
        {
            error = Text("Plugins.UriParser.Error.InvalidSource", "URI 插件来源无效。仅支持本地 .pclx/.zip、远程插件包、manifest 或 Git 仓库。");
            return false;
        }

        if (PluginRemoteInstallService.LooksLikePackageUrl(value))
            source = new PluginUriInstallSource(value, PluginUriInstallSourceKind.RemotePackage);
        else if (PluginRemoteInstallService.LooksLikeManifestUrl(value))
            source = new PluginUriInstallSource(value, PluginUriInstallSourceKind.Manifest);
        else
            source = new PluginUriInstallSource(value, PluginUriInstallSourceKind.Git);
        return true;
    }

    public static bool TryParseRepositorySource(
        UriActionRequest request,
        out PluginUriRepositorySource? source,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        source = null;
        error = null;

        PluginRepositorySourceKind? parameterKind = null;
        string? value = null;
        if (TryGetValue(request.Query, out var typedValue, "topic"))
        {
            error = Text("Plugins.UriParser.Error.CustomTopicNotSupported", "自定义 Topic 插件源不受支持；GitHub Topic 由启动器内置维护。");
            return false;
        }
        else if (TryGetValue(request.Query, out typedValue, "manifest"))
        {
            parameterKind = PluginRepositorySourceKind.Manifest;
            value = typedValue;
        }
        else if (TryGetValue(request.Query, out typedValue, "json"))
        {
            parameterKind = PluginRepositorySourceKind.Json;
            value = typedValue;
        }

        if (TryGetExplicitRepositoryKind(request, out var explicitKind, out error))
            parameterKind = explicitKind;
        else if (error is not null)
            return false;

        if (string.IsNullOrWhiteSpace(value)
            && !TryGetValue(request.Query, out value, "url", "source", "repo", "repository", "index", "registry"))
            value = request.PathArguments.Count > 0 ? request.PathArguments[0] : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = Text("Plugins.UriParser.Error.MissingSourceParam", "URI 添加插件源缺少 source、manifest 或 json 参数。");
            return false;
        }

        value = value.Trim();
        if (value.StartsWith("topic:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("topics:", StringComparison.OrdinalIgnoreCase))
        {
            error = Text("Plugins.UriParser.Error.CustomTopicNotSupported", "自定义 Topic 插件源不受支持；GitHub Topic 由启动器内置维护。");
            return false;
        }
        var prefixKind = StripSourcePrefix(ref value);
        var kind = parameterKind ?? prefixKind ?? InferRepositoryKind(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            error = Text("Plugins.UriParser.Error.SourceContentEmpty", "URI 插件源内容不能为空。");
            return false;
        }

        switch (kind)
        {
            case PluginRepositorySourceKind.Topic:
            error = Text("Plugins.UriParser.Error.CustomTopicNotSupported", "自定义 Topic 插件源不受支持；GitHub Topic 由启动器内置维护。");
                return false;
            case PluginRepositorySourceKind.Manifest:
                if (!IsAbsoluteHttpUri(value))
                {
                    error = Text("Plugins.UriParser.Error.ManifestMustBeHttp", "URI manifest 插件源必须使用 HTTP 或 HTTPS 地址。");
                    return false;
                }
                break;
            default:
                if (File.Exists(value))
                {
                    if (!value.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        error = Text("Plugins.UriParser.Error.LocalSourceMustBeFile", "URI 本地插件源必须是已有的 JSON 文件。");
                        return false;
                    }
                    value = Path.GetFullPath(value);
                }
                else if (!IsAbsoluteHttpUri(value))
                {
                    error = Text("Plugins.UriParser.Error.JsonSourceMustBeHttpOrFile", "URI JSON 插件源必须是 HTTP/HTTPS 地址或已有的本地 JSON 文件。");
                    return false;
                }
                break;
        }

        var name = GetFirstValue(request.Query, "name", "title", "repoName");
        if (string.IsNullOrWhiteSpace(name))
            name = Text("Plugins.UriParser.Label.CustomPluginSource", "自定义插件源");
        else
            name = name.Trim();
        source = new PluginUriRepositorySource(value, name, kind);
        return true;
    }

    private static bool TryCreateInstallSource(
        string? value,
        PluginUriInstallSourceKind kind,
        out PluginUriInstallSource? source,
        out string? error)
    {
        source = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = Text("Plugins.UriParser.Error.InstallSourceEmpty", "URI 插件来源不能为空。");
            return false;
        }

        value = value.Trim();
        if (kind == PluginUriInstallSourceKind.Manifest && !IsAbsoluteHttpUri(value))
        {
            error = Text("Plugins.UriParser.Error.ManifestMustBeHttp2", "URI 插件 manifest 必须使用 HTTP 或 HTTPS 地址。");
            return false;
        }
        if (kind == PluginUriInstallSourceKind.Git && !LooksLikeGitSource(value))
        {
            error = Text("Plugins.UriParser.Error.InvalidGitSource", "URI Git 插件来源无效。");
            return false;
        }
        source = new PluginUriInstallSource(value, kind);
        return true;
    }

    private static bool TryCreatePackageSource(
        string? value,
        out PluginUriInstallSource? source,
        out string? error)
    {
        source = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = Text("Plugins.UriParser.Error.PackageSourceEmpty", "URI 插件包来源不能为空。");
            return false;
        }

        value = value.Trim();
        if (!PluginRemoteInstallService.LooksLikePackageUrl(value))
        {
            error = Text("Plugins.UriParser.Error.PackageMustBePclxOrZip", "URI 插件包必须是 .pclx 或 .zip 文件。");
            return false;
        }
        if (File.Exists(value))
        {
            source = new PluginUriInstallSource(Path.GetFullPath(value), PluginUriInstallSourceKind.LocalPackage);
            return true;
        }
        if (!IsAbsoluteHttpUri(value))
        {
            error = Text("Plugins.UriParser.Error.InvalidLocalOrRemotePackage", "URI 本地插件包不存在，远程插件包必须使用 HTTP 或 HTTPS 地址。");
            return false;
        }
        source = new PluginUriInstallSource(value, PluginUriInstallSourceKind.RemotePackage);
        return true;
    }

    private static bool TryGetExplicitRepositoryKind(
        UriActionRequest request,
        out PluginRepositorySourceKind kind,
        out string? error)
    {
        error = null;
        string? value = null;
        string? key = null;
        foreach (var candidate in new[] { "sourceKind", "kind", "type" })
        {
            if (!request.Query.TryGetValue(candidate, out var current) || string.IsNullOrWhiteSpace(current)) continue;
            if (candidate.Equals("type", StringComparison.OrdinalIgnoreCase)
                && (current.Equals(request.ActionType, StringComparison.OrdinalIgnoreCase)
                    || current.Equals(request.Command, StringComparison.OrdinalIgnoreCase)))
                continue;
            value = current;
            key = candidate;
            break;
        }
        if (value is null)
        {
            kind = default;
            return false;
        }

        if (TryParseRepositoryKind(value, out kind)) return true;
        error = Text("Plugins.UriParser.Error.UnsupportedSourceKey", "URI 插件源 {0} 仅支持 json 或 manifest。", key);
        return false;
    }

    private static bool TryParseRepositoryKind(string value, out PluginRepositorySourceKind kind)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "json": kind = PluginRepositorySourceKind.Json; return true;
            case "manifest": kind = PluginRepositorySourceKind.Manifest; return true;
            default: kind = default; return false;
        }
    }

    private static PluginRepositorySourceKind? StripSourcePrefix(ref string value)
    {
        foreach (var (prefix, kind) in new[]
                 {
                     ("manifest:", PluginRepositorySourceKind.Manifest),
                     ("json:", PluginRepositorySourceKind.Json)
                 })
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            value = value[prefix.Length..].Trim();
            return kind;
        }
        return null;
    }

    private static PluginRepositorySourceKind InferRepositoryKind(string value)
    {
        if (File.Exists(value)) return PluginRepositorySourceKind.Json;
        if (IsAbsoluteHttpUri(value))
        {
            var path = new Uri(value).AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)
                || path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                return PluginRepositorySourceKind.Manifest;
            return PluginRepositorySourceKind.Json;
        }
        return PluginRepositorySourceKind.Json;
    }

    private static bool LooksLikeGitSource(string value)
    {
        if (PluginRemoteInstallService.IsGitSource(value)) return true;
        if (value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) return true;
        return IsAbsoluteHttpUri(value);
    }

    private static bool IsAbsoluteHttpUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool TryGetValue(
        IReadOnlyDictionary<string, string> values,
        out string? value,
        params string[] keys)
    {
        value = GetFirstValue(values, keys);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetFirstValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
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
