using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginTrustServiceTest
{
    [TestMethod]
    public void EvaluateUpdate_ShouldRequireReconfirm_WhenCapabilitiesExpand()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a",
            CapabilitiesSnapshot = [PluginCapabilities.ContributeTools]
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-a",
            Capabilities = [PluginCapabilities.ContributeTools, PluginCapabilities.ReadInstanceInfo]
        };

        var decision = PluginTrustService.EvaluateUpdate(installed, incoming);

        Assert.AreEqual(PluginTrustDecision.RequireReconfirm, decision);
    }

    [TestMethod]
    public void EvaluateUpdate_ShouldAllow_WhenNoCapabilityChange()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a",
            CapabilitiesSnapshot = [PluginCapabilities.ContributeTools]
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-a",
            Capabilities = [PluginCapabilities.ContributeTools]
        };

        var decision = PluginTrustService.EvaluateUpdate(installed, incoming);

        Assert.AreEqual(PluginTrustDecision.Allow, decision);
    }

    [TestMethod]
    public void EvaluateUpdate_ShouldRequireReconfirm_WhenSourceChanges()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a",
            CapabilitiesSnapshot = [PluginCapabilities.ContributeTools]
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-b", // 不同来源
            Capabilities = [PluginCapabilities.ContributeTools]
        };

        var decision = PluginTrustService.EvaluateUpdate(installed, incoming);

        Assert.AreEqual(PluginTrustDecision.RequireReconfirm, decision);
    }

    [TestMethod]
    public void EvaluateInstall_ShouldRequireReconfirm_ForGit()
    {
        var entry = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://example.com/plugin-source"
        };

        Assert.AreEqual(PluginTrustDecision.RequireReconfirm,
            PluginTrustService.EvaluateInstall(entry, PluginInstallSourceType.Git));
    }

    [TestMethod]
    public void EvaluateInstall_ShouldAllow_ForOfficialRepository()
    {
        var entry = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = PluginRepositoryService.GetOfficialIndexUrl()
        };

        var decision = PluginTrustService.EvaluateInstall(entry, PluginInstallSourceType.Repository);

        Assert.AreEqual(PluginTrustDecision.Allow, decision);
    }

    [TestMethod]
    public void IsOfficialRepository_ShouldRecognizeBuiltInRegistry()
    {
        Assert.IsTrue(PluginTrustService.IsOfficialRepository(PluginRepositoryService.GetOfficialIndexUrl()));
    }

    [TestMethod]
    public void EvaluateInstall_ShouldRequireRepositoryTrust_ForUntrustedCustomRepo()
    {
        var entry = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://unknown-repo.example.com/index.json"
        };

        var decision = PluginTrustService.EvaluateInstall(entry, PluginInstallSourceType.Repository);

        Assert.AreEqual(PluginTrustDecision.RequireRepositoryTrust, decision);
    }
}
