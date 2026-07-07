using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 插件 SDK 的 API 版本信息。<br/>
/// 插件通过 <see cref="PluginManifest.MinApiVersion"/> 与 <see cref="PluginManifest.MaxApiVersion"/>
/// 声明支持的 SDK 版本范围，
/// 宿主在加载时会据此进行兼容性校验。
/// </summary>
public static class ApiVersions
{
    /// <summary>
    /// 当前 SDK 契约的主版本号。当出现不兼容的契约变更时递增。
    /// </summary>
    public const int Major = 1;

    /// <summary>
    /// 次要版本号。向后兼容的新增能力时递增。
    /// </summary>
    public const int Minor = 2;

    /// <summary>
    /// 修订号。
    /// </summary>
    public const int Patch = 0;

    /// <summary>
    /// 当前 SDK 契约版本。
    /// </summary>
    public static Version Current { get; } = new(Major, Minor, Patch, 0);

    /// <summary>
    /// 判断宿主提供的 API 版本是否满足插件要求的最低版本。<br/>
    /// 规则：宿主主版本号必须与插件要求的主版本号一致，且宿主版本不低于插件要求的版本。
    /// </summary>
    /// <param name="hostApiVersion">宿主实际提供的 API 版本</param>
    /// <param name="minRequired">插件声明的最低 API 版本</param>
    /// <returns>是否兼容</returns>
    public static bool IsCompatible(Version hostApiVersion, Version? minRequired)
        => IsCompatible(hostApiVersion, minRequired, null);

    /// <summary>
    /// 判断宿主提供的 API 版本是否落在插件声明的兼容范围内。<br/>
    /// 任一边界存在时，宿主主版本号必须与该边界主版本号一致。
    /// </summary>
    /// <param name="hostApiVersion">宿主实际提供的 API 版本</param>
    /// <param name="minRequired">插件声明的最低 API 版本</param>
    /// <param name="maxSupported">插件声明的最高兼容 API 版本</param>
    /// <returns>是否兼容</returns>
    public static bool IsCompatible(Version hostApiVersion, Version? minRequired, Version? maxSupported)
    {
        if (minRequired is not null)
        {
            if (hostApiVersion.Major != minRequired.Major) return false;
            if (CompareNormalized(hostApiVersion, minRequired) < 0) return false;
        }

        if (maxSupported is not null)
        {
            if (hostApiVersion.Major != maxSupported.Major) return false;
            if (CompareNormalized(hostApiVersion, maxSupported) > 0) return false;
        }

        return true;
    }

    private static int CompareNormalized(Version left, Version right)
    {
        var compare = left.Major.CompareTo(right.Major);
        if (compare != 0) return compare;

        compare = left.Minor.CompareTo(right.Minor);
        if (compare != 0) return compare;

        compare = NormalizePart(left.Build).CompareTo(NormalizePart(right.Build));
        if (compare != 0) return compare;

        return NormalizePart(left.Revision).CompareTo(NormalizePart(right.Revision));
    }

    private static int NormalizePart(int value) => value < 0 ? 0 : value;
}
