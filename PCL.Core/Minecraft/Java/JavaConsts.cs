namespace PCL.Core.Minecraft.Java;
public class JavaConsts
{
    public static readonly string[] ExcludeFolderNames = ["javapath", "java8path", "common files", "netease"];

    public static readonly string[] MostPossibleKeywords =
    [
        "java", "jdk", "jre",
        "dragonwell", "azul", "zulu", "oracle", "open", "amazon", "corretto",
        "eclipse", "temurin", "hotspot", "semeru", "kona", "bellsoft"
    ];

    public static readonly string[] PossibleKeywords =
    [
        "environment", "env", "runtime", "x86_64", "amd64", "arm64", "x64",
        "pcl", "hmcl", "baka", "minecraft"
    ];

    /// <summary>
    ///     Mojang 运行时目录（<c>runtime/&lt;组件&gt;/&lt;平台&gt;/&lt;组件&gt;/bin/java</c>）中的平台目录名。
    ///     这些名称本身不含 Java 关键词，需要显式识别才能继续向下查找。
    /// </summary>
    public static readonly string[] RuntimePlatformFolderNames =
    [
        "linux", "linux-i386",
        "mac-os", "mac-os-arm64",
        "windows-x86", "windows-x64", "windows-arm64"
    ];

    public static readonly string[] AllKeywords = [.. PossibleKeywords, .. MostPossibleKeywords];
}
