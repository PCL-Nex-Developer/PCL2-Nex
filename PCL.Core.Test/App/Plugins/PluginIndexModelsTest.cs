using System;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

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
          "entryAssembly": "lib/HelloPlugin.dll",
          "minApiVersion": "1.0.0",
          "maxApiVersion": "1.1.0",
          "minHostVersion": "2.15.0-beta.1",
          "maxHostVersion": "2.15.0",
          "capabilities": ["ContributeTools", "ReadInstanceInfo"],
          "homepageUrl": "https://example.test",
          "license": "Apache-2.0",
          "icon": "assets/icon.png",
          "screenshots": ["assets/1.png"]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.AreEqual("com.example.hello", manifest.Id);
        Assert.AreEqual("lib/HelloPlugin.dll", manifest.EntryAssembly);
        Assert.AreEqual(new Version(1, 0, 0), manifest.MinApiVersion);
        Assert.AreEqual(new Version(1, 1, 0), manifest.MaxApiVersion);
        Assert.AreEqual("2.15.0-beta.1", manifest.MinHostVersion);
        Assert.AreEqual("2.15.0", manifest.MaxHostVersion);
        Assert.AreEqual(2, manifest.Capabilities.Length);
        Assert.AreEqual(PluginCapabilities.ContributeTools, manifest.Capabilities[0]);
        Assert.AreEqual(PluginCapabilities.ReadInstanceInfo, manifest.Capabilities[1]);
    }

    [TestMethod]
    public void PluginPackageManifest_DeserializeJavaScriptRuntime_ShouldPreserveEntryScript()
    {
        var json = """
        {
          "id": "com.example.js",
          "name": "JS Plugin",
          "version": "1.0.0",
          "author": "Example",
          "runtime": "javascript-v8",
          "entryScript": "main.js",
          "minApiVersion": "1.0.0",
          "capabilities": ["ContributePluginPage"]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.AreEqual(PluginPackageManifest.RuntimeJavaScriptV8, manifest.Runtime);
        Assert.AreEqual("main.js", manifest.EntryScript);
        Assert.IsTrue(manifest.IsJavaScriptPlugin());
        Assert.AreEqual(PluginCapabilities.ContributePluginPage, manifest.Capabilities[0]);
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
              "capabilities": ["ContributeTools"],
              "custom": { "channel": "stable" },
              "maxHostVersion": "2.15.0"
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
        Assert.AreEqual(PluginCapabilities.ContributeTools, hello.Capabilities[0]);
        Assert.IsTrue(hello.Custom?.ContainsKey("channel"));
        Assert.AreEqual("2.15.0", hello.MaxHostVersion);

        var world = index.Plugins[1];
        Assert.AreEqual("com.example.world", world.Id);
        Assert.AreEqual("https://example.test/world/pcl-manifest.json", world.ManifestUrl);
    }

    [TestMethod]
    public void PluginMarketManifest_SelectCompatibleVersion_ShouldUseVersionList()
    {
        var json = """
        {
          "versions": [
            {
              "version": "2.0.0",
              "packageUrl": "https://example.test/releases/hello-2.0.0.pclx",
              "minApiVersion": "1.0.0",
              "minHostVersion": "9.0.0"
            },
            {
              "version": "1.5.0",
              "packageUrl": "https://example.test/releases/hello-1.5.0.pclx",
              "sha256": "ABCDEF",
              "minApiVersion": "1.0.0",
              "maxApiVersion": "2.0.0",
              "minHostVersion": "2.15.0",
              "maxHostVersion": "2.16.0",
              "releaseNotes": "stable"
            }
          ]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginMarketManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(manifest);
        Assert.AreEqual(2, manifest.Versions.Count);

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2.15.0");

        Assert.AreEqual("1.5.0", selected.Version);
        Assert.AreEqual("https://example.test/releases/hello-1.5.0.pclx", selected.PackageUrl);
        Assert.AreEqual("ABCDEF", selected.Sha256);
        Assert.AreEqual(new Version(1, 0, 0), selected.MinApiVersion);
        Assert.AreEqual(new Version(2, 0, 0), selected.MaxApiVersion);
        Assert.AreEqual("stable", selected.ReleaseNotes);
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
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldIgnoreNonPackageUrls()
      {
        var manifest = new PluginMarketManifest
        {
          Versions =
          [
            new PluginMarketVersion
            {
              Version = "2.0.0",
              PackageUrl = "https://example.test/releases/hello-2.0.0.exe"
            },
            new PluginMarketVersion
            {
              Version = "1.0.0",
              PackageUrl = "https://example.test/releases/hello-1.0.0.zip"
            }
          ]
        };

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2.17.0");

        Assert.AreEqual("1.0.0", selected.Version);
        Assert.AreEqual("https://example.test/releases/hello-1.0.0.zip", selected.PackageUrl);
      }

      [TestMethod]
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldPickHighestCompatibleVersion()
      {
        var manifest = new PluginMarketManifest
        {
          Versions =
          [
            new PluginMarketVersion
            {
              Version = "3.0.0",
              PackageUrl = "https://example.test/releases/hello-3.0.0.pclx",
              MinApiVersion = new Version(9, 0, 0)
            },
            new PluginMarketVersion
            {
              Version = "1.5.0",
              PackageUrl = "https://example.test/releases/hello-1.5.0.pclx",
              MinApiVersion = new Version(1, 0, 0),
              MaxApiVersion = new Version(2, 0, 0)
            },
            new PluginMarketVersion
            {
              Version = "2.0.0",
              PackageUrl = "https://example.test/releases/hello-2.0.0.pclx",
              MinApiVersion = new Version(1, 0, 0),
              MaxApiVersion = new Version(2, 0, 0)
            }
          ]
        };

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "2.17.0");

        Assert.AreEqual("2.0.0", selected.Version);
        Assert.AreEqual("https://example.test/releases/hello-2.0.0.pclx", selected.PackageUrl);
      }

      [TestMethod]
      public void PluginMarketManifest_SelectCompatibleVersion_ShouldPickEasyTierStyleUpdate()
      {
        var manifest = new PluginMarketManifest
        {
          Versions =
          [
            new PluginMarketVersion
            {
              Version = "1.0.6",
              PackageUrl = "https://example.test/releases/pclnex.easytier-v1.0.6.pclx",
              MinApiVersion = new Version(1, 1, 0),
              MinHostVersion = "3.0.0"
            },
            new PluginMarketVersion
            {
              Version = "1.0.0",
              PackageUrl = "https://example.test/releases/pclnex.easytier-v1.0.0.pclx",
              MinApiVersion = new Version(1, 1, 0),
              MinHostVersion = "3.0.0"
            }
          ]
        };

        var selected = PluginRemoteInstallService.SelectCompatibleManifestVersion(manifest, "3.0.1");

        Assert.AreEqual("1.0.6", selected.Version);
        Assert.AreEqual("https://example.test/releases/pclnex.easytier-v1.0.6.pclx", selected.PackageUrl);
      }

    [TestMethod]
    public void PluginPackageManifest_SerializeRoundtrip_ShouldPreserveData()
    {
        var original = new PluginPackageManifest
        {
            Id = "com.test.roundtrip",
            Name = "Roundtrip Test",
            Version = new Version(3, 2, 1, 0),
            Author = "Tester",
            EntryAssembly = "lib/Test.dll",
            MinApiVersion = new Version(1, 0, 0, 0),
            Capabilities = [PluginCapabilities.ContributeSettings, PluginCapabilities.RegisterCliCommand]
        };

        var json = JsonSerializer.Serialize(original, PluginJson.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<PluginPackageManifest>(json, PluginJson.SerializerOptions);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Id, deserialized.Id);
        Assert.AreEqual(original.Version, deserialized.Version);
        Assert.AreEqual(original.EntryAssembly, deserialized.EntryAssembly);
        Assert.AreEqual(original.Capabilities.Length, deserialized.Capabilities.Length);
    }

    [TestMethod]
    public void PluginInstallRecord_DefaultValues_ShouldBeReasonable()
    {
        var record = new PluginInstallRecord();

        Assert.AreEqual(string.Empty, record.PluginId);
        Assert.IsTrue(record.Enabled);
        Assert.AreEqual(PluginInstallSourceType.Repository, record.SourceType);
        Assert.AreEqual(0, record.CapabilitiesSnapshot.Length);
    }
}
