using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 标记一个类型作为插件入口。该类型必须实现 <see cref="IPclPlugin"/> 且具备公共无参构造函数。
/// </summary>
/// <remarks>
/// 建议将此特性应用在插件程序集内一个专门的入口类上。宿主会在加载程序集后扫描该特性。
/// </remarks>
/// <param name="id">插件唯一标识符</param>
/// <param name="name">人类可读名称</param>
/// <param name="version">插件版本号字符串，需可解析为 <see cref="Version"/></param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PluginAttribute(
    string id,
    string name,
    string version = "1.0.0.0")
    : Attribute
{
    /// <summary>插件唯一标识符。</summary>
    public string Id { get; } = id;

    /// <summary>人类可读名称。</summary>
    public string Name { get; } = name;

    /// <summary>插件版本号字符串。</summary>
    public string Version { get; } = version;

    /// <summary>作者。</summary>
    public string? Author { get; set; }

    /// <summary>可选描述。</summary>
    public string? Description { get; set; }

    /// <summary>可选主页 URL。</summary>
    public string? HomePageUrl { get; set; }

    /// <summary>所需的最低 API 版本字符串。</summary>
    public string? MinApiVersion { get; set; }

    /// <summary>最高兼容 API 版本字符串。</summary>
    public string? MaxApiVersion { get; set; }

    /// <summary>所需的最低启动器版本（SemVer 字符串）。</summary>
    public string? MinHostVersion { get; set; }

    /// <summary>最高兼容启动器版本（SemVer 字符串）。</summary>
    public string? MaxHostVersion { get; set; }

    /// <summary>能力标志。</summary>
    public PluginCapabilities Capabilities { get; set; } = PluginCapabilities.None;

    /// <summary>加载时机。</summary>
    public PluginLoadTiming LoadTiming { get; set; } = PluginLoadTiming.WindowCreated;

    /// <summary>
    /// 将特性内容转换为 <see cref="PluginManifest"/>。
    /// </summary>
    public PluginManifest ToManifest()
    {
        Version parsedVersion;
        try { parsedVersion = System.Version.Parse(Version); }
        catch { parsedVersion = new Version(1, 0, 0, 0); }

        Version? minApi = null;
        if (!string.IsNullOrWhiteSpace(MinApiVersion))
        {
            try { minApi = System.Version.Parse(MinApiVersion); }
            catch { /* 忽略无法解析的版本字符串 */ }
        }

        Version? maxApi = null;
        if (!string.IsNullOrWhiteSpace(MaxApiVersion))
        {
            try { maxApi = System.Version.Parse(MaxApiVersion); }
            catch { /* 忽略无法解析的版本字符串 */ }
        }

        return new PluginManifest
        {
            Id = Id,
            Name = Name,
            Version = parsedVersion,
            Author = Author ?? string.Empty,
            Description = Description ?? string.Empty,
            HomePageUrl = HomePageUrl,
            MinApiVersion = minApi,
            MaxApiVersion = maxApi,
            MinHostVersion = MinHostVersion,
            MaxHostVersion = MaxHostVersion,
            EntryPointTypeName = null, // 宿主扫描时自动填充
            Capabilities = Capabilities,
            LoadTiming = LoadTiming
        };
    }
}
