using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Minecraft.Launch;

namespace PCL.Core.Test.Minecraft.Launch;

[TestClass]
public class MinecraftPlatformRulesTest
{
    [TestMethod]
    public void Check_SelectsOnlyTheCurrentOperatingSystem()
    {
        var windowsOnly = JsonNode.Parse("""
            [{ "action": "allow", "os": { "name": "windows" } }]
            """);
        var macOsOnly = JsonNode.Parse("""
            [{ "action": "allow", "os": { "name": "osx" } }]
            """);

        Assert.IsFalse(MinecraftPlatformRules.Check(windowsOnly, "osx", Architecture.X64, "15.7.7"));
        Assert.IsTrue(MinecraftPlatformRules.Check(macOsOnly, "osx", Architecture.X64, "15.7.7"));
    }

    [TestMethod]
    public void Check_UsesOperatingSystemArchitecture()
    {
        var x64Only = JsonNode.Parse("""
            [{ "action": "allow", "os": { "name": "osx", "arch": "x86_64" } }]
            """);

        Assert.IsTrue(MinecraftPlatformRules.Check(x64Only, "osx", Architecture.X64, "15.7.7"));
        Assert.IsFalse(MinecraftPlatformRules.Check(x64Only, "osx", Architecture.Arm64, "15.7.7"));
    }

    [TestMethod]
    public void ResolveNativeClassifier_ReplacesLegacyArchitecturePlaceholder()
    {
        Assert.AreEqual("natives-windows-64",
            MinecraftPlatformRules.ResolveNativeClassifier("natives-windows-${arch}", Architecture.X64));
        Assert.AreEqual("natives-windows-32",
            MinecraftPlatformRules.ResolveNativeClassifier("natives-windows-${arch}", Architecture.X86));
        Assert.AreEqual("natives-osx",
            MinecraftPlatformRules.ResolveNativeClassifier("natives-osx", Architecture.X64));
    }
}
