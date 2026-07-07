using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginRemoteInstallServiceTest
{
    [TestMethod]
    public void RewriteGitCloneUrl_ShouldUseGhproxyPrefix_ForHttpsGithub()
    {
        var result = PluginRemoteInstallService.RewriteGitCloneUrl(
            "https://github.com/PCL-Nex-Developer/HelloPlugin.git", 1);

        Assert.AreEqual("https://gh-proxy.org/https://github.com/PCL-Nex-Developer/HelloPlugin.git", result);
    }

    [TestMethod]
    public void RewriteGitCloneUrl_ShouldNotRewriteSshGitUrl()
    {
        var result = PluginRemoteInstallService.RewriteGitCloneUrl(
            "git@github.com:PCL-Nex-Developer/HelloPlugin.git", 1);

        Assert.AreEqual("git@github.com:PCL-Nex-Developer/HelloPlugin.git", result);
    }

    [TestMethod]
    public void ParseGitSource_ShouldExtractFragmentReference()
    {
        var result = PluginRemoteInstallService.ParseGitSource(
            "git+https://github.com/PCL-Nex-Developer/HelloPlugin.git#v1.2.3");

        Assert.AreEqual("https://github.com/PCL-Nex-Developer/HelloPlugin.git", result.CloneUrl);
        Assert.AreEqual("v1.2.3", result.Reference);
        Assert.AreEqual("https://github.com/PCL-Nex-Developer/HelloPlugin.git#v1.2.3", result.ToDisplayString());
    }

    [TestMethod]
    public void ParseGitSource_ShouldExtractAtReferenceAfterGitSuffix()
    {
        var result = PluginRemoteInstallService.ParseGitSource(
            "git@github.com:PCL-Nex-Developer/HelloPlugin.git@legacy-api");

        Assert.AreEqual("git@github.com:PCL-Nex-Developer/HelloPlugin.git", result.CloneUrl);
        Assert.AreEqual("legacy-api", result.Reference);
    }

    [TestMethod]
    public void ParseGitSource_ShouldPreferExplicitReference()
    {
        var result = PluginRemoteInstallService.ParseGitSource(
            "https://github.com/PCL-Nex-Developer/HelloPlugin.git#main", "v1.0.0");

        Assert.AreEqual("https://github.com/PCL-Nex-Developer/HelloPlugin.git", result.CloneUrl);
        Assert.AreEqual("v1.0.0", result.Reference);
    }
}