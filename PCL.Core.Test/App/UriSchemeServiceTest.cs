using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Cli;
using PCL.Core.App.Essentials;

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
}