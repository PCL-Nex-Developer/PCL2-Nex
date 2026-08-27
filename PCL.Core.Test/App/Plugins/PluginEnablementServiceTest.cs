using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
[DoNotParallelize]
public class PluginEnablementServiceTest
{
    [TestMethod]
    public void CompareByEnabledOrder_ShouldFollowConfiguredArrayOrder()
    {
        string[] pluginIds = ["com.example.third", "com.example.first", "com.example.second"];
        string[] configuredOrder = ["com.example.first", "com.example.second", "com.example.third"];

        var sorted = pluginIds
            .OrderBy(id => id, Comparer<string>.Create((left, right) =>
                PluginEnablementService.CompareByEnabledOrder(left, right, configuredOrder)))
            .ToArray();

        CollectionAssert.AreEqual(configuredOrder, sorted);
    }

    [TestMethod]
    public void CompareByEnabledOrder_ShouldPlaceUnconfiguredPluginsAfterConfiguredPlugins()
    {
        string[] pluginIds = ["com.example.zeta", "com.example.beta", "com.example.alpha"];
        string[] configuredOrder = ["com.example.beta"];

        var sorted = pluginIds
            .OrderBy(id => id, Comparer<string>.Create((left, right) =>
                PluginEnablementService.CompareByEnabledOrder(left, right, configuredOrder)))
            .ToArray();

        CollectionAssert.AreEqual(new[] { "com.example.beta", "com.example.alpha", "com.example.zeta" }, sorted);
    }

    [TestMethod]
    public void SelfProtectionRecord_ShouldPersistFailureAndNotificationState()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-plugin-protection-" + Guid.NewGuid().ToString("N"));
        var originalPlugins = Paths.Plugins;
        try
        {
            Paths.Plugins = root;
            PluginEnablementService.MarkSelfProtectionDisabled(
                "example.failed",
                "Failed Plugin",
                "1.2.3",
                "Required patch did not match.");

            var record = PluginEnablementService.GetSelfProtectionDisabledPlugin("example.failed");
            Assert.IsNotNull(record);
            Assert.AreEqual("Failed Plugin", record.PluginName);
            Assert.AreEqual("1.2.3", record.PluginVersion);
            Assert.AreEqual("Required patch did not match.", record.Reason);
            Assert.IsFalse(record.NotificationShown);

            PluginEnablementService.MarkSelfProtectionNotificationShown("example.failed");
            record = PluginEnablementService.GetSelfProtectionDisabledPlugin("example.failed");
            Assert.IsNotNull(record);
            Assert.IsTrue(record.NotificationShown);

            PluginEnablementService.ClearSelfProtectionDisabled("example.failed");
            Assert.IsNull(PluginEnablementService.GetSelfProtectionDisabledPlugin("example.failed"));
        }
        finally
        {
            Paths.Plugins = originalPlugins;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void SelfProtectionRecord_ShouldReadLegacyTimestampMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-plugin-protection-legacy-" + Guid.NewGuid().ToString("N"));
        var originalPlugins = Paths.Plugins;
        try
        {
            Paths.Plugins = root;
            var markerDirectory = Path.Combine(root, ".self-protection-disabled");
            Directory.CreateDirectory(markerDirectory);
            File.WriteAllText(
                Path.Combine(markerDirectory, "example.legacy"),
                DateTimeOffset.UtcNow.ToString("O"));

            var record = PluginEnablementService.GetSelfProtectionDisabledPlugin("example.legacy");
            Assert.IsNotNull(record);
            Assert.AreEqual("example.legacy", record.PluginName);
            Assert.IsFalse(record.NotificationShown);
        }
        finally
        {
            Paths.Plugins = originalPlugins;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
