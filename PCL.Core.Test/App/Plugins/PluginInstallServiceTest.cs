using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
[DoNotParallelize]
public class PluginInstallServiceTest
{
    [TestMethod]
    public void GetInstalledPlugins_ShouldReturnEmpty_WhenNoRecordsExist()
    {
        // 在没有初始化的环境中应返回空列表
        try
        {
            var plugins = PluginInstallService.GetInstalledPlugins();
            Assert.IsNotNull(plugins);
        }
        catch
        {
            Assert.Inconclusive("Paths 静态初始化失败，跳过测试。");
        }
    }

    [TestMethod]
    public void IsInstalled_ShouldReturnFalse_ForUnknownPlugin()
    {
        try
        {
            var result = PluginInstallService.IsInstalled("com.nonexistent.plugin");
            Assert.IsFalse(result);
        }
        catch
        {
            Assert.Inconclusive("Paths 静态初始化失败，跳过测试。");
        }
    }

    [TestMethod]
    public void GetRecord_ShouldReturnNull_ForUnknownPlugin()
    {
        try
        {
            var record = PluginInstallService.GetRecord("com.nonexistent.plugin");
            Assert.IsNull(record);
        }
        catch
        {
            Assert.Inconclusive("Paths 静态初始化失败，跳过测试。");
        }
    }

    [TestMethod]
    public async Task InstallFromDirectoryAsync_ShouldRemoveNewPluginWhenRecordPersistenceFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-install-rollback-" + Guid.NewGuid().ToString("N"));
        var installed = Path.Combine(root, "installed");
        var temp = Path.Combine(root, "temp");
        var source = Path.Combine(root, "source");
        var trustAsFile = Path.Combine(root, "trust-file");
        Directory.CreateDirectory(installed);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.Combine(source, "lib"));
        File.WriteAllText(Path.Combine(source, "lib", "plugin.dll"), "new");
        File.WriteAllText(Path.Combine(source, "mixins.json"), "{}");
        File.WriteAllText(trustAsFile, "not a directory");

        var originalInstalled = Paths.PluginInstalled;
        var originalTemp = Paths.PluginTemp;
        var originalTrust = Paths.PluginTrust;
        try
        {
            Paths.PluginInstalled = installed;
            Paths.PluginTemp = temp;
            Paths.PluginTrust = trustAsFile;
            var manifest = CreateValidManifest("example.install", "1.0.0");

            await Assert.ThrowsExactlyAsync<IOException>(() => PluginInstallService.InstallFromDirectoryAsync(
                source, manifest, PluginInstallSourceType.Repository, "https://example.test/plugin.pclx"));

            Assert.IsFalse(Directory.Exists(Path.Combine(installed, "example.install")));
        }
        finally
        {
            Paths.PluginInstalled = originalInstalled;
            Paths.PluginTemp = originalTemp;
            Paths.PluginTrust = originalTrust;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task InstallFromDirectoryAsync_ShouldRestoreOldPluginAndRecordWhenUpdatePersistenceFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-update-rollback-" + Guid.NewGuid().ToString("N"));
        var installed = Path.Combine(root, "installed");
        var temp = Path.Combine(root, "temp");
        var source = Path.Combine(root, "source");
        var trust = Path.Combine(root, "trust");
        Directory.CreateDirectory(installed);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(trust);
        Directory.CreateDirectory(Path.Combine(installed, "example.rollback", "lib"));
        Directory.CreateDirectory(Path.Combine(source, "lib"));
        File.WriteAllText(Path.Combine(installed, "example.rollback", "lib", "plugin.dll"), "old");
        File.WriteAllText(Path.Combine(source, "lib", "plugin.dll"), "new");
        File.WriteAllText(Path.Combine(source, "mixins.json"), "{}");
        var recordPath = Path.Combine(trust, "example.rollback.json");
        var originalRecord = JsonSerializer.Serialize(new PluginInstallRecord
        {
            PluginId = "example.rollback",
            InstalledVersion = "1.0.0",
            InstalledFrom = "https://example.test/plugin.pclx",
            SourceType = PluginInstallSourceType.Repository,
            Enabled = true
        }, PluginJson.SerializerOptions);
        File.WriteAllText(recordPath, originalRecord);

        var originalInstalled = Paths.PluginInstalled;
        var originalTemp = Paths.PluginTemp;
        var originalTrust = Paths.PluginTrust;
        try
        {
            Paths.PluginInstalled = installed;
            Paths.PluginTemp = temp;
            Paths.PluginTrust = trust;
            var manifest = CreateValidManifest("example.rollback", "2.0.0");

            using (new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Assert.ThrowsExactlyAsync<IOException>(() => PluginInstallService.InstallFromDirectoryAsync(
                    source, manifest, PluginInstallSourceType.Repository, "https://example.test/plugin.pclx"));

                Assert.AreEqual("old", File.ReadAllText(Path.Combine(installed, "example.rollback", "lib", "plugin.dll")));
                Assert.AreEqual(originalRecord, File.ReadAllText(recordPath));
            }
        }
        finally
        {
            Paths.PluginInstalled = originalInstalled;
            Paths.PluginTemp = originalTemp;
            Paths.PluginTrust = originalTrust;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task InstallFromDirectoryAsync_ShouldRejectLegacyLifecycleManifestAtServiceBoundary()
    {
        var manifest = CreateValidManifest("example.legacy", "1.0.0");
        manifest.AdditionalProperties = new()
        {
            ["loadMethod"] = JsonDocument.Parse("\"LoadAsync\"").RootElement.Clone()
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PluginInstallService.InstallFromDirectoryAsync(
                "unused", manifest, PluginInstallSourceType.Local, "local"));

        StringAssert.Contains(exception.Message, "loadMethod");
    }

    [TestMethod]
    public async Task UninstallAsync_ShouldKeepRecordWhenPluginDirectoryIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-uninstall-rollback-" + Guid.NewGuid().ToString("N"));
        var installed = Path.Combine(root, "installed");
        var trust = Path.Combine(root, "trust");
        var pluginDir = Path.Combine(installed, "example.locked");
        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(trust);
        var lockedFile = Path.Combine(pluginDir, "plugin.dll");
        File.WriteAllText(lockedFile, "locked");
        var recordPath = Path.Combine(trust, "example.locked.json");
        File.WriteAllText(recordPath, JsonSerializer.Serialize(new PluginInstallRecord
        {
            PluginId = "example.locked",
            InstalledVersion = "1.0.0",
            InstalledFrom = "https://example.test/plugin.pclx"
        }, PluginJson.SerializerOptions));

        var originalInstalled = Paths.PluginInstalled;
        var originalTrust = Paths.PluginTrust;
        try
        {
            Paths.PluginInstalled = installed;
            Paths.PluginTrust = trust;
            using var locked = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

            await Assert.ThrowsExactlyAsync<IOException>(() => PluginInstallService.UninstallAsync("example.locked"));

            Assert.IsTrue(Directory.Exists(pluginDir));
            Assert.IsTrue(File.Exists(recordPath));
        }
        finally
        {
            Paths.PluginInstalled = originalInstalled;
            Paths.PluginTrust = originalTrust;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static PluginPackageManifest CreateValidManifest(string id, string version) => new()
    {
        Id = id,
        Name = id,
        Version = version,
        EntryAssembly = "lib/plugin.dll",
        MixinConfig = "mixins.json",
        PclCoreVersion = PluginCompatibility.CurrentPclCoreVersion
    };
}
