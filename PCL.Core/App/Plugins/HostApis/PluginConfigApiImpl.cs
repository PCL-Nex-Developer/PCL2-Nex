using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.HostApis;

/// <summary>
/// 插件配置 API 实现。每个插件拥有一个位于其数据目录下的 <c>config.ini</c>，
/// 采用扁平的 <c>key=value</c> 格式，键名自动加上插件命名空间前缀以防冲突。
/// </summary>
internal sealed class PluginConfigApiImpl(string dataDirectory, string pluginId) : IPluginConfigApi
{
    private string ConfigFile => Path.Combine(dataDirectory, "config.ini");

    private static string SafeKey(string key) => key.Replace("\r", "").Replace("\n", "").Replace("=", "");

    private string _Read(string key, string defaultValue)
    {
        key = SafeKey(key);
        try
        {
            if (!File.Exists(ConfigFile)) return defaultValue;
            var prefix = key + "=";
            foreach (var line in File.ReadAllLines(ConfigFile))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return _Unescape(line[prefix.Length..]);
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private void _Write(string key, string? value)
    {
        key = SafeKey(key);
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var lines = File.Exists(ConfigFile)
                ? new List<string>(File.ReadAllLines(ConfigFile))
                : [];
            var prefix = key + "=";
            var found = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (value is null) lines.RemoveAt(i);
                    else lines[i] = prefix + _Escape(value);
                    found = true;
                    break;
                }
            }
            if (!found && value is not null) lines.Add(prefix + _Escape(value));
            File.WriteAllLines(ConfigFile, lines);
        }
        catch
        {
            /* 配置写入失败不应影响插件主流程 */
        }
    }

    private static string _Escape(string v) => v.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
    private static string _Unescape(string v) => v.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");

    public string GetString(string key, string defaultValue = "") => _Read(key, defaultValue);

    public int GetInt(string key, int defaultValue = 0)
    {
        var raw = _Read(key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var raw = _Read(key, defaultValue ? "true" : "false");
        return bool.TryParse(raw, out var v) ? v : defaultValue;
    }

    public double GetDouble(string key, double defaultValue = 0d)
    {
        var raw = _Read(key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    public void Set(string key, string value) => _Write(key, value);
    public void Set(string key, int value) => _Write(key, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string key, bool value) => _Write(key, value ? "true" : "false");
    public void Set(string key, double value) => _Write(key, value.ToString(CultureInfo.InvariantCulture));

    public bool Contains(string key) => _TryHas(key);

    private bool _TryHas(string key)
    {
        key = SafeKey(key);
        try
        {
            if (!File.Exists(ConfigFile)) return false;
            var prefix = key + "=";
            return File.ReadAllLines(ConfigFile).Any(l => l.StartsWith(prefix, StringComparison.Ordinal));
        }
        catch { return false; }
    }

    public void Remove(string key) => _Write(key, null);

    public IEnumerable<string> Keys()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return [];
            return File.ReadAllLines(ConfigFile)
                .Where(l => l.Contains('='))
                .Select(l => SafeKey(l[..l.IndexOf('=')]))
                .ToList();
        }
        catch { return []; }
    }
}
