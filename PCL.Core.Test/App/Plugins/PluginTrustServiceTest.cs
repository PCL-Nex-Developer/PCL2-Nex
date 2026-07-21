using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginTrustServiceTest
{
    [TestMethod]
    public void UpdateSourceMatching_ShouldKeepGitAndManifestPluginsOnTheirRecordedSource()
    {
        var git = new PluginInstallRecord
        {
            PluginId = "example.plugin",
            InstalledFrom = "https://github.com/example/plugin",
            SourceType = PluginInstallSourceType.Git
        };
        var manifest = new PluginInstallRecord
        {
            PluginId = "example.plugin",
            InstalledFrom = "https://plugins.example/manifest.json",
            SourceType = PluginInstallSourceType.Manifest
        };
        var entry = new PluginRepositoryEntry
        {
            Id = "example.plugin",
            SourceRepoUrl = "https://github.com/example/plugin",
            ManifestUrl = "https://plugins.example/manifest.json"
        };

        Assert.IsTrue(PluginUpdateService.MatchesInstalledSource(git, entry));
        Assert.IsTrue(PluginUpdateService.MatchesInstalledSource(manifest, entry));
        entry.SourceRepoUrl = "https://github.com/other/plugin";
        entry.ManifestUrl = "https://other.example/manifest.json";
        Assert.IsFalse(PluginUpdateService.MatchesInstalledSource(git, entry));
        Assert.IsFalse(PluginUpdateService.MatchesInstalledSource(manifest, entry));
    }
    [TestMethod]
    public void EvaluateUpdate_ShouldAllow_WhenSourceIsUnchanged()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a"
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-a"
        };

        var decision = PluginTrustService.EvaluateUpdate(installed, incoming);

        Assert.AreEqual(PluginTrustDecision.Allow, decision);
    }

    [TestMethod]
    public void EvaluateUpdate_ShouldAllow_WhenNoLegacyCapabilityMetadataExists()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a"
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-a"
        };

        var decision = PluginTrustService.EvaluateUpdate(installed, incoming);

        Assert.AreEqual(PluginTrustDecision.Allow, decision);
    }

    [TestMethod]
    public void EvaluateUpdate_ShouldAllow_WhenManifestSourceMatchesInstallRecord()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a/hello/manifest.json"
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-a/index.json"
        };

        var decision = PluginTrustService.EvaluateUpdate(installed, incoming, "https://repo-a/hello/manifest.json");

        Assert.AreEqual(PluginTrustDecision.Allow, decision);
    }

    [TestMethod]
    public void EvaluateUpdate_ShouldRequireReconfirm_WhenSourceChanges()
    {
        var installed = new PluginInstallRecord
        {
            PluginId = "com.example.hello",
            InstalledFrom = "https://repo-a"
        };
        var incoming = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceRepoUrl = "https://repo-b" // 不同来源
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
        Assert.IsTrue(PluginTrustService.IsOfficialRepository(
            " HTTPS://RAW.GITHUBUSERCONTENT.COM/PCL-NEX-DEVELOPER/NEX_SERVER/REFS/HEADS/MAIN/APIV2/PLUGIN-MARKET.JSON "));
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

    [TestMethod]
    public void EvaluateInstall_ShouldTrustCustomManifestSourceInsteadOfPluginGitHubRepository()
    {
        var entry = new PluginRepositoryEntry
        {
            Id = "com.example.hello",
            SourceKind = "Manifest",
            ManifestUrl = "https://plugins.example.test/hello/manifest.json",
            SourceRepoUrl = "https://github.com/Owner/hello",
            GitHubLogin = "Owner"
        };

        Assert.AreEqual("https://plugins.example.test/hello/manifest.json",
            PluginTrustService.GetRepositoryTrustUrl(entry));
        Assert.AreEqual(PluginTrustDecision.RequireRepositoryTrust,
            PluginTrustService.EvaluateInstall(entry, PluginInstallSourceType.Repository));
    }
}
