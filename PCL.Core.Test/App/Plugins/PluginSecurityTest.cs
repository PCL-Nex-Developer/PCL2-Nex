using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginSecurityTest
{
    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("example")]
    [DataRow("example..plugin")]
    [DataRow("../example.plugin")]
    [DataRow("example/plugin")]
    [DataRow("example. plugin")]
    public void PluginId_ShouldRejectUnsafeOrUnstableValues(string pluginId)
    {
        Assert.IsFalse(PluginPackageService.IsValidPluginId(pluginId));
    }

    [TestMethod]
    [DataRow("example.plugin")]
    [DataRow("com.example_plugin.feature-1")]
    public void PluginId_ShouldAcceptSafeReverseDnsValues(string pluginId)
    {
        Assert.IsTrue(PluginPackageService.IsValidPluginId(pluginId));
    }

    [TestMethod]
    public async Task Install_ShouldRejectUnsafeIdBeforeConstructingPaths()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "../escape",
            Name = "Escape",
            PclCoreVersion = PluginCompatibility.CurrentPclCoreVersion
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PluginInstallService.InstallFromDirectoryAsync(
            "unused", manifest, PluginInstallSourceType.Repository, "https://example.test/plugin.pclx"));
    }

    [TestMethod]
    public async Task PublicPathOperations_ShouldRejectUnsafeIds()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => PluginInstallService.UninstallAsync(".."));
        Assert.ThrowsExactly<ArgumentException>(() => PluginInstallService.SetEnabled("..", false));
        Assert.ThrowsExactly<ArgumentException>(() => PluginEnablementService.SetEnabled("..", true));
        Assert.ThrowsExactly<ArgumentException>(() => PluginEnablementService.MarkSelfProtectionDisabled(".."));
    }

    [TestMethod]
    public void SynchronousEnable_ShouldRejectCompatibilityConfirmationBypass()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PluginInstallService.SetEnabled("example.plugin", true));
    }

    [TestMethod]
    public void PackageRequiredFlag_ShouldNotExistInPackageContract()
    {
        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(
            "{\"id\":\"example.required\",\"required\":true}",
            PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.IsNull(typeof(PluginPackageManifest).GetProperty("Required"));
    }

    [TestMethod]
    public void ValidateSha256_ShouldAcceptCorrectHashAndRejectBadHashes()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "PCL Nex plugin package");
            var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

            Assert.AreEqual(expected, PluginRemoteInstallService.ValidateSha256(file, expected));
            Assert.AreEqual(expected, PluginRemoteInstallService.ValidateSha256(file, null));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PluginRemoteInstallService.ValidateSha256(file, new string('0', 64)));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PluginRemoteInstallService.ValidateSha256(file, "ABCDEF"));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task RepositoryPackage_ShouldRequireSha256BeforeNetworkAccess()
    {
        var source = new PluginInstallSourceEntry
        {
            Type = "package",
            Url = "https://example.test/plugin.pclx"
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PluginRemoteInstallService.PrepareAsync(source));
    }

    [TestMethod]
    public void ApplyGitHubHeaders_ShouldPreservePrivateAccessHeadersForAcceleratedRequest()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://gh-proxy.org/https://api.github.com/repos/Owner/private/contents/manifest.json");

        PluginRemoteInstallService.ApplyGitHubHeaders(
            request,
            "https://api.github.com/repos/Owner/private/contents/manifest.json",
            "private-token",
            "application/vnd.github.raw+json");

        Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
        Assert.AreEqual("private-token", request.Headers.Authorization?.Parameter);
        Assert.IsTrue(request.Headers.Contains("User-Agent"));
        Assert.IsTrue(request.Headers.Contains("Accept"));
        Assert.IsTrue(request.Headers.Contains("X-GitHub-Api-Version"));
    }

    [TestMethod]
    public void ApplyGitHubHeaders_ShouldNotDependOnAccelerationSelectionOrLeakToThirdParty()
    {
        using var githubRequest = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/Owner/private/contents/manifest.json");
        PluginRemoteInstallService.ApplyGitHubHeaders(
            githubRequest,
            "https://api.github.com/repos/Owner/private/contents/manifest.json",
            "private-token",
            "application/vnd.github.raw+json");
        Assert.AreEqual("private-token", githubRequest.Headers.Authorization?.Parameter);
        Assert.IsTrue(githubRequest.Headers.Contains("X-GitHub-Api-Version"));

        using var thirdPartyRequest = new HttpRequestMessage(HttpMethod.Get,
            "https://plugins.example.test/manifest.json");
        PluginRemoteInstallService.ApplyGitHubHeaders(
            thirdPartyRequest,
            "https://plugins.example.test/manifest.json",
            "private-token",
            "application/json");
        Assert.IsNull(thirdPartyRequest.Headers.Authorization);
        Assert.IsFalse(thirdPartyRequest.Headers.Contains("X-GitHub-Api-Version"));
        Assert.IsFalse(thirdPartyRequest.Headers.Contains("Accept"));
    }

    [TestMethod]
    public void ManifestFetchCandidates_ShouldUseOnlyConfiguredMirrorAndOriginal()
    {
        const string url = "https://api.github.com/repos/Owner/plugin/contents/manifest.json";

        var disabled = PluginRemoteInstallService.GetManifestFetchCandidates(url, 0);
        Assert.AreEqual(1, disabled.Count);
        Assert.AreEqual(url, disabled[0].Url);

        var enabled = PluginRemoteInstallService.GetManifestFetchCandidates(url, 1);
        Assert.AreEqual(2, enabled.Count);
        Assert.AreEqual("https://gh-proxy.org/" + url, enabled[0].Url);
        Assert.AreEqual(url, enabled[1].Url);
        Assert.IsFalse(enabled.Any(candidate => candidate.Url.StartsWith("https://v4.gh-proxy.org/", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ReadManifestAsync_ShouldRejectOversizedBodyBeforeJsonParsing()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', PluginMarketQueryOptions.DefaultManifestSizeLimit + 1))
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PluginRemoteInstallService.ReadManifestAsync(response, CancellationToken.None));
    }

    [TestMethod]
    public void SelectedMarketIdentity_ShouldMatchPackageIdAndVersion()
    {
        var package = new PluginPackageManifest
        {
            Id = "example.plugin",
            Version = "1.2.3"
        };

        PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "EXAMPLE.PLUGIN", "1.2.3");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "other.plugin", "1.2.3"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "example.plugin", "1.2.4"));

        package.Version = "1.2.3-beta.1";
        PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "example.plugin", "1.2.3-beta.1");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "example.plugin", "1.2.3"));
    }

    [TestMethod]
    public void ManifestDownloads_ShouldRejectMissingOrMalformedSha256()
    {
        var manifest = new PluginMarketManifest
        {
            Repository = "https://github.com/Owner/plugin",
            Versions =
            [
                new PluginMarketVersion
                {
                    Version = "1.0.0",
                    PclCoreVersion = "2026.07.1",
                    ReleaseNotes = "https://github.com/Owner/plugin/releases/tag/v1.0.0",
                    Downloads = new PluginMarketDownloads
                    {
                        AnyCpu = new PluginMarketDownload
                        {
                            PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.0.0/plugin.pclx"
                        }
                    }
                }
            ]
        };

        Assert.ThrowsExactly<InvalidDataException>(() => PluginRepositoryService.ValidateManifestDownloads(manifest));
        manifest.Versions[0].Downloads!.AnyCpu!.Sha256 = "XYZ";
        Assert.ThrowsExactly<InvalidDataException>(() => PluginRepositoryService.ValidateManifestDownloads(manifest));
    }

    [TestMethod]
    public void MarketManifest_ShouldRejectEmptyAndUnorderableVersions()
    {
        var empty = CreateValidMarketManifest("example.empty");
        empty.Versions.Clear();
        Assert.ThrowsExactly<InvalidDataException>(() => PluginRepositoryService.ValidateMarketManifest(empty));

        var invalid = CreateValidMarketManifest("example.invalid");
        invalid.Versions[0].Version = "latest";
        Assert.ThrowsExactly<InvalidDataException>(() => PluginRepositoryService.ValidateMarketManifest(invalid));
    }

    private static PluginMarketManifest CreateValidMarketManifest(string id) => new()
    {
        Id = id,
        Name = "Plugin",
        Author = new PluginMarketAuthor { GitHubLogin = "Owner" },
        Description = "Description",
        Repository = "https://github.com/Owner/plugin",
        Versions =
        [
            new PluginMarketVersion
            {
                Version = "1.0.0",
                PclCoreVersion = "2026.07.1",
                ReleaseNotes = "https://github.com/Owner/plugin/releases/tag/v1.0.0",
                Downloads = new PluginMarketDownloads
                {
                    AnyCpu = new PluginMarketDownload
                    {
                        PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.0.0/plugin.pclx",
                        Sha256 = new string('A', 64)
                    }
                }
            }
        ]
    };

    [TestMethod]
    public void ExtractZipSafely_ShouldRejectPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-zip-security-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "plugin.pclx");
        var extractPath = Path.Combine(root, "extract");
        Directory.CreateDirectory(root);
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("escape");
            }

            Assert.ThrowsExactly<InvalidDataException>(() =>
                PluginRemoteInstallService.ExtractZipSafely(archivePath, extractPath));
            Assert.IsFalse(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
