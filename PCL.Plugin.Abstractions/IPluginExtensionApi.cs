using System;
using System.Collections.Generic;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 通用扩展点注册能力。插件通过命名扩展点向宿主或其他插件贡献能力实例。
/// </summary>
public interface IPluginExtensionApi
{
    /// <summary>
    /// 向指定扩展点注册一个贡献项。返回值必须在插件卸载时释放。
    /// </summary>
    IDisposable Register<TContribution>(PluginExtensionDescriptor<TContribution> descriptor)
        where TContribution : class;
}

/// <summary>
/// 扩展点贡献描述符。
/// </summary>
public sealed class PluginExtensionDescriptor<TContribution> where TContribution : class
{
    /// <summary>扩展点标识，建议使用反向域名或 <c>pcl:</c> 前缀命名。</summary>
    public string ExtensionPoint { get; init; } = string.Empty;

    /// <summary>贡献项唯一标识（在同一插件、同一扩展点内唯一即可）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>人类可读名称，用于宿主 UI 展示或日志。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>排序权重，数值越小越靠前。</summary>
    public int Order { get; init; } = 100;

    /// <summary>可选元数据。宿主可按扩展点约定读取这些键值。</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>实际贡献对象。对象类型由扩展点约定。</summary>
    public required TContribution Contribution { get; init; }
}

/// <summary>
/// PCL 内置扩展点标识。
/// </summary>
public static class PluginExtensionPoints
{
    /// <summary>联机隧道提供者扩展点，贡献对象类型为 <see cref="ILobbyTunnelProvider"/>。</summary>
    public const string LobbyTunnelProvider = "pcl:lobby:tunnel-provider";

    /// <summary>联机功能服务扩展点，贡献对象类型为 <see cref="ILobbyService"/>。</summary>
    public const string LobbyService = "pcl:lobby:service";
}