using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Mixin;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
[DoNotParallelize]
public sealed class PluginLoaderEndToEndTest
{
    [TestMethod]
    public async Task InstalledDirectory_ShouldLoadApplyOptionalFailureAndRollbackRealPluginAssembly()
    {
        const string pluginId = "test.loader.e2e";
        var root = CreatePackage(pluginId,
            ("main.mixins.json", MainConfiguration),
            ("optional.mixins.json", OptionalMissingConfiguration));
        var originalSafeMode = PluginLoaderService.SafeMode;
        try
        {
            PluginLoaderService.SafeMode = false;
            Assert.IsFalse(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest()));

            await PluginLoaderService.LoadAllFromDirectoryAsync(
                root,
                isEnabled: id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase),
                enabledOrder: [pluginId],
                disableFailedPlugins: false);

            AssertLoadedWithOptionalWarning(pluginId);
            Assert.IsTrue(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest()));

            PluginLoaderService.RollbackLoadedPluginForTesting(pluginId);
            Assert.IsFalse(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest()));
            Assert.IsFalse(PluginLoaderService.LoadedPlugins.Any(item =>
                string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            PluginLoaderService.RollbackLoadedPluginForTesting(pluginId);
            PluginLoaderService.SafeMode = originalSafeMode;
            DeleteAfterCollectibleUnload(root);
        }
    }

    [TestMethod]
    public async Task RequiredConfigurationFailure_ShouldRollbackEarlierConfiguration()
    {
        const string pluginId = "test.loader.required-failure";
        var root = CreatePackage(pluginId,
            ("main.mixins.json", MainConfiguration),
            ("required-failure.mixins.json", RequiredMissingConfiguration));
        var originalSafeMode = PluginLoaderService.SafeMode;
        try
        {
            PluginLoaderService.SafeMode = false;
            await PluginLoaderService.LoadAllFromDirectoryAsync(
                root,
                isEnabled: id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase),
                enabledOrder: [pluginId],
                disableFailedPlugins: false);

            Assert.IsFalse(PluginLoaderService.LoadedPlugins.Any(item =>
                string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest()));
        }
        finally
        {
            PluginLoaderService.RollbackLoadedPluginForTesting(pluginId);
            PluginLoaderService.SafeMode = originalSafeMode;
            DeleteAfterCollectibleUnload(root);
        }
    }

    [TestMethod]
    public async Task SafeMode_ShouldSkipPackageBeforeLoadingItsAssembly()
    {
        const string pluginId = "test.loader.safe-mode";
        var root = CreatePackage(pluginId, ("main.mixins.json", MainConfiguration));
        var originalSafeMode = PluginLoaderService.SafeMode;
        try
        {
            PluginLoaderService.SafeMode = true;
            await PluginLoaderService.LoadAllFromDirectoryAsync(
                root,
                isEnabled: id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase),
                enabledOrder: [pluginId],
                disableFailedPlugins: false);

            Assert.IsFalse(PluginLoaderService.LoadedPlugins.Any(item =>
                string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase)));
            PluginLoaderService.SafeMode = false;
            Assert.IsFalse(PluginLoaderService.ShouldSkipThirdPartyMixins(new PluginPackageManifest()));
        }
        finally
        {
            PluginLoaderService.RollbackLoadedPluginForTesting(pluginId);
            PluginLoaderService.SafeMode = originalSafeMode;
            DeleteAfterCollectibleUnload(root);
        }
    }

    private static void AssertLoadedWithOptionalWarning(string pluginId)
    {
        var record = PluginLoaderService.LoadedPlugins.Single(item =>
            string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEqual(new[] { "main.mixins.json" }, record.AppliedMixinConfigurations.ToArray());
        Assert.IsTrue(record.Warnings.Any(warning =>
            warning.Contains("optional.mixins.json", StringComparison.Ordinal)));
    }

    private static void DeleteAfterCollectibleUnload(string directory)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static string CreatePackage(string pluginId, params (string Name, string Json)[] configurations)
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-loader-e2e-" + Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, pluginId);
        Directory.CreateDirectory(pluginDirectory);
        File.Copy(typeof(PluginLoaderEndToEndMixin).Assembly.Location, Path.Combine(pluginDirectory, "plugin.dll"));
        foreach (var configuration in configurations)
            File.WriteAllText(Path.Combine(pluginDirectory, configuration.Name), configuration.Json);

        var manifest = new PluginPackageManifest
        {
            Id = pluginId,
            Name = "Loader end-to-end test",
            Version = "1.0.0",
            Author = "PCL.Core.Test",
            PclCoreVersion = PluginCompatibility.MinimumSupportedPclCoreVersion,
            EntryAssembly = "plugin.dll",
            MixinConfigs = configurations.Select(configuration => configuration.Name).ToArray()
        };
        File.WriteAllText(
            Path.Combine(pluginDirectory, "plugin.json"),
            JsonSerializer.Serialize(manifest, PluginJson.SerializerOptions));
        return root;
    }

    private const string MainConfiguration = """
        {
          "required": true,
          "mixins": ["PCL.Core.Test.App.Plugins.PluginLoaderEndToEndMixin"],
          "priority": 1000,
          "injectors": { "defaultRequire": 1 }
        }
        """;

    private const string OptionalMissingConfiguration = """
        {
          "required": false,
          "mixins": ["PCL.Core.Test.App.Plugins.MissingOptionalMixin"],
          "priority": 1000,
          "injectors": { "defaultRequire": 1 }
        }
        """;

    private const string RequiredMissingConfiguration = """
        {
          "required": true,
          "mixins": ["PCL.Core.Test.App.Plugins.MissingRequiredMixin"],
          "priority": 1000,
          "injectors": { "defaultRequire": 1 }
        }
        """;
}

[Mixin(typeof(PluginLoaderService))]
public static class PluginLoaderEndToEndMixin
{
    [Inject(nameof(PluginLoaderService.ShouldSkipThirdPartyMixins), At = MixinAt.Head, Cancellable = true)]
    public static void ForceSkip(CallbackInfo<bool> callback) => callback.SetReturnValue(true);
}
