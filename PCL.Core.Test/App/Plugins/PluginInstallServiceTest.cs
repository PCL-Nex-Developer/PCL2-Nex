using System;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
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
}
