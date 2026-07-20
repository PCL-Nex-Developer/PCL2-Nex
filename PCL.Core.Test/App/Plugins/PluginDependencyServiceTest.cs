using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginDependencyServiceTest
{
    [TestMethod]
    public void Manifests_ShouldDeserializeDependenciesAndResolveVersionOverride()
    {
        var package = JsonSerializer.Deserialize<PluginPackageManifest>(
            """{"id":"example.feature","name":"Feature","version":"1.0.0","author":"Example","pclCoreVersion":"2026.07.1","entryAssembly":"feature.dll","mixinConfig":"mixins.json","dependencies":[{"id":"bridge.python","version":">=1.0.0 <2.0.0"}]}""",
            PluginJson.SerializerOptions);
        Assert.IsNotNull(package);
        Assert.AreEqual("bridge.python", package.Dependencies.Single().Id);

        var manifest = CreateMarketManifest();
        manifest.Dependencies = [new PluginDependency { Id = "bridge.python", Version = ">=1.0.0" }];
        manifest.Versions.Add(CreateMarketVersion("2.0.0"));
        manifest.Versions[1].Dependencies = [new PluginDependency { Id = "bridge.javascript", Version = ">=2.0.0" }];

        PluginRepositoryService.ValidateMarketManifest(manifest);

        Assert.AreEqual("bridge.python", manifest.Versions[0].ResolvedDependencies!.Single().Id);
        Assert.AreEqual("bridge.javascript", manifest.Versions[1].ResolvedDependencies!.Single().Id);
    }

    [TestMethod]
    public void DependencyDeclarations_ShouldRejectInvalidDuplicateAndSelfDependencies()
    {
        Assert.IsFalse(PluginDependencyService.ValidateDeclarations(
            "example.feature",
            [new PluginDependency { Id = "invalid" }]).IsValid);
        Assert.IsFalse(PluginDependencyService.ValidateDeclarations(
            "example.feature",
            [new PluginDependency { Id = "EXAMPLE.FEATURE" }]).IsValid);
        Assert.IsFalse(PluginDependencyService.ValidateDeclarations(
            "example.feature",
            [
                new PluginDependency { Id = "bridge.python" },
                new PluginDependency { Id = "BRIDGE.PYTHON" }
            ]).IsValid);
        Assert.IsFalse(PluginDependencyService.ValidateDeclarations(
            "example.feature",
            [new PluginDependency { Id = "bridge.python", Version = ">=latest" }]).IsValid);
    }

    [TestMethod]
    public void VersionExpression_ShouldSupportExactAndComparatorChains()
    {
        Assert.IsTrue(PluginDependencyService.IsVersionSatisfied("1.5.0", "1.5.0", out _));
        Assert.IsTrue(PluginDependencyService.IsVersionSatisfied("1.5.0", ">=1.0.0 <2.0.0", out _));
        Assert.IsFalse(PluginDependencyService.IsVersionSatisfied("2.0.0", ">=1.0.0 <2.0.0", out _));
        Assert.IsTrue(PluginDependencyService.IsVersionSatisfied("2.0.0", "*", out _));
    }

    [TestMethod]
    public void InstalledDependencyCheck_ShouldReportMissingDisabledAndVersionMismatch()
    {
        var feature = CreatePackage("example.feature", "1.0.0",
            new PluginDependency { Id = "bridge.python", Version = ">=1.0.0 <2.0.0" });
        var installed = new Dictionary<string, PluginPackageManifest>(StringComparer.OrdinalIgnoreCase);

        StringAssert.Contains(
            PluginDependencyService.CheckDependencies(feature, installed, _ => true).ErrorMessage!,
            "缺少");

        installed["bridge.python"] = CreatePackage("bridge.python", "1.5.0");
        StringAssert.Contains(
            PluginDependencyService.CheckDependencies(feature, installed, _ => false).ErrorMessage!,
            "未启用");

        installed["bridge.python"].Version = "2.0.0";
        StringAssert.Contains(
            PluginDependencyService.CheckDependencies(feature, installed, _ => true).ErrorMessage!,
            "不满足");

        installed["bridge.python"].Version = "1.5.0";
        Assert.IsTrue(PluginDependencyService.CheckDependencies(feature, installed, _ => true).IsValid);
    }

    [TestMethod]
    public void LoadPlan_ShouldPutBridgeBeforeDependentRegardlessOfUserOrder()
    {
        var feature = CreatePackage("example.feature", "1.0.0",
            new PluginDependency { Id = "bridge.python", Version = ">=1.0.0" });
        var bridge = CreatePackage("bridge.python", "1.2.0");
        var enabled = new HashSet<string>([feature.Id, bridge.Id], StringComparer.OrdinalIgnoreCase);

        var plan = PluginDependencyService.CreateLoadPlan(
            [new PluginPackageLocation(feature, "feature"), new PluginPackageLocation(bridge, "bridge")],
            enabled.Contains,
            [feature.Id, bridge.Id]);

        CollectionAssert.AreEqual(
            new[] { bridge.Id, feature.Id },
            plan.Packages.Select(package => package.Manifest.Id).ToArray());
        Assert.AreEqual(0, plan.Errors.Count);
    }

    [TestMethod]
    public void LoadPlan_ShouldRejectCyclesAndDependentsOfInvalidPrerequisites()
    {
        var first = CreatePackage("cycle.first", "1.0.0", new PluginDependency { Id = "cycle.second" });
        var second = CreatePackage("cycle.second", "1.0.0", new PluginDependency { Id = "cycle.first" });
        var enabled = new HashSet<string>([first.Id, second.Id], StringComparer.OrdinalIgnoreCase);

        var plan = PluginDependencyService.CreateLoadPlan(
            [new PluginPackageLocation(first, "first"), new PluginPackageLocation(second, "second")],
            enabled.Contains,
            [first.Id, second.Id]);

        Assert.AreEqual(0, plan.Packages.Count);
        StringAssert.Contains(plan.Errors[first.Id], "循环依赖");
        StringAssert.Contains(plan.Errors[second.Id], "循环依赖");
    }

    [TestMethod]
    public void MarketAndPackageDependencies_ShouldMatchExactly()
    {
        var package = CreatePackage("example.feature", "1.0.0",
            new PluginDependency { Id = "bridge.python", Version = ">=1.0.0" });
        var expected = new[] { new PluginDependency { Id = "BRIDGE.PYTHON", Version = ">=1.0.0" } };

        PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "example.feature", "1.0.0", expected);
        expected[0].Version = ">=2.0.0";
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PluginRemoteInstallService.ValidateSelectedMarketIdentity(package, "example.feature", "1.0.0", expected));
    }

    [TestMethod]
    public void LoadContext_ShouldReuseBridgeAssemblyFromPrerequisiteContext()
    {
        var defaultAssemblyNames = AssemblyLoadContext.Default.Assemblies
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .Select(path =>
            {
                try { return (Path: path, Name: AssemblyName.GetAssemblyName(path)); }
                catch { return (Path: string.Empty, Name: (AssemblyName?)null); }
            })
            .FirstOrDefault(item => item.Name?.Name is not null
                                    && !defaultAssemblyNames.Contains(item.Name.Name));
        Assert.IsNotNull(candidate.Name, "测试输出中应至少存在一个尚未由默认上下文加载的托管程序集。");

        var bridgeContext = new AssemblyLoadContext("pcl.bridge.test", isCollectible: true);
        var bridgeAssembly = bridgeContext.LoadFromAssemblyPath(Path.GetFullPath(candidate.Path));
        var dependentContext = new CollectiblePluginLoadContext(
            typeof(PluginDependencyServiceTest).Assembly.Location,
            [bridgeAssembly]);
        try
        {
            var resolved = dependentContext.LoadFromAssemblyName(bridgeAssembly.GetName());
            Assert.AreSame(bridgeAssembly, resolved);
            Assert.AreSame(bridgeContext, AssemblyLoadContext.GetLoadContext(resolved));
        }
        finally
        {
            dependentContext.Unload();
            bridgeContext.Unload();
        }
    }

    private static PluginPackageManifest CreatePackage(
        string id,
        string version,
        params PluginDependency[] dependencies) => new()
    {
        Id = id,
        Name = id,
        Version = version,
        Author = "Test",
        PclCoreVersion = "2026.07.1",
        EntryAssembly = id + ".dll",
        MixinConfig = "mixins.json",
        Dependencies = dependencies.ToList()
    };

    private static PluginMarketManifest CreateMarketManifest() => new()
    {
        Id = "example.feature",
        Name = "Feature",
        Author = new PluginMarketAuthor { GitHubLogin = "example" },
        Description = "Feature",
        Repository = "https://github.com/example/feature",
        Versions = [CreateMarketVersion("1.0.0")]
    };

    private static PluginMarketVersion CreateMarketVersion(string version) => new()
    {
        Version = version,
        PclCoreVersion = "2026.07.1",
        ReleaseNotes = "https://github.com/example/feature/releases/tag/v" + version,
        Downloads = new PluginMarketDownloads
        {
            AnyCpu = new PluginMarketDownload
            {
                PackageUrl = "https://github.com/example/feature/releases/download/v" + version + "/feature.pclx",
                Sha256 = new string('A', 64)
            }
        }
    };
}
