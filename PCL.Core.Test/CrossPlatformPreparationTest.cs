using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.IO;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Parser;
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
