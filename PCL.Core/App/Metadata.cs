using System.Text.Json.Serialization;
namespace PCL.Core.App;

/// <summary>
/// 启动器版本信息模型
/// </summary>
/// <param name="BaseVersion">严格的 <c>yyyy.MM.patch</c> 基本版本号。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LauncherVersionModel(
    [property: JsonPropertyName("base"), JsonRequired] LauncherBaseVersion BaseVersion
)
{
    public override string ToString() => BaseVersion.ToString();
}

/// <summary>
/// 第三方内容许可信息模型
/// </summary>
/// <param name="Name">内容名称</param>
/// <param name="Information">许可信息</param>
/// <param name="WebsiteUri">网站 URI</param>
/// <param name="LicenseUri">许可证 URI</param>
public sealed record ThirdPartyLicenseModel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("info")] string Information,
    [property: JsonPropertyName("website")] string? WebsiteUri = null,
    [property: JsonPropertyName("license")] string? LicenseUri = null
);

/// <summary>
/// 启动器元数据模型
/// </summary>
/// <param name="Name">程序名称</param>
/// <param name="Version">版本信息</param>
public sealed record MetadataModel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] LauncherVersionModel Version,
    [property: JsonPropertyName("licenses")] ThirdPartyLicenseModel[] Licenses
);
