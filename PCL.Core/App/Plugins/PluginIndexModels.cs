using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 插件索引与包模型专用 JSON 序列化配置。
/// 与 <see cref="PCL.Core.Utils.JsonCompat.SerializerOptions"/> 独立，
/// 与 <see cref="PCL.Core.Utils.JsonCompat.SerializerOptions"/> 独立，避免市场和 PCLX 契约
/// 被启动器内部 JSON 兼容逻辑宽松解析。
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
                new JsonStringEnumConverter()
            }
        };
        options.MakeReadOnly(true);
        return options;
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

    /// <summary>内联 README Markdown。</summary>
    [JsonPropertyName("readme")]
    public string? Readme { get; set; }

    /// <summary>README Markdown 地址，可为网络 URL 或相对来源 JSON 的路径。</summary>
    [JsonPropertyName("readmeUrl")]
    public string? ReadmeUrl { get; set; }

    /// <summary>作者。</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>市场展示版本号字符串，通常为最新可用版本。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

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

    /// <summary>插件 Logo。可为网络 URL，也可由来源文件解析相对路径。</summary>
    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("group")]
    public string? Group { get; set; }

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

    [JsonPropertyName("githubLogin")]
    public string? GitHubLogin { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("fork")]
    public bool Fork { get; set; }

    [JsonIgnore]
    public PluginMarketManifest? MarketManifest { get; set; }

    [JsonIgnore]
    public PluginMarketVersion? SelectedVersion { get; set; }

    [JsonIgnore]
    public PluginMarketDownload? SelectedDownload { get; set; }

    [JsonIgnore]
    public PluginCoreCompatibilityStatus CompatibilityStatus { get; set; } = PluginCoreCompatibilityStatus.Unknown;

    [JsonIgnore]
    public PluginDeveloperTrustLevel DeveloperTrustLevel { get; set; } = PluginDeveloperTrustLevel.Other;

    [JsonIgnore]
    public string SourceKind { get; set; } = "Plugins";

    [JsonIgnore]
    public string SourceGroup { get; set; } = "Plugins";

    /// <summary>GitHub 仓库中 manifest.json 的最后提交时间。</summary>
    [JsonIgnore]
    public DateTimeOffset? LastUpdatedAt { get; set; }

    /// <summary>GitHub Releases 所有资产的累计下载次数。</summary>
    [JsonIgnore]
    public long? DownloadCount { get; set; }

    [JsonIgnore]
    public bool SourceIsOfficial { get; set; }

    /// <summary>ManifestUrl 是否直接指向单插件 manifest，而不是包含 plugins 的来源 JSON。</summary>
    [JsonIgnore]
    public bool ManifestUrlIsDirect { get; set; }

}

/// <summary>
/// 开发者维护的插件发布 manifest。
/// 负责声明可下载版本和兼容范围；市场展示元数据应放在 index.json。
/// </summary>
public sealed class PluginMarketManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public PluginMarketAuthor? Author { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>内联 README Markdown。</summary>
    [JsonPropertyName("readme")]
    public string? Readme { get; set; }

    /// <summary>README Markdown 地址，可为网络 URL 或相对来源 JSON 的路径。</summary>
    [JsonPropertyName("readmeUrl")]
    public string? ReadmeUrl { get; set; }

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("homepageUrl")]
    public string? HomepageUrl { get; set; }

    /// <summary>插件 Logo，可为绝对网络 URL或相对 manifest 的路径。</summary>
    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>所有版本默认使用的前置插件；版本条目可单独覆盖。</summary>
    [JsonPropertyName("dependencies")]
    public List<PluginDependency> Dependencies { get; set; } = [];

    /// <summary>可下载版本列表。客户端按 pclCoreVersion 和当前平台选择最高版本。</summary>
    [JsonPropertyName("versions")]
    public List<PluginMarketVersion> Versions { get; set; } = [];

    /// <summary>Nex_Server 预索引元数据；开发者仓库中的 manifest.json 无需声明。</summary>
    [JsonPropertyName("index")]
    public PluginMarketIndexMetadata? Index { get; set; }
}

public sealed class PluginMarketIndexMetadata
{
    [JsonPropertyName("manifestUrl")]
    public string ManifestUrl { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdatedAt")]
    public DateTimeOffset? LastUpdatedAt { get; set; }

    [JsonPropertyName("downloadCount")]
    public long DownloadCount { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("fork")]
    public bool Fork { get; set; }
}

/// <summary>
/// 插件发布 manifest 中的一个可下载版本。
/// </summary>
public sealed class PluginMarketVersion
{
    /// <summary>由 Topic 仓库旧版 versions-only manifest 显式转换；不属于 manifest.json 契约。</summary>
    [JsonIgnore]
    internal bool IsLegacyRepositoryVersion { get; set; }

    /// <summary>验证 manifest 后关联的市场插件 ID；不属于 manifest.json 契约。</summary>
    [JsonIgnore]
    public string? PluginId { get; set; }

    /// <summary>插件版本号字符串。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>当前平台解析后的插件包下载地址；不属于 manifest.json 契约。</summary>
    [JsonIgnore]
    public string ResolvedPackageUrl { get; set; } = string.Empty;

    /// <summary>当前平台解析后的包 SHA-256；不属于 manifest.json 契约。</summary>
    [JsonIgnore]
    public string? ResolvedSha256 { get; set; }

    /// <summary>构建该插件版本时引用的 PCL.Core BaseVersion。</summary>
    [JsonPropertyName("pclCoreVersion")]
    public string? PclCoreVersion { get; set; }

    /// <summary>该版本的前置插件；未声明时继承 manifest 顶层 dependencies。</summary>
    [JsonPropertyName("dependencies")]
    public List<PluginDependency>? Dependencies { get; set; }

    /// <summary>完成 manifest 校验后解析出的最终前置插件列表。</summary>
    [JsonIgnore]
    public IReadOnlyList<PluginDependency>? ResolvedDependencies { get; internal set; }

    [JsonPropertyName("downloads")]
    public PluginMarketDownloads? Downloads { get; set; }

    /// <summary>发布说明。</summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>该版本的自定义元数据。</summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, JsonElement>? Custom { get; set; }

    /// <summary>用于检测并拒绝旧版顶层 packageUrl/sha256 等非契约字段。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class PluginMarketAuthor
{
    [JsonPropertyName("githubLogin")]
    public string GitHubLogin { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class PluginMarketDownloads
{
    [JsonPropertyName("windows")]
    public PluginMarketArchitectureDownloads? Windows { get; set; }

    [JsonPropertyName("linux")]
    public PluginMarketArchitectureDownloads? Linux { get; set; }

    [JsonPropertyName("macos")]
    public PluginMarketArchitectureDownloads? MacOS { get; set; }

    // Legacy OS-neutral download keys remain supported for existing manifests.
    [JsonPropertyName("amd64")]
    public PluginMarketDownload? Amd64 { get; set; }

    [JsonPropertyName("arm64")]
    public PluginMarketDownload? Arm64 { get; set; }

    [JsonPropertyName("anycpu")]
    public PluginMarketDownload? AnyCpu { get; set; }
}

public sealed class PluginMarketArchitectureDownloads
{
    [JsonPropertyName("amd64")]
    public PluginMarketDownload? Amd64 { get; set; }

    [JsonPropertyName("arm64")]
    public PluginMarketDownload? Arm64 { get; set; }

    [JsonPropertyName("anycpu")]
    public PluginMarketDownload? AnyCpu { get; set; }
}

public sealed class PluginMarketDownload
{
    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}

/// <summary>
/// 插件前置依赖。前置仍是普通 PCL.Mixin 插件，也可自行作为 Python、JavaScript
/// 或其他公共能力的 Bridge；Core 只负责依赖、版本、程序集共享和加载顺序。
/// </summary>
public sealed class PluginDependency
{
    /// <summary>前置插件 ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>SemVer 约束，例如 <c>&gt;=1.0.0 &lt;2.0.0</c>；空值或 * 表示任意版本。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
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
    /// <summary>插件唯一标识符。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>人类可读名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>作者。</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>内联 README Markdown。</summary>
    [JsonPropertyName("readme")]
    public string? Readme { get; set; }

    /// <summary>README Markdown 地址，可为网络 URL 或相对 manifest 的路径。</summary>
    [JsonPropertyName("readmeUrl")]
    public string? ReadmeUrl { get; set; }

    /// <summary>构建插件时引用的 PCL.Core BaseVersion。</summary>
    [JsonPropertyName("pclCoreVersion")]
    public string? PclCoreVersion { get; set; }

    /// <summary>
    /// 主程序集相对路径（相对于包根目录），如 <c>lib/HelloPlugin.dll</c>。
    /// </summary>
    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>单个 Mixin 配置文件相对路径。</summary>
    [JsonPropertyName("mixinConfig")]
    public string? MixinConfig { get; set; }

    /// <summary>多个 Mixin 配置文件相对路径。</summary>
    [JsonPropertyName("mixinConfigs")]
    public string[] MixinConfigs { get; set; } = [];

    /// <summary>
    /// 可由用户逐项启用的实验功能。每一项都必须声明自己独占的 Mixin 配置，
    /// 未选中时不会参与启动时的注入。
    /// </summary>
    [JsonPropertyName("experimentalFeatures")]
    public List<PluginExperimentalFeature> ExperimentalFeatures { get; set; } = [];

    /// <summary>
    /// 必须先安装、启用并成功加载的前置插件。依赖插件的公共程序集会共享给当前插件，
    /// 可用于实现 Bridge，而无需由 Core 内置脚本运行时或通用 Host API。
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<PluginDependency> Dependencies { get; set; } = [];

    /// <summary>主页 URL。</summary>
    [JsonPropertyName("homepageUrl")]
    public string? HomepageUrl { get; set; }

    /// <summary>许可证标识。</summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>图标相对路径。</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>插件 Logo，可为包内相对路径或 HTTP/HTTPS URL。</summary>
    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    /// <summary>截图相对路径列表。</summary>
    [JsonPropertyName("screenshots")]
    public string[]? Screenshots { get; set; }

    /// <summary>用于在安装阶段检测并拒绝旧插件入口字段。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    public IReadOnlyList<string> GetMixinConfigurationPaths()
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(MixinConfig)) result.Add(MixinConfig.Trim());
        result.AddRange((MixinConfigs ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()));
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>返回安装时必须存在的全部 Mixin 配置，包括尚未启用的实验功能。</summary>
    public IReadOnlyList<string> GetAllMixinConfigurationPaths()
    {
        var result = new List<string>(GetMixinConfigurationPaths());
        foreach (var feature in ExperimentalFeatures ?? [])
        {
            if (feature is null) continue;
            result.AddRange(feature.GetMixinConfigurationPaths());
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>返回本次启动应应用的基础和已启用实验功能的 Mixin 配置。</summary>
    public IReadOnlyList<string> GetEnabledMixinConfigurationPaths(IEnumerable<string>? enabledFeatureIds)
    {
        var enabled = new HashSet<string>(enabledFeatureIds ?? [], StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(GetMixinConfigurationPaths());
        foreach (var feature in ExperimentalFeatures ?? [])
        {
            if (feature is null) continue;
            if (enabled.Contains(feature.Id)) result.AddRange(feature.GetMixinConfigurationPaths());
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>根据 Mixin 配置路径查询其所属实验功能。</summary>
    public PluginExperimentalFeature? FindExperimentalFeatureByMixinConfiguration(string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(configurationPath)) return null;
        return (ExperimentalFeatures ?? []).FirstOrDefault(feature => feature is not null &&
            feature.GetMixinConfigurationPaths().Any(path =>
                string.Equals(path, configurationPath.Trim(), StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>插件包内可单独启用的实验功能声明。</summary>
public sealed class PluginExperimentalFeature
{
    /// <summary>包内稳定标识符，用于保存用户选择。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>面向用户的简短说明。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>该实验功能的来源 PR 地址。</summary>
    [JsonPropertyName("pullRequestUrl")]
    public string? PullRequestUrl { get; set; }

    /// <summary>该实验功能专属的单个 Mixin 配置。</summary>
    [JsonPropertyName("mixinConfig")]
    public string? MixinConfig { get; set; }

    /// <summary>该实验功能专属的多个 Mixin 配置。</summary>
    [JsonPropertyName("mixinConfigs")]
    public string[] MixinConfigs { get; set; } = [];

    public IReadOnlyList<string> GetMixinConfigurationPaths()
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(MixinConfig)) result.Add(MixinConfig.Trim());
        result.AddRange((MixinConfigs ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()));
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

/// <summary>
/// 插件市场来源文档。一个 JSON 可同时声明开发者、额外 manifest 地址和内联插件。
/// </summary>
public sealed class PluginMarketSourceDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// 此商店源声明的开发者集合。官方内置源中的 official 条目授予官方身份；
    /// 用户添加的第三方源中的条目只授予用户信任身份。
    /// </summary>
    [JsonPropertyName("developers")]
    public List<PluginDeveloperRecord> Developers { get; set; } = [];

    [JsonPropertyName("manifests")]
    public List<string> Manifests { get; set; } = [];

    [JsonPropertyName("plugins")]
    public List<PluginMarketManifest> Plugins { get; set; } = [];
}

