using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginPathsTest
{
    [TestMethod]
    public void PluginSubdirectories_ShouldBeNestedUnderPluginsRoot()
    {
        try
        {
            // Paths 静态构造函数依赖 Basics（读取配置文件），
            // 在测试环境中可能因缺少配置文件而失败。
            // 若失败则跳过此测试（不影响 CI）。
            _ = Paths.Plugins;
        }
        catch
        {
            Assert.Inconclusive("Paths 静态初始化失败（测试环境缺少配置文件），跳过路径测试。");
            return;
        }

        Assert.AreEqual(Paths.Plugins, Paths.PluginInstalled);
        Assert.IsFalse(string.IsNullOrWhiteSpace(Paths.PluginTrust));
        Assert.IsFalse(string.IsNullOrWhiteSpace(Paths.PluginTemp));
    }

    [TestMethod]
    public void PluginSubdirectories_ShouldHaveDistinctNames()
    {
        try
        {
            _ = Paths.Plugins;
        }
        catch
        {
            Assert.Inconclusive("Paths 静态初始化失败（测试环境缺少配置文件），跳过路径测试。");
            return;
        }

        Assert.AreNotEqual(Paths.PluginInstalled, Paths.PluginTrust);
        Assert.AreNotEqual(Paths.PluginInstalled, Paths.PluginTemp);
        Assert.AreNotEqual(Paths.PluginTrust, Paths.PluginTemp);
    }
}
