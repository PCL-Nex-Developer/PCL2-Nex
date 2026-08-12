using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Minecraft;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PCL.Core.Test
{
    [TestClass]
    public class JavaTest
    {
        [TestMethod]
        public async Task TestJavaSearch()
        {
            // Java 搜索是否稳定
            var jas = new JavaManager(
                new PeHeaderParser(),
                [
                new RegistryJavaScanner(),
                new DefaultPathsScanner(),
                new PathEnvironmentScanner(),
                new MicrosoftStoreJavaScanner(),
                new WhereCommandScanner()
            ]);
            await jas.ScanJavaAsync();
            var firstSacnned = jas.GetSortedJavaList();
            foreach (var ja in firstSacnned)
            {
                Console.WriteLine(ja.ToString());
                Assert.IsGreaterThan(0, ja.Installation.Version.Major, "Java version is not valid: " + ja.Installation.JavaFolder);
                Assert.IsFalse(string.IsNullOrWhiteSpace(ja.Installation.JavaFolder));
            }
            await jas.ScanJavaAsync();
            var secondScaned = jas.GetSortedJavaList();
            Assert.HasCount(secondScaned.Count, firstSacnned);
            // Java 搜索是否能够正确选择
            Assert.IsTrue(secondScaned.Count == 0 || (secondScaned.Count > 0 && (await jas.SelectSuitableJavaAsync(new Version(1, 8, 0), new Version(30, 0, 0))).Length > 0));
            // Java 是否有重复
            Assert.IsFalse(secondScaned.GroupBy(x => x.Installation.JavaExePath).Any(x => x.Count() > 1));
        }

        [TestMethod]
        public void TestJavaRequirementFromLauncherVersionJson()
        {
            var json = JsonNode.Parse("""
                {
                  "javaVersion": {
                    "component": "java-runtime-epsilon",
                    "majorVersion": 25
                  }
                }
                """)!.AsObject();

            var requirement = JavaRuntimeRequirement.FromVersionJson(json);

            Assert.IsNotNull(requirement);
            Assert.AreEqual(25, requirement.MajorVersion);
            Assert.AreEqual("java-runtime-epsilon", requirement.Component);
            Assert.AreEqual(new Version(25, 0, 0, 0), requirement.MinimumVersion);
        }

        [TestMethod]
        public void TestJavaRequirementFromEmbeddedVersionJson()
        {
            var json = JsonNode.Parse("""
                {
                  "java_component": "java-runtime-gamma",
                  "java_version": 17
                }
                """)!.AsObject();

            var requirement = JavaRuntimeRequirement.FromVersionJson(json);

            Assert.IsNotNull(requirement);
            Assert.AreEqual(17, requirement.MajorVersion);
            Assert.AreEqual("java-runtime-gamma", requirement.Component);
            Assert.AreEqual(new Version(17, 0, 0, 0), requirement.MinimumVersion);
        }

        [TestMethod]
        public void TestJavaRequirementKeepsLegacyJavaVersionFormat()
        {
            var json = JsonNode.Parse("""{"javaVersion":{"majorVersion":8}}""")!.AsObject();

            var requirement = JavaRuntimeRequirement.FromVersionJson(json);

            Assert.IsNotNull(requirement);
            Assert.AreEqual(new Version(1, 8, 0, 0), requirement.MinimumVersion);
        }

        [TestMethod]
        public void TestJavaVersionRangeNormalizesLegacyFormat()
        {
            Assert.IsTrue(JavaManager.IsVersionSuitable(
                new Version(17, 0, 10),
                new Version(1, 17, 0, 0),
                new Version(1, 18, 999, 999)));
            Assert.IsFalse(JavaManager.IsVersionSuitable(
                new Version(19, 0, 1),
                new Version(1, 17, 0, 0),
                new Version(1, 18, 999, 999)));
        }
    [TestMethod]
        public async Task ScanJava_ThenRemoveDirectory_RefreshDropsUnavailableEntries()
        {
            var directory = Path.Combine(Path.GetTempPath(), "PCLNex-JavaTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var javaExe = Path.Combine(directory, "java.exe");
            try
            {
                File.WriteAllBytes(javaExe, []);
                var manager = new JavaManager(new FakeJavaParser(), new FakeJavaScanner(javaExe));

                await manager.ScanJavaAsync(force: true);
                Assert.AreEqual(1, manager.GetSortedJavaList().Count);

                // 卸载 Java：删除扫描到的目录后点击刷新
                Directory.Delete(directory, true);
                await manager.ScanJavaAsync(force: true);

                Assert.AreEqual(0, manager.GetSortedJavaList().Count);
                Assert.IsFalse(manager.Exist(javaExe));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private sealed class FakeJavaParser : IJavaParser
        {
            public JavaInstallation? Parse(string javaExePath)
                => File.Exists(javaExePath)
                    ? new JavaInstallation(
                        Path.GetDirectoryName(javaExePath)!,
                        new Version(17, 0, 10),
                        JavaBrandType.OpenJDK,
                        MachineType.AMD64,
                        true,
                        false)
                    : null;
        }

        private sealed class FakeJavaScanner : IJavaScanner
        {
            private readonly string _javaExePath;

            public FakeJavaScanner(string javaExePath) => _javaExePath = javaExePath;

            public void Scan(ICollection<string> results) => results.Add(_javaExePath);
        }
    }
}
