using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginRemoteInstallServiceTest
{
    [TestMethod]
    [DoNotParallelize]
    public async Task PrepareDownloadedPackageAsync_ShouldValidateAndOwnOnlyExtractedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-downloaded-plugin-" + Guid.NewGuid().ToString("N"));
        var pluginTemp = Path.Combine(root, "plugin-temp");
        var packagePath = Path.Combine(root, "example.pclx");
        Directory.CreateDirectory(pluginTemp);
        var dependencies = new List<PluginDependency>
        {
            new() { Id = "com.example.bridge", Version = ">=1.0.0 <2.0.0" }
        };
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.downloaded",
            Name = "Downloaded Plugin",
            Version = "1.2.3",
            Author = "Example",
            PclCoreVersion = "2026.07.1",
            EntryAssembly = "lib/Example.Plugin.dll",
            MixinConfig = "mixins.json",
            Dependencies = dependencies
        };

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("plugin.json");
            await using (var stream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(stream, manifest, PluginJson.SerializerOptions);
            archive.CreateEntry("lib\\Example.Plugin.dll");
            archive.CreateEntry("mixins.json");
        }

        string expectedSha256;
        await using (var packageStream = File.OpenRead(packagePath))
            expectedSha256 = Convert.ToHexString(await SHA256.HashDataAsync(packageStream));
        var originalPluginTemp = Paths.PluginTemp;
        PluginPreparedInstall? prepared = null;
        try
        {
            Paths.PluginTemp = pluginTemp;
            prepared = await PluginRemoteInstallService.PrepareDownloadedPackageAsync(
                packagePath,
                "https://example.test/example.pclx",
                expectedSha256,
                manifest.Id,
                manifest.Version,
                dependencies);

            Assert.AreEqual(manifest.Id, prepared.Manifest.Id);
            Assert.AreEqual(manifest.Version, prepared.Manifest.Version);
            Assert.AreEqual(PluginInstallSourceType.Repository, prepared.SourceType);
            Assert.AreEqual("https://example.test/example.pclx", prepared.SourceUrl);
            Assert.AreEqual(expectedSha256, prepared.VerifiedSha256);
            Assert.IsTrue(File.Exists(Path.Combine(prepared.PluginRoot, "plugin.json")));
            Assert.IsTrue(File.Exists(Path.Combine(prepared.PluginRoot, "lib", "Example.Plugin.dll")));
            Assert.IsTrue(Directory.Exists(prepared.CleanupPath));

            var cleanupPath = prepared.CleanupPath;
            prepared.Dispose();
            prepared = null;
            Assert.IsFalse(Directory.Exists(cleanupPath));
            Assert.IsTrue(File.Exists(packagePath));
        }
        finally
        {
            prepared?.Dispose();
            Paths.PluginTemp = originalPluginTemp;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
