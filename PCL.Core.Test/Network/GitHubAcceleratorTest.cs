using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.Test.Network;

[TestClass]
public class GitHubAcceleratorTest
{
    [TestMethod]
    [DataRow("https://github.com/PCL-Nex-Developer/PCL2-Nex/releases")]
    [DataRow("https://api.github.com/repos/PCL-Nex-Developer/PCL2-Nex")]
    [DataRow("https://raw.githubusercontent.com/PCL-Nex-Developer/PCL2-Nex/main/README.md")]
    [DataRow("https://objects.githubusercontent.com/github-production-release-asset/file")]
    [DataRow("https://release-assets.githubusercontent.com/github-production-release-asset/file")]
    [DataRow("https://gist.githubusercontent.com")]
    [DataRow("https://gist.githubusercontent.com/user/hash/raw/file.txt")]
    [DataRow("https://avatars.githubusercontent.com/u/1?v=4")]
    public void Rewrite_ShouldPrefixSupportedGithubUrls(string url)
    {
        var result = GitHubAccelerator.Rewrite(url, 2);

        Assert.AreEqual("https://v4.gh-proxy.org/" + url, result);
    }

    [TestMethod]
    [DataRow("http://github.com/PCL-Nex-Developer/PCL2-Nex")]
    [DataRow("https://example.com/github.com/PCL-Nex-Developer/PCL2-Nex")]
    public void Rewrite_ShouldIgnoreUnsupportedUrls(string url)
    {
        var result = GitHubAccelerator.Rewrite(url, 1);

        Assert.AreEqual(url, result);
    }

    [TestMethod]
    public void Rewrite_ShouldNotPrefixAlreadyAcceleratedUrl()
    {
        const string url = "https://gh-proxy.org/https://github.com/PCL-Nex-Developer/PCL2-Nex";

        var result = GitHubAccelerator.Rewrite(url, 4);

        Assert.AreEqual(url, result);
    }

    [TestMethod]
    public void GetSpeedTestUrl_ShouldUseMirrorSpeedTestEndpoint()
    {
        var result = GitHubAccelerator.GetSpeedTestUrl(3);

        Assert.AreEqual("https://v6.gh-proxy.org/__speedtest/download?size=26214400", result);
    }

    [TestMethod]
    public void Rewrite_ShouldPreserveApiSearchQueryWithoutDoubleEncoding()
    {
        const string url = "https://api.github.com/search/repositories?q=topic%3Apclnexplugin&sort=updated&order=desc&page=2";

        var result = GitHubAccelerator.Rewrite(url, 1);

        Assert.AreEqual("https://gh-proxy.org/" + url, result);
        Assert.IsFalse(result.Contains("%253A", System.StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GetRequestCandidates_ShouldFallbackToOriginalUrl()
    {
        const string url = "https://api.github.com/search/repositories?q=topic%3Apclnexplugin&page=2";

        var candidates = GitHubAccelerator.GetRequestCandidates(url, 2);

        CollectionAssert.AreEqual(new[] { "https://v4.gh-proxy.org/" + url, url }, candidates.ToArray());
    }

    [TestMethod]
    public void Rewrite_ShouldOnlyAccelerateSelectedDomains()
    {
        var selected = new[] { "api.github.com", "release-assets.githubusercontent.com" };

        Assert.AreEqual(
            "https://gh-proxy.org/https://api.github.com/repos/example/test",
            GitHubAccelerator.Rewrite("https://api.github.com/repos/example/test", 1, selected));
        Assert.AreEqual(
            "https://github.com/example/test",
            GitHubAccelerator.Rewrite("https://github.com/example/test", 1, selected));
    }
}
