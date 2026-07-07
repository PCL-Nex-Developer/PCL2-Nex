using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginRepositoryServiceTest
{
    [TestMethod]
    public void GetOfficialIndexUrl_ShouldUseRegistryJsonDefault()
    {
        Assert.AreEqual(
            "https://github.com/PCL-Nex-Developer/PCL2-Nex/raw/refs/heads/dev/plugins.json",
            PluginRepositoryService.GetOfficialIndexUrl());
    }

    [TestMethod]
    public void GetInstallSources_ShouldPreferManifestUrl()
    {
        var entry = new PluginRepositoryEntry
        {
            ManifestUrl = "https://example.test/pcl-plugin-manifest.json"
        };

        var sources = PluginRepositoryService.GetInstallSources(entry).ToList();

        Assert.AreEqual(1, sources.Count);
        Assert.AreEqual("manifest", sources[0].Type);
        Assert.AreEqual("https://example.test/pcl-plugin-manifest.json", sources[0].Url);
    }

    [TestMethod]
    public void GetInstallSources_ShouldReturnEmptyWhenManifestUrlMissing()
    {
        var entry = new PluginRepositoryEntry();

        var sources = PluginRepositoryService.GetInstallSources(entry).ToList();

        Assert.AreEqual(0, sources.Count);
    }

    [TestMethod]
    public void MergeIndexes_ShouldPreserveDistinctSourceEntries()
    {
        var a = new PluginRepositoryIndex
        {
            Name = "Official",
            Plugins =
            [
                new PluginRepositoryEntry
                {
                    Id = "com.example.hello",
                    Name = "Hello",
                    SourceRepoUrl = "https://repo-a",
                    ManifestUrl = "https://example.test/hello/manifest.json"
                }
            ]
        };
        var b = new PluginRepositoryIndex
        {
            Name = "Third",
            Plugins =
            [
                new PluginRepositoryEntry
                {
                    Id = "com.example.hello",
                    Name = "Hello Alt",
                    SourceRepoUrl = "https://repo-b",
                    ManifestUrl = "https://example.test/hello/manifest.json"
                },
                new PluginRepositoryEntry
                {
                    Id = "com.example.world",
                    Name = "World",
                    SourceRepoUrl = "https://repo-b",
                    ManifestUrl = "https://example.test/world/manifest.json"
                }
            ]
        };

        var merged = PluginRepositoryService.MergeIndexes([a, b]);

        Assert.AreEqual(3, merged.Count);
    }

    [TestMethod]
    public void NormalizeIndex_ShouldKeepOnlyDocumentFormatEntries()
    {
        var index = new PluginRepositoryIndex
        {
            Plugins =
            [
                null!,
                new PluginRepositoryEntry
                {
                    Id = " com.example.hello ",
                    Name = " Hello ",
                    ManifestUrl = " https://example.test/hello/pcl-plugin-manifest.json ",
                    Homepage = " https://example.test/hello "
                },
                new PluginRepositoryEntry
                {
                    Id = "com.example.duplicate",
                    Name = "Duplicate",
                    ManifestUrl = "https://example.test/hello/pcl-plugin-manifest.json"
                },
                new PluginRepositoryEntry
                {
                    Id = "com.example.no-manifest",
                    Name = "Missing Manifest"
                },
                new PluginRepositoryEntry
                {
                    Id = "com.example.relative-manifest",
                    Name = "Relative Manifest",
                    ManifestUrl = "/manifest.json"
                },
                new PluginRepositoryEntry
                {
                    Id = null!,
                    Name = "Null Id",
                    ManifestUrl = "https://example.test/null-id/manifest.json"
                },
                new PluginRepositoryEntry
                {
                    Id = "com.example.null-name",
                    Name = null!,
                    ManifestUrl = "https://example.test/null-name/manifest.json"
                }
            ]
        };

        PluginRepositoryService.NormalizeIndex(index, "https://example.test/index.json");

        Assert.AreEqual(1, index.Plugins.Count);
        Assert.AreEqual("com.example.hello", index.Plugins[0].Id);
        Assert.AreEqual("Hello", index.Plugins[0].Name);
        Assert.AreEqual("https://example.test/hello/pcl-plugin-manifest.json", index.Plugins[0].ManifestUrl);
        Assert.AreEqual("https://example.test/hello", index.Plugins[0].HomepageUrl);
        Assert.AreEqual("https://example.test/index.json", index.Plugins[0].SourceRepoUrl);
    }

    [TestMethod]
    public void MergeIndexes_ShouldDeduplicateSameIdAndSource()
    {
        var a = new PluginRepositoryIndex
        {
            Name = "Official",
            Plugins =
            [
                new PluginRepositoryEntry
                {
                    Id = "com.example.hello",
                    Name = "Hello v1",
                    SourceRepoUrl = "https://repo-a",
                    ManifestUrl = "https://example.test/hello/manifest.json"
                }
            ]
        };
        var b = new PluginRepositoryIndex
        {
            Name = "Mirror",
            Plugins =
            [
                new PluginRepositoryEntry
                {
                    Id = "com.example.hello",
                    Name = "Hello v2",
                    SourceRepoUrl = "https://repo-a",
                    ManifestUrl = "https://example.test/hello/manifest.json"
                }
            ]
        };

        var merged = PluginRepositoryService.MergeIndexes([a, b]);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("Hello v1", merged[0].Name); // 第一个条目保留
    }

    [TestMethod]
    public void MergeIndexes_ShouldHandleEmptyIndexes()
    {
        var a = new PluginRepositoryIndex { Name = "Empty", Plugins = [] };
        var b = new PluginRepositoryIndex { Name = "AlsoEmpty", Plugins = null! };

        var merged = PluginRepositoryService.MergeIndexes([a, b]);

        Assert.AreEqual(0, merged.Count);
    }

    [TestMethod]
    public void MergeIndexes_SamePluginDifferentSources_ShouldKeepBoth()
    {
        var a = new PluginRepositoryIndex
        {
            Plugins =
            [
                new PluginRepositoryEntry
                {
                    Id = "com.example.hello",
                    Name = "Hello A",
                    SourceRepoUrl = "https://repo-a",
                    ManifestUrl = "https://example.test/hello/manifest.json"
                },
                new PluginRepositoryEntry
                {
                    Id = "com.example.hello",
                    Name = "Hello B",
                    SourceRepoUrl = "https://repo-b",
                    ManifestUrl = "https://example.test/hello/manifest.json"
                }
            ]
        };

        var merged = PluginRepositoryService.MergeIndexes([a]);

        Assert.AreEqual(2, merged.Count);
    }
}
