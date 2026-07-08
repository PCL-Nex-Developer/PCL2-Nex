using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginPackageServiceTest
{
    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingEntryAssembly()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            Author = "Example",
            EntryAssembly = "",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "EntryAssembly");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingEntryScriptForJavaScript()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.js",
            Name = "JS Plugin",
            Version = new Version(1, 0, 0, 0),
            Author = "Example",
            Runtime = PluginPackageManifest.RuntimeJavaScriptV8,
            EntryScript = "",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "EntryScript");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldAcceptJavaScriptManifest()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.js",
            Name = "JS Plugin",
            Version = new Version(1, 0, 0, 0),
            Author = "Example",
            Runtime = PluginPackageManifest.RuntimeJavaScriptV8,
            EntryScript = "main.js",
            MinApiVersion = new Version(1, 0, 0, 0),
            Capabilities = [PluginCapabilities.ContributePluginPage]
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingId()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "Id");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectMissingName()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "Name");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectInvalidVersion()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(0, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "Version");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectIncompatibleApiVersion()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(2, 0, 0, 0) // 主版本号不兼容
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "不兼容");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldAcceptNewerApiWhenNoMaximumDeclared()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectApiVersionAbovePluginMaximum()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0),
            MaxApiVersion = new Version(0, 9, 0, 0)
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest, "2.15.0");

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "API 版本不兼容");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectHostVersionAbovePluginMaximum()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0),
            MaxHostVersion = "2.14.0"
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest, "2.15.0");

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "启动器版本不兼容");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldAcceptHostVersionWithinRange()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0),
            MinHostVersion = "2.15.0-beta.1",
            MaxHostVersion = "2.15.0"
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest, "2.15.0");

        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldRejectInvalidHostVersionConstraint()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = new Version(1, 0, 0, 0),
            MinHostVersion = "2.15"
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest, "2.15.0");

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "MinHostVersion");
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldAcceptValidManifest()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            Author = "Example",
            EntryAssembly = "lib/HelloPlugin.dll",
            MinApiVersion = new Version(1, 0, 0, 0),
            Capabilities = [PluginCapabilities.ContributeTools]
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void ValidatePackageManifest_ShouldAcceptNullManifest()
    {
        var result = PluginPackageService.ValidatePackageManifest(null!);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void ValidatePackageManifest_NullMinApiVersion_ShouldReject()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "com.example.hello",
            Name = "Hello",
            Version = new Version(1, 0, 0, 0),
            EntryAssembly = "lib/test.dll",
            MinApiVersion = null!
        };

        var result = PluginPackageService.ValidatePackageManifest(manifest);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage!, "MinApiVersion");
    }

    [TestMethod]
    public async Task ReadAndValidateDirectoryAsync_ShouldReadPluginJsonFromDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pcl_plugin_manifest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var manifest = new PluginPackageManifest
            {
                Id = "com.example.js",
                Name = "JS Plugin",
                Version = new Version(1, 0, 0, 0),
                Author = "Example",
                Runtime = PluginPackageManifest.RuntimeJavaScriptV8,
                EntryScript = "main.js",
                MinApiVersion = new Version(1, 0, 0, 0),
                Capabilities = [PluginCapabilities.ContributeTools]
            };
            await File.WriteAllTextAsync(Path.Combine(tempDir, "plugin.json"),
                JsonSerializer.Serialize(manifest, PluginJson.SerializerOptions));

            var (readManifest, result) = await PluginPackageService.ReadAndValidateDirectoryAsync(tempDir);

            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(readManifest);
            Assert.AreEqual("com.example.js", readManifest.Id);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
