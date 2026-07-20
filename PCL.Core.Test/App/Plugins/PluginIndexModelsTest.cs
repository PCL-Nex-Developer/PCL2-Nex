using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginIndexModelsTest
{
    [TestMethod]
    public void PluginPackageManifest_Deserialize_ShouldPreserveCriticalFields()
    {
        var json = """
        {
          "id": "com.example.hello",
          "name": "Hello",
          "version": "1.2.3",
          "author": "Example",
          "description": "desc",
          "pclCoreVersion": "2026.07.1",
          "entryAssembly": "lib/HelloPlugin.dll",
          "mixinConfig": "mixins.main.json",
          "mixinConfigs": ["mixins.extra.json"],
          "homepageUrl": "https://example.test",
          "license": "Apache-2.0",
          "icon": "assets/icon.png",
          "logo": "https://example.test/logo.png",
          "screenshots": ["assets/1.png"]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.AreEqual("com.example.hello", manifest.Id);
        Assert.AreEqual("lib/HelloPlugin.dll", manifest.EntryAssembly);
        Assert.AreEqual("2026.07.1", manifest.PclCoreVersion);
        Assert.AreEqual("https://example.test/logo.png", manifest.Logo);
        CollectionAssert.AreEqual(
            new[] { "mixins.main.json", "mixins.extra.json" },
            manifest.GetMixinConfigurationPaths().ToArray());
    }

    [TestMethod]
    public void PluginPackageManifest_LegacyJavaScriptFields_ShouldNotBePartOfContract()
    {
        var json = """
        {
          "id": "com.example.js",
          "name": "JS Plugin",
          "version": "1.0.0",
          "author": "Example",
          "runtime": "javascript-v8",
          "entryScript": "main.js",
          "loadMethod": "LoadAsync"
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.IsNull(typeof(PluginPackageManifest).GetProperty("Runtime"));
        Assert.IsNull(typeof(PluginPackageManifest).GetProperty("EntryScript"));
        Assert.IsNull(typeof(PluginPackageManifest).GetProperty("LoadMethod"));
        Assert.IsFalse(PluginPackageService.ValidatePackageManifest(manifest).IsValid);
    }

    [TestMethod]
    public void PluginRepositoryIndex_Deserialize_ShouldPreserveManifestEntries()
    {
        var json = """
        {
          "name": "PCL Nex Plugin Market Index",
          "description": "Approved plugins for PCL Nex market",
          "homepageUrl": "https://github.com/PCL-Nex-Developer/Plugins",
          "maintainer": "PCL Nex",
          "plugins": [
            {
              "id": "com.example.hello",
              "name": "Hello",
              "version": "1.0.0",
              "author": "Example",
              "description": "desc",
              "manifestUrl": "https://example.test/hello/pcl-manifest.json",
              "homepage": "https://example.test/hello",
              "repository": {
                "type": "git",
                "url": "https://github.com/example/hello-plugin.git",
                "directory": "plugin"
              },
              "custom": { "channel": "stable" }
            },
            {
              "id": "com.example.world",
              "name": "World",
              "version": "2.0.0",
              "manifestUrl": "https://example.test/world/pcl-manifest.json"
            }
          ]
        }
        """;

        var index = JsonSerializer.Deserialize<PluginRepositoryIndex>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(index);
        Assert.AreEqual("PCL Nex Plugin Market Index", index.Name);
        Assert.AreEqual(2, index.Plugins.Count);

        var hello = index.Plugins[0];
        Assert.AreEqual("com.example.hello", hello.Id);
        Assert.AreEqual("https://example.test/hello/pcl-manifest.json", hello.ManifestUrl);
        Assert.AreEqual("Example", hello.Author);
        Assert.AreEqual("desc", hello.Description);
        Assert.AreEqual("https://example.test/hello", hello.Homepage);
        Assert.AreEqual("git", hello.Repository?.Type);
        Assert.AreEqual("https://github.com/example/hello-plugin.git", hello.Repository?.Url);
        Assert.AreEqual("plugin", hello.Repository?.Directory);
        Assert.IsTrue(hello.Custom?.ContainsKey("channel"));

        var world = index.Plugins[1];
        Assert.AreEqual("com.example.world", world.Id);
        Assert.AreEqual("https://example.test/world/pcl-manifest.json", world.ManifestUrl);
    }

    [TestMethod]
    public void PluginMarketManifest_SelectCompatibleVersion_ShouldUseVersionList()
    {
        var json = """
        {
          "id": "example.hello",
          "name": "Hello",
          "author": { "githubLogin": "example" },
          "description": "Hello plugin",
          "repository": "https://github.com/example/hello-plugin",
          "versions": [
            {
              "version": "2.0.0",
              "pclCoreVersion": "2026.08.1",
              "downloads": { "anycpu": { "packageUrl": "https://github.com/example/hello-plugin/releases/download/v2.0.0/hello.pclx", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" } },
              "releaseNotes": "https://github.com/example/hello-plugin/releases/tag/v2.0.0"
            },
            {
              "version": "1.5.0",
              "pclCoreVersion": "2026.07.1",
              "downloads": { "anycpu": { "packageUrl": "https://github.com/example/hello-plugin/releases/download/v1.5.0/hello.pclx", "sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" } },
              "releaseNotes": "https://github.com/example/hello-plugin/releases/tag/v1.5.0"
            }
          ]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginMarketManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.AreEqual(2, manifest.Versions.Count);

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2026.07.1");

        Assert.AreEqual("2.0.0", selected.Version);
        Assert.AreEqual("2026.08.1", selected.PclCoreVersion);
    }

      [TestMethod]
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldRequireVersionsList()
      {
        var json = """
        {
          "version": "1.0.0",
          "packageUrl": "https://example.test/releases/hello-1.0.0.pclx",
          "minApiVersion": "1.0.0"
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginMarketManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        try
        {
          PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2.17.0");
          Assert.Fail("旧单版本 manifest 不应被接受。");
        }
        catch (System.IO.InvalidDataException)
        {
        }
      }

      [TestMethod]
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldRejectLegacyTopLevelDownloadFields()
      {
        var manifest = CreateMarketManifest();
        manifest.Versions[0].AdditionalProperties = new Dictionary<string, JsonElement>
        {
          ["packageUrl"] = JsonDocument.Parse("\"https://github.com/example/hello-plugin/releases/download/v1.0.0/legacy.pclx\"").RootElement.Clone()
        };

        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
          PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2026.07.1"));
      }

      [TestMethod]
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldPickHighestInstallableVersionIncludingFuture()
      {
        var manifest = CreateMarketManifest();
        manifest.Versions =
        [
          CreateMarketVersion("3.0.0", "2026.08.1"),
          CreateMarketVersion("1.5.0", "2026.07.1"),
          CreateMarketVersion("2.0.0", "2026.07.1")
        ];

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2026.07.1");

        Assert.AreEqual("3.0.0", selected.Version);
        Assert.AreEqual(PluginCoreCompatibilityStatus.Future,
            PluginCompatibility.EvaluatePclCoreVersion(selected.PclCoreVersion, "2026.07.1"));
      }

      [TestMethod]
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldPickEasyTierStyleUpdate()
      {
        var manifest = CreateMarketManifest();
        manifest.Versions =
        [
          CreateMarketVersion("1.0.6", "2026.07.1"),
          CreateMarketVersion("1.0.0", "2026.07.1")
        ];

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2026.07.1");

        Assert.AreEqual("1.0.6", selected.Version);
        Assert.AreEqual("https://github.com/example/hello-plugin/releases/download/v1.0.6/hello.pclx", selected.ResolvedPackageUrl);
      }

    private static PluginMarketManifest CreateMarketManifest() => new()
    {
      Id = "example.hello",
      Name = "Hello",
      Author = new PluginMarketAuthor { GitHubLogin = "example" },
      Description = "Hello plugin",
      Repository = "https://github.com/example/hello-plugin",
      Versions = [CreateMarketVersion("1.0.0", "2026.07.1")]
    };

    private static PluginMarketVersion CreateMarketVersion(string version, string coreVersion) => new()
    {
      Version = version,
      PclCoreVersion = coreVersion,
      ReleaseNotes = "https://github.com/example/hello-plugin/releases/tag/v" + version,
      Downloads = new PluginMarketDownloads
      {
        AnyCpu = new PluginMarketDownload
        {
          PackageUrl = "https://github.com/example/hello-plugin/releases/download/v" + version + "/hello.pclx",
          Sha256 = new string('A', 64)
        }
      }
    };

    [TestMethod]
    public void PluginPackageManifest_SerializeRoundtrip_ShouldPreserveData()
    {
        var original = new PluginPackageManifest
        {
            Id = "com.test.roundtrip",
            Name = "Roundtrip Test",
            Version = "3.2.1-beta.1",
            Author = "Tester",
            PclCoreVersion = "2026.07.1",
            EntryAssembly = "lib/Test.dll",
            MixinConfigs = ["mixins.client.json", "mixins.download.json"]
        };

        var json = JsonSerializer.Serialize(original, PluginJson.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Id, deserialized.Id);
        Assert.AreEqual(original.Version, deserialized.Version);
        Assert.AreEqual(original.EntryAssembly, deserialized.EntryAssembly);
        CollectionAssert.AreEqual(original.MixinConfigs, deserialized.MixinConfigs);
    }

    [TestMethod]
    public void PluginInstallRecord_DefaultValues_ShouldBeReasonable()
    {
        var record = new PluginInstallRecord();

        Assert.AreEqual(string.Empty, record.PluginId);
        Assert.IsTrue(record.Enabled);
        Assert.AreEqual(PluginInstallSourceType.Repository, record.SourceType);
    }
}
