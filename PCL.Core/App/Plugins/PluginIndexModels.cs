using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件索引与包模型专用 JSON 序列化配置。
/// 与 <see cref="PCL.Core.Utils.JsonCompat.SerializerOptions"/> 独立，
/// 因为索引/包模型需要自定义 <see cref="Version"/> 转换器，
/// 而全局配置已被冻结、无法追加。
/// </summary>
public static class PluginJson
{
    /// <summary>
    /// 序列化/反序列化插件索引与包元数据使用的统一配置。
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = _CreateOptions();

    private static JsonSerializerOptions _CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(),
                new VersionJsonConverter()
            }
        };
        options.MakeReadOnly(true);
        return options;
    }

    /// <summary>
    /// <see cref="System.Version"/> 的 JSON 转换器。
    /// 将版本号字符串（如 "1.2.3"）与 <see cref="Version"/> 互转。
    /// </summary>
    private sealed class VersionJsonConverter : JsonConverter<Version>
    {
        public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (!string.IsNullOrWhiteSpace(text) && Version.TryParse(text, out var v))
                    return v;
            }
            return new Version(0, 0, 0, 0);
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}

/// <summary>
/// 插件市场索引 <c>index.json</c> 的根对象。
/// 该文件由开发组审核维护，客户端只展示已允许进入市场的插件条目。
/// </summary>
public sealed class PluginRepositoryIndex
{
    /// <summary>市场索引名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>市场索引描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>仓库主页 URL。</summary>
    [JsonPropertyName("homepageUrl")]
    public string? HomepageUrl { get; set; }

    /// <summary>维护者。</summary>
    [JsonPropertyName("maintainer")]
    public string? Maintainer { get; set; }

    /// <summary>允许进入市场的插件列表。</summary>
    [JsonPropertyName("plugins")]
    public List<PluginRepositoryEntry> Plugins { get; set; } = [];
}

/// <summary>
/// 市场索引中的单个插件条目。
/// </summary>
public sealed class PluginRepositoryEntry
{
    /// <summary>插件唯一标识符。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>人类可读名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>插件描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>作者。</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>市场展示版本号字符串，通常为最新可用版本。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>市场展示用的最低 API 版本。实际下载版本仍以 manifest 中的版本条目为准。</summary>
    [JsonPropertyName("minApiVersion")]
    public Version? MinApiVersion { get; set; }

    /// <summary>市场展示用的最高兼容 API 版本。实际下载版本仍以 manifest 中的版本条目为准。</summary>
    [JsonPropertyName("maxApiVersion")]
    public Version? MaxApiVersion { get; set; }

    /// <summary>市场展示用的最低启动器版本。实际下载版本仍以 manifest 中的版本条目为准。</summary>
    [JsonPropertyName("minHostVersion")]
    public string? MinHostVersion { get; set; }

    /// <summary>市场展示用的最高兼容启动器版本。实际下载版本仍以 manifest 中的版本条目为准。</summary>
    [JsonPropertyName("maxHostVersion")]
    public string? MaxHostVersion { get; set; }

    /// <summary>插件声明的能力列表。</summary>
    [JsonPropertyName("capabilities")]
    public PluginCapabilities[] Capabilities { get; set; } = [];

    /// <summary>开发者维护的市场 manifest URL。索引只负责指向审核通过的 manifest。</summary>
    [JsonPropertyName("manifestUrl")]
    public string? ManifestUrl { get; set; }

    /// <summary>插件主页 URL。</summary>
    [JsonPropertyName("homepageUrl")]
    public string? HomepageUrl { get; set; }

    /// <summary>兼容 package.json 风格的主页 URL。</summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>源仓库 URL。</summary>
    [JsonPropertyName("sourceRepoUrl")]
    public string? SourceRepoUrl { get; set; }

    /// <summary>npm package.json 风格的 repository 字段。</summary>
    [JsonPropertyName("repository")]
    public PluginRepositoryMetadata? Repository { get; set; }

    /// <summary>图标 URL。</summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>截图 URL 列表。</summary>
    [JsonPropertyName("screenshots")]
    public string[]? Screenshots { get; set; }

    /// <summary>发布说明。</summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>许可证标识。</summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>信任级别描述。</summary>
    [JsonPropertyName("trustLevel")]
    public string? TrustLevel { get; set; }

    /// <summary>审核人或审核组织。</summary>
    [JsonPropertyName("reviewedBy")]
    public string? ReviewedBy { get; set; }

    /// <summary>审核通过时间。</summary>
    [JsonPropertyName("reviewedAt")]
    public DateTime? ReviewedAt { get; set; }

    /// <summary>开发者 manifest 的自定义元数据。</summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, JsonElement>? Custom { get; set; }

}

/// <summary>
/// 开发者维护的插件发布 manifest。
/// 负责声明可下载版本和兼容范围；市场展示元数据应放在 index.json。
/// </summary>
public sealed class PluginMarketManifest
{
    /// <summary>可下载版本列表。客户端会选择当前启动器和 API 兼容的最高版本。</summary>
    [JsonPropertyName("versions")]
    public List<PluginMarketVersion> Versions { get; set; } = [];
}

/// <summary>
/// 插件发布 manifest 中的一个可下载版本。
/// </summary>
public sealed class PluginMarketVersion
{
    /// <summary>插件版本号字符串。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>插件包下载地址，必须指向 .pclx 或 .zip。</summary>
    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>包的 SHA-256 哈希。</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>该下载版本所需的最低插件 API / JSAPI 版本。</summary>
    [JsonPropertyName("minApiVersion")]
    public Version? MinApiVersion { get; set; }

    /// <summary>该下载版本最高兼容插件 API / JSAPI 版本。</summary>
    [JsonPropertyName("maxApiVersion")]
    public Version? MaxApiVersion { get; set; }

    /// <summary>该下载版本所需的最低启动器版本。</summary>
    [JsonPropertyName("minHostVersion")]
    public string? MinHostVersion { get; set; }

    /// <summary>该下载版本最高兼容启动器版本。</summary>
    [JsonPropertyName("maxHostVersion")]
    public string? MaxHostVersion { get; set; }

    /// <summary>发布说明。</summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>该版本的自定义元数据。</summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, JsonElement>? Custom { get; set; }
}

/// <summary>
/// npm package.json 风格的仓库元数据。
/// </summary>
[JsonConverter(typeof(PluginRepositoryMetadataJsonConverter))]
public sealed class PluginRepositoryMetadata
{
    /// <summary>仓库类型，如 git。</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>仓库 URL。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>仓库子目录。</summary>
    [JsonPropertyName("directory")]
    public string? Directory { get; set; }
}

public sealed class PluginRepositoryMetadataJsonConverter : JsonConverter<PluginRepositoryMetadata>
{
    public override PluginRepositoryMetadata? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var url = reader.GetString();
            return string.IsNullOrWhiteSpace(url) ? null : new PluginRepositoryMetadata { Type = "git", Url = url };
        }
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("repository must be a string or object.");

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var metadata = new PluginRepositoryMetadata();
        if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            metadata.Type = type.GetString();
        if (root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
            metadata.Url = urlProp.GetString();
        if (root.TryGetProperty("directory", out var directory) && directory.ValueKind == JsonValueKind.String)
            metadata.Directory = directory.GetString();
        return metadata;
    }

    public override void Write(Utf8JsonWriter writer, PluginRepositoryMetadata value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(value.Type)) writer.WriteString("type", value.Type);
        if (!string.IsNullOrWhiteSpace(value.Url)) writer.WriteString("url", value.Url);
        if (!string.IsNullOrWhiteSpace(value.Directory)) writer.WriteString("directory", value.Directory);
        writer.WriteEndObject();
    }
}

/// <summary>
/// 市场插件条目中的一个可选安装源。
/// </summary>
public sealed class PluginInstallSourceEntry
{
    /// <summary>来源类型，支持 <c>package</c>，并兼容旧 <c>git</c>。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>来源地址。</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>可选 Git 引用（分支或 tag）。为空时安装默认分支。</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>可选 SHA-256 校验值。</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}

/// <summary>
/// 插件包内的 <c>plugin.json</c> manifest。
/// 用于安装阶段的元数据校验，与运行时程序集特性区分开。
/// </summary>
public sealed class PluginPackageManifest
{
    public const string RuntimeDotNet = "dotnet";
    public const string RuntimeJavaScriptV8 = "javascript-v8";

    /// <summary>插件唯一标识符。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>人类可读名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号。</summary>
    [JsonPropertyName("version")]
    public Version Version { get; set; } = new(0, 0, 0, 0);

    /// <summary>作者。</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 插件运行时。默认 <c>dotnet</c>；JavaScript 插件使用 <c>javascript-v8</c>。
    /// </summary>
    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = RuntimeDotNet;

    /// <summary>
    /// 主程序集相对路径（相对于包根目录），如 <c>lib/HelloPlugin.dll</c>。
    /// </summary>
    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>
    /// JavaScript 入口脚本相对路径（相对于包根目录），如 <c>main.js</c>。
    /// </summary>
    [JsonPropertyName("entryScript")]
    public string EntryScript { get; set; } = string.Empty;

    /// <summary>所需的最低 API 版本。</summary>
    [JsonPropertyName("minApiVersion")]
    public Version MinApiVersion { get; set; } = new(0, 0, 0, 0);

    /// <summary>最高兼容 API 版本。为空表示不限制上限。</summary>
    [JsonPropertyName("maxApiVersion")]
    public Version? MaxApiVersion { get; set; }

    /// <summary>所需的最低启动器版本（SemVer 字符串）。为空表示不限制。</summary>
    [JsonPropertyName("minHostVersion")]
    public string? MinHostVersion { get; set; }

    /// <summary>最高兼容启动器版本（SemVer 字符串）。为空表示不限制。</summary>
    [JsonPropertyName("maxHostVersion")]
    public string? MaxHostVersion { get; set; }

    /// <summary>声明的能力列表。</summary>
    [JsonPropertyName("capabilities")]
    public PluginCapabilities[] Capabilities { get; set; } = [];

    /// <summary>主页 URL。</summary>
    [JsonPropertyName("homepageUrl")]
    public string? HomepageUrl { get; set; }

    /// <summary>许可证标识。</summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>图标相对路径。</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>启动加载窗口图标相对路径。宿主会在显示 SplashScreen 前尝试读取。</summary>
    [JsonPropertyName("startupIcon")]
    public string? StartupIcon { get; set; }

    /// <summary>主窗口任务栏/标题栏图标相对路径。JavaScript 插件可在运行时读取并应用。</summary>
    [JsonPropertyName("windowIcon")]
    public string? WindowIcon { get; set; }

    /// <summary>标题栏 Logo 图片相对路径。JavaScript 插件可在运行时读取并应用。</summary>
    [JsonPropertyName("titleLogo")]
    public string? TitleLogo { get; set; }

    /// <summary>截图相对路径列表。</summary>
    [JsonPropertyName("screenshots")]
    public string[]? Screenshots { get; set; }

    public bool IsJavaScriptPlugin()
        => string.Equals(NormalizedRuntime, RuntimeJavaScriptV8, StringComparison.OrdinalIgnoreCase);

    public bool IsDotNetPlugin()
        => string.Equals(NormalizedRuntime, RuntimeDotNet, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string NormalizedRuntime => string.IsNullOrWhiteSpace(Runtime) ? RuntimeDotNet : Runtime.Trim();
}

