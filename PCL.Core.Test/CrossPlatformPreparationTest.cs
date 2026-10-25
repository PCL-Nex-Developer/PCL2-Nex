using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.IO;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Secret;

namespace PCL.Core.Test;

[TestClass]
public sealed class CrossPlatformPreparationTest
{
    [TestMethod]
    public void JavaRuntimeProperties_ParseModernVersionAndArchitecture()
    {
        const string output = """
                                  Property settings:
                                      java.vendor = Eclipse Adoptium
                                      java.version = 21.0.2+13-LTS
                                      os.arch = aarch64
                                  """;

        Assert.AreEqual(new Version(21, 0, 2), PeHeaderParser.ParseVersion(output));
        Assert.AreEqual((MachineType.ARM64, true), PeHeaderParser.ParseArchitecture(output));
    }

    [TestMethod]
    public void JavaRuntimeProperties_ParseLegacyVersion()
    {
        const string output = "java.version = 1.8.0_402";

        Assert.AreEqual(new Version(1, 8, 0, 402), PeHeaderParser.ParseVersion(output));
    }

    [TestMethod]
    public void JavaPlatform_UsesNativeExecutableNamesAndPathRules()
    {
        var expectedExecutable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var expectedCompiler = OperatingSystem.IsWindows() ? "javac.exe" : "javac";

        Assert.AreEqual(expectedExecutable, JavaPlatform.ExecutableName);
        Assert.AreEqual(expectedCompiler, JavaPlatform.CompilerName);
        Assert.AreEqual(OperatingSystem.IsWindows(), JavaPlatform.GuiExecutableName is not null);
    }

    [TestMethod]
    public void JavaPlatform_SystemTrustStoreTypeArgument_IsWindowsOnly()
    {
        var argument = JavaPlatform.GetSystemTrustStoreTypeArgument();

        if (OperatingSystem.IsWindows())
            Assert.AreEqual("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT", argument);
        else
            Assert.IsNull(argument, "非 Windows 平台不能传入 WINDOWS-ROOT，否则默认 SSLContext 无法构建");
    }

    [TestMethod]
    public void DefaultPathsScanner_EnumeratesMacOsJdkBundles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PCLNex-JavaScannerTest-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "temurin-21.jdk", "Contents", "Home");
        try
        {
            Directory.CreateDirectory(Path.Combine(home, "bin"));
            File.WriteAllText(Path.Combine(home, "bin", JavaPlatform.ExecutableName), string.Empty);

            var homes = DefaultPathsScanner.EnumerateMacOsJdkHomes(root).ToList();

            CollectionAssert.Contains(homes, home);
            Assert.HasCount(0,
                DefaultPathsScanner.EnumerateMacOsJdkHomes(Path.Combine(root, "missing")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void DefaultPathsScanner_FindsMojangRuntimeUnderPlatformFolder()
    {
        // Mojang 官方运行时布局：runtime/<组件>/<平台>/<组件>/bin/java
        var root = Path.Combine(Path.GetTempPath(), "PCLNex-JavaRuntimeTest-" + Guid.NewGuid().ToString("N"));
        var javaExe = Path.Combine(root, "java-runtime-gamma", "mac-os", "java-runtime-gamma", "bin",
            JavaPlatform.ExecutableName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(javaExe)!);
            File.WriteAllText(javaExe, string.Empty);

            var results = new List<string>();
            DefaultPathsScanner._BfsSearch(root, results);

            CollectionAssert.Contains(results, javaExe);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PhysicalMemory_IsReportedWithValidBounds()
    {
        var (total, available) = KernelInterop.GetPhysicalMemoryBytes();

        Assert.IsGreaterThan(0UL, total);
        Assert.IsLessThanOrEqualTo(total, available);
    }

    [TestMethod]
    public async Task DirectoryPermissionProbe_WorksForWritableDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PCLNex-PermissionTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.IsTrue(await Directories.CheckPermissionAsync(directory));
            await Directories.CheckPermissionWithExceptionAsync(directory);
            Assert.HasCount(0, Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void PlatformIdentityAndSystemRoot_AreStableAndUsable()
    {
        Assert.IsNotEmpty(Identify.RawId);
        CollectionAssert.AreEqual(Identify.RawId, Identify.RawId);
        Assert.IsTrue(Path.IsPathRooted(SystemPaths.DriveLetter));
    }

    [TestMethod]
    public void FileSystemPath_UsesPlatformComparisonAndRejectsSiblingPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-root");
        var child = Path.Combine(root, "plugins", "sample");
        var sibling = root + "-other";

        Assert.IsTrue(FileSystemPath.IsWithinDirectory(child, root));
        Assert.IsFalse(FileSystemPath.IsWithinDirectory(sibling, root));
        Assert.IsFalse(FileSystemPath.IsWithinDirectory(root, root));
        Assert.IsTrue(FileSystemPath.IsWithinDirectory(root, root, allowEqual: true));
        Assert.AreEqual(OperatingSystem.IsWindows(),
            FileSystemPath.Equals(child, child.ToUpperInvariant()));
        Assert.IsTrue(Path.EndsInDirectorySeparator(FileSystemPath.EnsureTrailingSeparator(root)));

        var systemRoot = Path.GetPathRoot(Path.GetFullPath(root))!;
        Assert.IsTrue(FileSystemPath.IsWithinDirectory(Path.Combine(systemRoot, "pcl-child"), systemRoot));
    }

    [TestMethod]
    public void FileSystemPath_NormalizesLegacyAndPortableSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-root");
        var expected = Path.Combine(root, "PCL", "Pictures");
        var legacyPath = expected.Replace(Path.DirectorySeparatorChar, '\\');

        Assert.AreEqual(expected, FileSystemPath.NormalizeSeparators(legacyPath));
        Assert.AreEqual(expected, FileSystemPath.Combine(root, @"PCL\Pictures"));
        Assert.AreEqual(expected, FileSystemPath.Combine(root, "PCL/Pictures"));
        Assert.IsTrue(FileSystemPath.IsWithinDirectory(legacyPath, root));
    }
}
