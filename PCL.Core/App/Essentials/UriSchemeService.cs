using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using PCL.Core.App.IoC;

namespace PCL.Core.App.Essentials;

public sealed record UriActionRequest(
    string Scheme,
    string Command,
    string? ActionType,
    string? Data,
    string RawUri,
    IReadOnlyList<string> PathArguments,
    IReadOnlyDictionary<string, string> Query);

[LifecycleService(LifecycleState.BeforeLoading, Priority = int.MaxValue - 100)]
[LifecycleScope("uri-scheme", "URI Scheme", false)]
public sealed partial class UriSchemeService
{
    public static readonly string[] SupportedSchemes = ["pcl", "pclnex"];

    public static readonly string[] SupportedPluginPackageExtensions = [".pclx"];

    private static readonly HashSet<string> _SupportedSchemeSet = new(SupportedSchemes, StringComparer.OrdinalIgnoreCase);

    [LifecycleStart]
    private static void _Start()
    {
        try
        {
            RegisterUriSchemes();
        }
        catch (Exception ex)
        {
            Context.Warn("URI Scheme 注册失败", ex);
        }
    }

    public static void RegisterUriSchemes()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var scheme in SupportedSchemes)
            RegisterUriScheme(scheme);
        RegisterPluginPackageFileAssociations();
    }

    public static string[] NormalizeFullCommandLineArguments(string[] args)
    {
        if (args.Length <= 1) return args;

        var normalized = new List<string>(args.Length);
        normalized.Add(args[0]);
        foreach (var arg in args.Skip(1))
        {
            if (TryConvertUriArgument(arg, out var convertedArgs))
                normalized.AddRange(convertedArgs);
            else if (TryConvertPluginPackageArgument(arg, out convertedArgs))
                normalized.AddRange(convertedArgs);
            else
                normalized.Add(arg);
        }
        return normalized.ToArray();
    }

    public static bool TryConvertUriArgument(string argument, out string[] args)
    {
        args = [];
        if (!TryParseUriAction(argument, out _)) return false;
        args = ["uri", "--uri", argument];
        return true;
    }

    public static bool TryConvertPluginPackageArgument(string argument, out string[] args)
    {
        args = [];
        if (string.IsNullOrWhiteSpace(argument)) return false;
        var path = argument.Trim('"');
        if (!SupportedPluginPackageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return false;
        args = ["uri", "--action", "install-plugin", "--file", path];
        return true;
    }

    public static bool TryParseUriAction(string argument, out UriActionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(argument)) return false;
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri)) return false;
        if (!_SupportedSchemeSet.Contains(uri.Scheme)) return false;

        var segments = GetPathSegments(uri);
        if (!TryGetCommand(uri, segments, out var command, out var commandSegmentIndex)) return false;

        var actionSegments = commandSegmentIndex == 0 ? segments.Skip(1).ToArray() : segments;
        var query = ParseQuery(uri.Query);

        var actionType = command.Equals("actions", StringComparison.OrdinalIgnoreCase)
            ? GetFirstValue(query, "type", "event", "action", "name")
            : command;
        if (string.IsNullOrWhiteSpace(actionType) && actionSegments.Length > 0)
            actionType = actionSegments[0];

        var data = GetFirstValue(query, "data", "arg", "args", "parameter", "value");
        if (data is null)
        {
            if (command.Equals("actions", StringComparison.OrdinalIgnoreCase) && actionSegments.Length > 1)
                data = string.Join('/', actionSegments.Skip(1));
            else if (!command.Equals("actions", StringComparison.OrdinalIgnoreCase) && actionSegments.Length > 0)
                data = string.Join('/', actionSegments);
        }

        var pathArguments = command.Equals("actions", StringComparison.OrdinalIgnoreCase) && actionSegments.Length > 0
            ? actionSegments.Skip(1).ToArray()
            : actionSegments;

        request = new UriActionRequest(uri.Scheme, command, actionType, data, argument, pathArguments, query);
        return true;
    }

    private static void RegisterUriScheme(string scheme)
    {
        using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
        if (schemeKey is null) return;

        schemeKey.SetValue(string.Empty, "URL:Plain Craft Launcher Nex", RegistryValueKind.String);
        schemeKey.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
        schemeKey.SetValue("FriendlyTypeName", "Plain Craft Launcher Nex URI", RegistryValueKind.String);

        using var iconKey = schemeKey.CreateSubKey("DefaultIcon");
        iconKey?.SetValue(string.Empty, $"\"{Basics.ExecutablePath}\",0", RegistryValueKind.String);

        using var commandKey = schemeKey.CreateSubKey(@"shell\open\command");
        commandKey?.SetValue(string.Empty, $"\"{Basics.ExecutablePath}\" \"%1\"", RegistryValueKind.String);
    }

    private static void RegisterPluginPackageFileAssociations()
    {
        foreach (var extension in SupportedPluginPackageExtensions)
        {
            using var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
            extensionKey?.SetValue(string.Empty, "PCL.PluginPackage", RegistryValueKind.String);
            extensionKey?.SetValue("Content Type", "application/vnd.pcl.plugin-package", RegistryValueKind.String);
            extensionKey?.SetValue("PerceivedType", "compressed", RegistryValueKind.String);
        }

        using var packageKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\PCL.PluginPackage");
        if (packageKey is null) return;

        packageKey.SetValue(string.Empty, "PCL 插件包", RegistryValueKind.String);
        using var iconKey = packageKey.CreateSubKey("DefaultIcon");
        iconKey?.SetValue(string.Empty, $"\"{Basics.ExecutablePath}\",0", RegistryValueKind.String);

        using var commandKey = packageKey.CreateSubKey(@"shell\open\command");
        commandKey?.SetValue(string.Empty, $"\"{Basics.ExecutablePath}\" \"%1\"", RegistryValueKind.String);
    }

    private static string[] GetPathSegments(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0) return [];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(UrlDecode)
            .ToArray();
    }

    private static bool TryGetCommand(Uri uri, string[] segments, out string command, out int commandSegmentIndex)
    {
        if (!string.IsNullOrWhiteSpace(uri.Host))
        {
            command = UrlDecode(uri.Host);
            commandSegmentIndex = -1;
            return true;
        }
        if (segments.Length > 0)
        {
            command = segments[0];
            commandSegmentIndex = 0;
            return true;
        }
        command = string.Empty;
        commandSegmentIndex = -1;
        return false;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return result;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var key = separatorIndex < 0 ? pair : pair[..separatorIndex];
            if (key.Length == 0) continue;
            var value = separatorIndex < 0 ? string.Empty : pair[(separatorIndex + 1)..];
            result[UrlDecode(key)] = UrlDecode(value);
        }
        return result;
    }

    private static string? GetFirstValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value))
                return value;
        return null;
    }

    private static string UrlDecode(string value) => Uri.UnescapeDataString(value.Replace("+", " "));
}
