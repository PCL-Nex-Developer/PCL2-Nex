using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL;

namespace PCL.Core.Test.Minecraft;

[TestClass]
public class McVersionClassifierTest
{
    [TestMethod]
    public void VersionToDropSupportsOldAndNewReleaseNames()
    {
        Assert.AreEqual(130, McVersionClassifier.VersionToDrop("1.13.2"));
        Assert.AreEqual(140, McVersionClassifier.VersionToDrop("1.14.2"));
        Assert.AreEqual(261, McVersionClassifier.VersionToDrop("26.1.2"));
    }

    [TestMethod]
    public void VersionToDropHandlesSnapshotSuffixesExplicitly()
    {
        Assert.AreEqual(0, McVersionClassifier.VersionToDrop("26.1-snapshot-1"));
        Assert.AreEqual(261, McVersionClassifier.VersionToDrop("26.1-snapshot-1", true));
    }
}
