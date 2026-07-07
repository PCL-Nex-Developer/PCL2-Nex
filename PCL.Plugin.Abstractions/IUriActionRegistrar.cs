using System;
using System.Collections.Generic;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// URI Scheme 动作注册能力。<br/>
/// 插件可注册形如 <c>pcl://plugin?plugin=&lt;id&gt;&amp;action=&lt;name&gt;</c> 的外部唤起动作。
/// </summary>
public interface IUriActionRegistrar
{
    /// <summary>
    /// 注册一个插件 URI 动作。
    /// </summary>
    /// <param name="descriptor">动作描述符</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterAction(UriActionDescriptor descriptor);
}

/// <summary>
/// 插件 URI 动作处理委托。
/// </summary>
/// <param name="context">外部 URI 动作上下文</param>
public delegate void UriActionHandler(PluginUriActionContext context);

/// <summary>
/// URI 动作描述符。
/// </summary>
public sealed class UriActionDescriptor
{
    /// <summary>动作名（不含插件 ID，建议仅使用小写字母、数字、连字符）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>简短描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>用法说明。</summary>
    public string Usage { get; init; } = string.Empty;

    /// <summary>处理委托。</summary>
    public required UriActionHandler Handler { get; init; }
}

/// <summary>
/// 传递给插件 URI 动作的上下文。
/// </summary>
public sealed class PluginUriActionContext
{
    public required string Scheme { get; init; }
    public required string RawUri { get; init; }
    public required string Action { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? GetQueryValue(string key)
        => Query.TryGetValue(key, out var value) ? value : null;
}