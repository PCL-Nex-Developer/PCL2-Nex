using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.Test.Network;

[TestClass]
public class GitHubAcceleratorTest
{
    [TestMethod]
    [DataRow("https://github.com/PCL-Nex-Developer/PCL2-Nex/releases")]
    [DataRow("https://raw.githubusercontent.com/PCL-Nex-Developer/PCL2-Nex/main/README.md")]
    [DataRow("https://gist.githubusercontent.com")]
    [DataRow("https://gist.githubusercontent.com/user/hash/raw/file.txt")]
    [DataRow("https://avatars.githubusercontent.com/u/1?v=4")]
    public void Rewrite_ShouldPrefixSupportedGithubUrls(string url)
    {
        var result = GitHubAccelerator.Rewrite(url, 2);

        Assert.AreEqual("https://v4.gh-proxy.org/" + url, result);
    }

    [TestMethod]
    [DataRow("https://api.github.com/repos/PCL-Nex-Developer/PCL2-Nex")]
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
}