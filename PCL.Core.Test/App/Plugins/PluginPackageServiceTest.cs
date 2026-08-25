using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginPackageServiceTest
{
    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingEntryAssembly()
    {
        var result = PluginPackageService.ValidatePackageManifest(CreateValidManifest());
        Assert.IsTrue(result.IsValid);

        var manifest = CreateValidManifest();
        manifest.EntryAssembly = string.Empty;
        result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "EntryAssembly");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingMixinConfiguration()
    {
        var manifest = CreateValidManifest();
        manifest.MixinConfig = null;
        manifest.MixinConfigs = [];

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "mixinConfig");
        StringAssert.Contains(result.ErrorMessage!, "LoadAsync/UnloadAsync");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectLegacyLifecycleFieldsEvenWithMixinConfig()
    {
        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(
            """{"id":"com.example.legacy","name":"Legacy","version":"1.0.0","pclCoreVersion":"2026.07.1","entryAssembly":"lib/Legacy.dll","mixinConfig":"mixins.json","loadMethod":"LoadAsync"}""",
            PluginJson.SerializerOptions)!;

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "loadMethod");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingIdNameAndInvalidVersion()
    {
        var missingId = CreateValidManifest();
        missingId.Id = string.Empty;
        StringAssert.Contains(PluginPackageService.ValidatePackageManifest(missingId).ErrorMessage!, "Id");

        var missingName = CreateValidManifest();
        missingName.Name = string.Empty;
        StringAssert.Contains(PluginPackageService.ValidatePackageManifest(missingName).ErrorMessage!, "Name");

        var invalidVersion = CreateValidManifest();
        invalidVersion.Version = "invalid";
        StringAssert.Contains(PluginPackageService.ValidatePackageManifest(invalidVersion).ErrorMessage!, "Version");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldApplyPclCoreVersionRules()
    {
        var compatible = CreateValidManifest();
        compatible.PclCoreVersion = "2026.07.1";
        Assert.AreEqual(
            PluginCoreCompatibilityStatus.Compatible,
            PluginPackageService.ValidatePackageManifest(compatible, "2026.07.1").CompatibilityStatus);

        var old = CreateValidManifest();
        old.PclCoreVersion = "2026.06.9";
        Assert.IsFalse(PluginPackageService.ValidatePackageManifest(old, "2026.07.1").IsValid);

        var future = CreateValidManifest();
        future.PclCoreVersion = "2026.08.1";
        var futureResult = PluginPackageService.ValidatePackageManifest(future, "2026.07.1");
        Assert.IsTrue(futureResult.IsValid);
        Assert.AreEqual(PluginCoreCompatibilityStatus.Future, futureResult.CompatibilityStatus);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task StartupCompatibility_ShouldConfirmFuturePluginBeforeApplyingMixins()
    {
        var manifest = CreateValidManifest();
        manifest.PclCoreVersion = "2026.08.1";
        var originalHandler = PluginCompatibility.ConfirmationAsync;
        try
        {
            var calls = 0;
            PluginCompatibility.ConfirmationAsync = (context, _) =>
            {
                calls++;
                Assert.AreEqual(PluginCompatibilityAction.Enable, context.Action);
                Assert.AreEqual(PluginCoreCompatibilityStatus.Future, context.Status);
                return Task.FromResult(false);
            };

            Assert.IsFalse(await PluginLoaderService.IsPackageCompatibleAsync(manifest, CancellationToken.None));
            Assert.AreEqual(1, calls);

            PluginCompatibility.ConfirmationAsync = (_, _) => Task.FromResult(true);
            Assert.IsTrue(await PluginLoaderService.IsPackageCompatibleAsync(manifest, CancellationToken.None));
        }
        finally
        {
            PluginCompatibility.ConfirmationAsync = originalHandler;
        }
    }

    [TestMethod]
    public void MixinConfigurationPaths_ShouldCombineSingularAndPluralWithoutDuplicates()
    {
        var manifest = CreateValidManifest();
        manifest.MixinConfig = "mixins.main.json";
        manifest.MixinConfigs = ["mixins.extra.json", "MIXINS.MAIN.JSON"];

        CollectionAssert.AreEqual(
            new[] { "mixins.main.json", "mixins.extra.json" },
            manifest.GetMixinConfigurationPaths().ToArray());
    }

    [TestMethod]
    public void ExperimentalFeatures_ShouldValidateAndOnlyExposeSelectedMixinConfigurations()
    {
        var manifest = CreateValidManifest();
        manifest.MixinConfig = null;
        manifest.MixinConfigs = [];
        manifest.ExperimentalFeatures =
        [
            new PluginExperimentalFeature
            {
                Id = "keyboard-step",
                Name = "Keyboard step",
                PullRequestUrl = "https://github.com/example/repository/pull/1",
                MixinConfig = "mixins/keyboard-step.json"
            },
            new PluginExperimentalFeature
            {
                Id = "url-normalize",
                Name = "URL normalize",
                MixinConfigs = ["mixins/url-normalize.json"]
            }
        ];

        Assert.IsTrue(PluginPackageService.ValidatePackageManifest(manifest).IsValid);
        CollectionAssert.AreEqual(
            new[] { "mixins/keyboard-step.json", "mixins/url-normalize.json" },
            manifest.GetAllMixinConfigurationPaths().ToArray());
        CollectionAssert.AreEqual(
            new[] { "mixins/url-normalize.json" },
            manifest.GetEnabledMixinConfigurationPaths(["url-normalize"]).ToArray());
        Assert.AreEqual("keyboard-step", manifest.FindExperimentalFeatureByMixinConfiguration("mixins/keyboard-step.json")!.Id);
    }

    [TestMethod]
    public void ExperimentalFeatures_ShouldRejectSharedOrMissingMixinConfigurations()
    {
        var manifest = CreateValidManifest();
        manifest.ExperimentalFeatures =
        [
            new PluginExperimentalFeature
            {
                Id = "bad-feature",
                Name = "Bad feature",
                MixinConfig = "mixins.json"
            }
        ];

        var sharedWithBase = PluginPackageService.ValidatePackageManifest(manifest);
        Assert.IsFalse(sharedWithBase.IsValid);
        StringAssert.Contains(sharedWithBase.ErrorMessage!, "共享");

        manifest.MixinConfig = null;
        manifest.ExperimentalFeatures[0].MixinConfig = null;
        var missingConfiguration = PluginPackageService.ValidatePackageManifest(manifest);
        Assert.IsFalse(missingConfiguration.IsValid);
        StringAssert.Contains(missingConfiguration.ErrorMessage!, "Mixin 配置");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldAllowNetworkOrLocalLogoAndRejectTraversal()
    {
        var local = CreateValidManifest();
        local.Logo = "assets/logo.png";
        Assert.IsTrue(PluginPackageService.ValidatePackageManifest(local).IsValid);

        var network = CreateValidManifest();
        network.Logo = "https://cdn.example.test/logo.png";
        Assert.IsTrue(PluginPackageService.ValidatePackageManifest(network).IsValid);

        var traversal = CreateValidManifest();
        traversal.Logo = "../logo.png";
        Assert.IsFalse(PluginPackageService.ValidatePackageManifest(traversal).IsValid);
    }

    [TestMethod]
    public async Task ReadAndValidateDirectoryAsync_ShouldReadMixinPluginJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pcl_plugin_manifest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest = CreateValidManifest();
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "plugin.json"),
                JsonSerializer.Serialize(manifest, PluginJson.SerializerOptions));

            var (readManifest, result) = await PluginPackageService.ReadAndValidateDirectoryAsync(tempDir);

            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(readManifest);
            Assert.AreEqual("com.example.mixin", readManifest.Id);
            Assert.AreEqual("mixins.json", readManifest.MixinConfig);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void ValidatePackageManifest_Null_ShouldReject()
        => Assert.IsFalse(PluginPackageService.ValidatePackageManifest(null!).IsValid);

    private static PluginPackageManifest CreateValidManifest() => new()
    {
        Id = "com.example.mixin",
        Name = "Mixin Plugin",
        Version = "1.0.0",
        Author = "Example",
        PclCoreVersion = "2026.07.1",
        EntryAssembly = "lib/Example.Plugin.dll",
        MixinConfig = "mixins.json"
    };
}
