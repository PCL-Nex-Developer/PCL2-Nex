using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 基础核心能力。所有插件均可获取，不受能力标志限制。
/// </summary>
public interface IPluginCoreApi
{
    /// <summary>
    /// 获取插件专属的日志记录器。日志会进入宿主的统一日志通道，并以插件 Id 标记。
    /// </summary>
    IPluginLogger GetLogger(string category);

    /// <summary>
    /// 向用户显示一条短暂提示（toast 风格）。
    /// </summary>
    /// <param name="message">提示文本</param>
    /// <param name="type">提示类型</param>
    void Hint(string message, PluginHintType type = PluginHintType.Info);

    /// <summary>
    /// 获取宿主当前使用的语言代码，如 <c>zh-CN</c>。
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// 尝试获取本地化字符串。若找不到则返回 <paramref name="fallback"/>。
    /// </summary>
    string Localize(string key, string? fallback = null);

    /// <summary>
    /// 宿主版本名称（如 <c>2.15.0</c>）。
    /// </summary>
    string HostVersion { get; }
}

/// <summary>
/// 插件日志接口。
/// </summary>
public interface IPluginLogger
{
    void Trace(string message, Exception? exception = null);
    void Debug(string message, Exception? exception = null);
    void Info(string message, Exception? exception = null);
    void Warn(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// 提示类型。
/// </summary>
public enum PluginHintType
{
    /// <summary>普通信息。</summary>
    Info,
    /// <summary>成功。</summary>
    Success,
    /// <summary>警告。</summary>
    Warning,
    /// <summary>错误。</summary>
    Error
}
