using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Cli;
using PCL.Core.App.Essentials;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App;

[TestClass]
public sealed class UriSchemeServiceTest
{
    [TestMethod]
    public void ParsePlainActionsUri()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pcl://actions", out var request));
        Assert.AreEqual("pcl", request!.Scheme);
        Assert.AreEqual("actions", request.Command);
        Assert.IsNull(request.ActionType);
        Assert.IsNull(request.Data);
    }

    [TestMethod]
    public void ParseQueryActionUri()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pclnex://actions?type=ShowHint&data=Hello+PCL", out var request));
        Assert.AreEqual("pclnex", request!.Scheme);
        Assert.AreEqual("ShowHint", request.ActionType);
        Assert.AreEqual("Hello PCL", request.Data);
        Assert.AreEqual("ShowHint", request.Query["type"]);
        Assert.AreEqual("Hello PCL", request.Query["data"]);
    }

    [TestMethod]
    public void ParsePathActionUri()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pcl://actions/ShowDialog/Title%7CContent", out var request));
        Assert.AreEqual("ShowDialog", request!.ActionType);
        Assert.AreEqual("Title|Content", request.Data);
        CollectionAssert.AreEqual(new[] { "Title|Content" }, request.PathArguments.ToArray());
    }

    [TestMethod]
    public void ParseGenericCommandUri()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pcl://ShowHint/Hello%20PCL", out var request));
        Assert.AreEqual("ShowHint", request!.Command, true);
        Assert.AreEqual("ShowHint", request.ActionType, true);
        Assert.AreEqual("Hello PCL", request.Data);
        CollectionAssert.AreEqual(new[] { "Hello PCL" }, request.PathArguments.ToArray());
    }

    [TestMethod]
    public void ParseLauncherActionUri()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pcl://launch?instance=1.20.1&server=play.example.net", out var request));
        Assert.AreEqual("launch", request!.Command);
        Assert.AreEqual("launch", request.ActionType);
        Assert.AreEqual("1.20.1", request.Query["instance"]);
        Assert.AreEqual("play.example.net", request.Query["server"]);
    }

    [TestMethod]
    public void DirectCommandUriKeepsActionWhenNameQueryExists()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pcl://launch?name=My+Instance", out var request));
        Assert.AreEqual("launch", request!.Command);
        Assert.AreEqual("launch", request.ActionType);
        Assert.AreEqual("My Instance", request.Query["name"]);
    }

    [TestMethod]
    public void ParseAddPluginSourceUri()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction("pcl://add-plugin-source?url=https%3A%2F%2Fexample.test%2Findex.json&name=Example+Source", out var request));
        Assert.AreEqual("add-plugin-source", request!.Command);
        Assert.AreEqual("add-plugin-source", request.ActionType);
        Assert.AreEqual("https://example.test/index.json", request.Query["url"]);
        Assert.AreEqual("Example Source", request.Query["name"]);
    }

    [TestMethod]
    public void RejectTopicPluginSourceFromKindParameter()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction(
            "pcl://add-plugin-source?source=My-Plugin-Topic&sourceKind=topic&name=Community", out var request));

        Assert.IsFalse(PluginUriSourceParser.TryParseRepositorySource(request!, out _, out var error));
        StringAssert.Contains(error, "json 或 manifest");
    }

    [TestMethod]
    public void RejectTopicPluginSourcePrefix()
    {
        var request = CreateRequest("add-plugin-source", new() { ["source"] = "topic:community-plugins" });
        Assert.IsFalse(PluginUriSourceParser.TryParseRepositorySource(request, out _, out var error));
        StringAssert.Contains(error, "Topic");
    }

    [TestMethod]
    public void RejectPlainKeywordAsPluginSource()
    {
        var request = CreateRequest("add-plugin-source", new() { ["source"] = "community-plugins" });
        Assert.IsFalse(PluginUriSourceParser.TryParseRepositorySource(request, out _, out var error));
        StringAssert.Contains(error, "HTTP/HTTPS");
    }

    [TestMethod]
    [DataRow("kind")]
    [DataRow("type")]
    [DataRow("sourceKind")]
    public void ClassifyManifestPluginSourceFromSupportedKindAliases(string kindParameter)
    {
        var uri = "pcl://add-plugin-source?source=https%3A%2F%2Fexample.test%2Fplugin&"
                  + kindParameter + "=manifest";
        Assert.IsTrue(UriSchemeService.TryParseUriAction(uri, out var request));

        Assert.IsTrue(PluginUriSourceParser.TryParseRepositorySource(request!, out var source, out var error), error);
        Assert.AreEqual(PluginRepositorySourceKind.Manifest, source!.Kind);
        Assert.AreEqual("https://example.test/plugin", source.Value);
    }

    [TestMethod]
    public void ClassifyNetworkJsonPluginSource()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction(
            "pcl://add-plugin-source?json=https%3A%2F%2Fexample.test%2Fregistry", out var request));

        Assert.IsTrue(PluginUriSourceParser.TryParseRepositorySource(request!, out var source, out var error), error);
        Assert.AreEqual(PluginRepositorySourceKind.Json, source!.Kind);
        Assert.AreEqual("https://example.test/registry", source.Value);
    }

    [TestMethod]
    public void RejectStandaloneDeveloperSourceKind()
    {
        var request = CreateRequest("add-plugin-source", new()
        {
            ["source"] = "https://example.test/plugin-market.json",
            ["kind"] = "developers"
        });

        Assert.IsFalse(PluginUriSourceParser.TryParseRepositorySource(request, out _, out var error));
        StringAssert.Contains(error, "json 或 manifest");
    }

    [TestMethod]
    public void ActionsUriTypeRemainsActionInsteadOfSourceKind()
    {
        Assert.IsTrue(UriSchemeService.TryParseUriAction(
            "pcl://actions?type=add-plugin-source&source=https%3A%2F%2Fexample.test%2Fregistry.json", out var request));

        Assert.IsTrue(PluginUriSourceParser.TryParseRepositorySource(request!, out var source, out var error), error);
        Assert.AreEqual(PluginRepositorySourceKind.Json, source!.Kind);
    }

    [TestMethod]
    public void ClassifyExistingLocalJsonPluginSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-uri-source-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\"version\":1,\"plugins\":[]}");
        try
        {
            var request = CreateRequest("add-plugin-source", new() { ["source"] = path, ["kind"] = "json" });
            Assert.IsTrue(PluginUriSourceParser.TryParseRepositorySource(request, out var source, out var error), error);
            Assert.AreEqual(PluginRepositorySourceKind.Json, source!.Kind);
            Assert.AreEqual(Path.GetFullPath(path), source.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RejectMissingLocalJsonPluginSource()
    {
        var request = CreateRequest("add-plugin-source", new() { ["source"] = @"C:\missing\registry.json", ["kind"] = "json" });
        Assert.IsFalse(PluginUriSourceParser.TryParseRepositorySource(request, out _, out var error));
        StringAssert.Contains(error, "已有的本地 JSON 文件");
    }

    [TestMethod]
    public void InstallPluginManifestParameterForcesManifestHandling()
    {
        var request = CreateRequest("install-plugin", new() { ["manifest"] = "https://example.test/plugin-feed" });
        Assert.IsTrue(PluginUriSourceParser.TryParseInstallSource(request, out var source, out var error), error);
        Assert.AreEqual(PluginUriInstallSourceKind.Manifest, source!.Kind);
    }

    [TestMethod]
    public void InstallPluginPackageParameterSelectsRemotePackage()
    {
        var request = CreateRequest("install-plugin", new() { ["package"] = "https://example.test/plugin.pclx?download=1" });
        Assert.IsTrue(PluginUriSourceParser.TryParseInstallSource(request, out var source, out var error), error);
        Assert.AreEqual(PluginUriInstallSourceKind.RemotePackage, source!.Kind);
    }

    [TestMethod]
    [DataRow("file")]
    [DataRow("path")]
    public void InstallPluginFileAndPathParametersSelectLocalPackage(string parameter)
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-uri-package-" + Guid.NewGuid().ToString("N") + ".pclx");
        File.WriteAllBytes(path, []);
        try
        {
            var request = CreateRequest("install-plugin", new() { [parameter] = path });
            Assert.IsTrue(PluginUriSourceParser.TryParseInstallSource(request, out var source, out var error), error);
            Assert.AreEqual(PluginUriInstallSourceKind.LocalPackage, source!.Kind);
            Assert.AreEqual(Path.GetFullPath(path), source.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void InstallPluginGitParameterForcesGitHandling()
    {
        var request = CreateRequest("install-plugin", new() { ["git"] = "https://github.com/example/plugin" });
        Assert.IsTrue(PluginUriSourceParser.TryParseInstallSource(request, out var source, out var error), error);
        Assert.AreEqual(PluginUriInstallSourceKind.Git, source!.Kind);
    }

    [TestMethod]
    public void InstallPluginUrlInfersManifestAndPackage()
    {
        var manifestRequest = CreateRequest("install-plugin", new() { ["url"] = "https://example.test/manifest.json" });
        Assert.IsTrue(PluginUriSourceParser.TryParseInstallSource(manifestRequest, out var manifest, out var manifestError), manifestError);
        Assert.AreEqual(PluginUriInstallSourceKind.Manifest, manifest!.Kind);

        var packageRequest = CreateRequest("install-plugin", new() { ["source"] = "https://example.test/plugin.zip" });
        Assert.IsTrue(PluginUriSourceParser.TryParseInstallSource(packageRequest, out var package, out var packageError), packageError);
        Assert.AreEqual(PluginUriInstallSourceKind.RemotePackage, package!.Kind);
    }

    [TestMethod]
    public void NormalizeUriArgumentForCommandLine()
    {
        var uri = "pcl://actions?type=ShowHint&data=Hi";
        var args = UriSchemeService.NormalizeFullCommandLineArguments(["PCL.exe", uri]);

        CollectionAssert.AreEqual(new[] { "PCL.exe", "uri", "--uri", uri }, args);

        var model = CommandLine.Parse(args, [("uri", [])]);
        Assert.AreEqual("uri", model.SubcommandText);
        var (exists, isTypeMatch) = model.Subcommand!.TryGetArgumentValue<string>("uri", out var value);
        Assert.IsTrue(exists);
        Assert.IsTrue(isTypeMatch);
        Assert.AreEqual(uri, value);
    }

    [TestMethod]
    public void NormalizePluginPackageArgumentForCommandLine()
    {
        var packagePath = @"C:\Plugins\example.pclx";
        var args = UriSchemeService.NormalizeFullCommandLineArguments(["PCL.exe", packagePath]);

        CollectionAssert.AreEqual(new[] { "PCL.exe", "uri", "--action", "install-plugin", "--file", packagePath }, args);

        var model = CommandLine.Parse(args, [("uri", [])]);
        Assert.AreEqual("uri", model.SubcommandText);
        var (exists, isTypeMatch) = model.Subcommand!.TryGetArgumentValue<string>("file", out var value);
        Assert.IsTrue(exists);
        Assert.IsTrue(isTypeMatch);
        Assert.AreEqual(packagePath, value);
    }

    [TestMethod]
    public void IgnoreUnsupportedScheme()
    {
        Assert.IsFalse(UriSchemeService.TryParseUriAction("https://actions?type=ShowHint", out _));
        Assert.IsFalse(UriSchemeService.TryConvertUriArgument("https://actions?type=ShowHint", out _));
    }

    private static UriActionRequest CreateRequest(string action, System.Collections.Generic.Dictionary<string, string> query)
        => new("test", action, action, null, string.Empty, [], query);
}
