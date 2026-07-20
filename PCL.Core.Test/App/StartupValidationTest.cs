using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Essentials;

namespace PCL.Core.Test.App;

[TestClass]
public sealed class StartupValidationTest
{
    [TestMethod]
    [DoNotParallelize]
    public void EnsureWindowsDirectoryEnvironment_ShouldRepairMissingProcessVariable()
    {
        var original = Environment.GetEnvironmentVariable("windir", EnvironmentVariableTarget.Process);
        var expected = Environment.GetEnvironmentVariable("SystemRoot", EnvironmentVariableTarget.Process);
        Assert.IsFalse(string.IsNullOrWhiteSpace(expected), "测试环境缺少 SystemRoot。");

        try
        {
            Environment.SetEnvironmentVariable("windir", null, EnvironmentVariableTarget.Process);

            var resolved = StartupValidation.EnsureWindowsDirectoryEnvironment();

            Assert.AreEqual(expected, resolved, ignoreCase: true);
            Assert.AreEqual(expected,
                Environment.GetEnvironmentVariable("windir", EnvironmentVariableTarget.Process),
                ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("windir", original, EnvironmentVariableTarget.Process);
        }
    }
}
