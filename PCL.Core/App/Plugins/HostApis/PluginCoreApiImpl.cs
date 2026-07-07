using System;
using System.Globalization;
using System.Threading;
using PCL.Core.App.Plugins;
using PCL.Core.Logging;
using PCL.Plugin.Abstractions;
using HintType = PCL.Plugin.Abstractions.PluginHintType;

namespace PCL.Core.App.Plugins.HostApis;

/// <summary>
/// 插件核心 API 实现。通过 <see cref="PluginHostBridge"/> 间接访问宿主能力。
/// </summary>
internal sealed class PluginCoreApiImpl(string pluginId) : IPluginCoreApi
{
    public IPluginLogger GetLogger(string category)
    {
        var prefix = string.IsNullOrWhiteSpace(category)
            ? $"[Plugin:{pluginId}] "
            : $"[Plugin:{pluginId}/{category}] ";
        return new LoggerImpl(prefix);
    }

    public void Hint(string message, PluginHintType type = HintType.Info)
    {
        var bridge = PluginHostBridge.Current;
        if (bridge is not null)
        {
            bridge.Notify(message, (int)type);
            return;
        }
        // 桥接尚未注册时退化为日志
        _RawLog($"[Plugin:{pluginId}] [Hint:{type}] {message}");
    }

    public string CurrentLanguage =>
        PluginHostBridge.Current?.CurrentLanguage ??
        CultureInfo.CurrentUICulture.Name;

    public string Localize(string key, string? fallback = null)
    {
        var bridge = PluginHostBridge.Current;
        if (bridge is not null) return bridge.Localize(key, fallback ?? key);
        return fallback ?? key;
    }

    public string HostVersion =>
        PluginHostBridge.Current?.HostVersion ?? "0.0.0";

    private static void _RawLog(string text)
    {
        try { LogService.Logger?.Log(text); }
        catch { /* 忽略日志服务不可用 */ }
    }

    private sealed class LoggerImpl(string prefix) : IPluginLogger
    {
        public void Trace(string message, Exception? exception = null) => _Write("TRA", message, exception);
        public void Debug(string message, Exception? exception = null) => _Write("DBG", message, exception);
        public void Info(string message, Exception? exception = null) => _Write("INFO", message, exception);
        public void Warn(string message, Exception? exception = null) => _Write("WARN", message, exception);
        public void Error(string message, Exception? exception = null) => _Write("ERR!", message, exception);

        private void _Write(string level, string message, Exception? ex)
        {
            var text = ex is null
                ? $"{prefix}{level} {message}"
                : $"{prefix}{level} {message} | {ex.GetType().Name}: {ex.Message}";
            try { LogService.Logger?.Log(text); }
            catch { /* 日志服务不可用时静默忽略，避免插件崩溃 */ }
        }
    }
}
