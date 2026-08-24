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
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Java/JavaVirtualMachines";
            yield return "/opt/homebrew/opt/openjdk";
            yield return "/usr/local/opt/openjdk";
            yield return Path.Combine(home, "Library", "Java", "JavaVirtualMachines");
            yield return Path.Combine(home, ".minecraft", "runtime");
            yield return Path.Combine(home, ".sdkman", "candidates", "java");
        }
    }
}
