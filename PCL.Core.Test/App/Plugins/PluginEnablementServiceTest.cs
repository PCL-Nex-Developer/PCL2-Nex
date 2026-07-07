using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
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
}