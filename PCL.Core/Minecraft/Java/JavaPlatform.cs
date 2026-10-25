using System;
using System.Collections.Generic;
using System.IO;
using PCL.Core.IO;

namespace PCL.Core.Minecraft.Java;

public static class JavaPlatform
{
    public static string ExecutableName => OperatingSystem.IsWindows() ? "java.exe" : "java";
    public static string CompilerName => OperatingSystem.IsWindows() ? "javac.exe" : "javac";
    public static string? GuiExecutableName => OperatingSystem.IsWindows() ? "javaw.exe" : null;
    public static string GetToolExecutableName(string toolName) => OperatingSystem.IsWindows()
        ? toolName + ".exe"
        : toolName;
    public static StringComparer PathComparer => FileSystemPath.Comparer;
    public static StringComparison PathComparison => FileSystemPath.Comparison;

    /// <summary>
    ///     用于信任系统根证书的 JVM 参数。
    /// </summary>
    /// <remarks>
    ///     <c>WINDOWS-ROOT</c> 仅由 Windows 的 SunMSCAPI 提供。在 macOS/Linux 上传入该类型会令
    ///     默认 <c>SSLContext</c> 构建失败（<c>KeyManagementException: problem accessing trust store</c>），
    ///     进而导致所有需要 HTTPS 的模组初始化失败（PCL-Nex#17、PCL-Nex#19）。
    ///     其他平台使用 Java 自带的默认信任库即可，因此返回 <see langword="null" />。
    /// </remarks>
    public static string? GetSystemTrustStoreTypeArgument() =>
        OperatingSystem.IsWindows() ? "-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT" : null;

    /// <summary>
    ///     可能指向 Java 安装目录的环境变量，其下应存在 <c>bin/java</c>。
    /// </summary>
    public static readonly string[] HomeEnvironmentVariables = ["JAVA_HOME", "JDK_HOME", "JRE_HOME"];

    public static IEnumerable<string> GetPlatformSearchRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsLinux())
        {
            yield return "/usr/lib/jvm";
            yield return "/usr/java";
            yield return "/opt/java";
            yield return "/opt/jdk";
            yield return Path.Combine(home, ".minecraft", "runtime");
            yield return Path.Combine(home, ".sdkman", "candidates", "java");
            yield return Path.Combine(home, ".asdf", "installs", "java");
            yield return Path.Combine(home, ".jdks");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Java/JavaVirtualMachines";
            yield return "/System/Library/Java/JavaVirtualMachines";
            // Homebrew 的 openjdk、openjdk@17 等版本化安装均位于 opt 下
            yield return "/opt/homebrew/opt";
            yield return "/usr/local/opt";
            yield return Path.Combine(home, "Library", "Java", "JavaVirtualMachines");
            yield return Path.Combine(home, "Library", "Application Support", "minecraft", "runtime");
            yield return Path.Combine(home, ".minecraft", "runtime");
            yield return Path.Combine(home, ".sdkman", "candidates", "java");
            yield return Path.Combine(home, ".jdks");
        }
    }
}
