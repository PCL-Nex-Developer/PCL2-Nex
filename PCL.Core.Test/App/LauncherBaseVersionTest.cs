using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;
using PCL.Core.App.Plugins;
using PCL.Core.Utils;

namespace PCL.Core.Test.App;

[TestClass]
public class LauncherBaseVersionTest
{
    [TestMethod]
    public void Parse_ShouldCompareYearMonthAndPatchNumerically()
    {
        Assert.IsTrue(LauncherBaseVersion.Parse("2026.08.1") > LauncherBaseVersion.Parse("2026.07.99"));
        Assert.IsTrue(LauncherBaseVersion.Parse("2027.01.0") > LauncherBaseVersion.Parse("2026.12.999"));
        Assert.AreEqual("2026.07.2", LauncherBaseVersion.Parse("2026.07.2").ToString());
    }

    [TestMethod]
    [DataRow("2026.7.1")]
    [DataRow("26.07.1")]
    [DataRow("2026.13.1")]
    [DataRow("2026.07.01")]
    [DataRow("2026.07")]
    [DataRow("v2026.07.1")]
    [DataRow(" 2026.07.1")]
    [DataRow("2026.07.1 ")]
    [DataRow("0000.07.1")]
    public void TryParse_ShouldRejectNonCanonicalValues(string value)
    {
        Assert.IsFalse(LauncherBaseVersion.TryParse(value, out _));
    }

    [TestMethod]
    public void JsonConverter_ShouldRoundTripCanonicalBaseVersion()
    {
        var model = JsonSerializer.Deserialize<LauncherVersionModel>(
            """{"base":"2026.07.2"}""", JsonCompat.SerializerOptions);

        Assert.IsNotNull(model);
        Assert.AreEqual(LauncherBaseVersion.Parse("2026.07.2"), model.BaseVersion);
        Assert.AreEqual("2026.07.2", model.ToString());
        Assert.AreEqual("{\"base\":\"2026.07.2\"}",
            JsonSerializer.Serialize(model, JsonCompat.SerializerOptions));
    }

    [TestMethod]
    [DataRow("{\"base\":\"3.0.3\"}")]
    [DataRow("{\"base\":\"2026.7.1\"}")]
    [DataRow("{}")]
    [DataRow("{\"name\":\"2026.07.1\",\"code\":1}")]
    [DataRow("{\"base\":\"2026.07.1\",\"code\":1}")]
    public void LauncherVersionModel_ShouldRejectInvalidOrLegacySchema(string json)
    {
        Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize<LauncherVersionModel>(json, JsonCompat.SerializerOptions));
    }

    [TestMethod]
    public void Compatibility_ShouldReturnAllFourStates()
    {
        Assert.AreEqual(PluginCoreCompatibilityStatus.Compatible,
            PluginCompatibility.EvaluatePclCoreVersion("2026.07.2", "2026.08.1", "2026.07.1"));
        Assert.AreEqual(PluginCoreCompatibilityStatus.TooOld,
            PluginCompatibility.EvaluatePclCoreVersion("2026.06.9", "2026.08.1", "2026.07.1"));
        Assert.AreEqual(PluginCoreCompatibilityStatus.Future,
            PluginCompatibility.EvaluatePclCoreVersion("2026.09.1", "2026.08.1", "2026.07.1"));
        Assert.AreEqual(PluginCoreCompatibilityStatus.Unknown,
            PluginCompatibility.EvaluatePclCoreVersion("latest", "2026.08.1", "2026.07.1"));
    }

    [TestMethod]
    public async Task FutureCompatibility_ShouldInvokeConfirmationHook()
    {
        var previous = PluginCompatibility.ConfirmationAsync;
        PluginCompatibilityConfirmationContext? captured = null;
        try
        {
            PluginCompatibility.ConfirmationAsync = (context, _) =>
            {
                captured = context;
                return Task.FromResult(true);
            };
            var manifest = new PluginPackageManifest
            {
                Id = "example.future",
                Name = "Future",
                PclCoreVersion = "2099.01.1"
            };

            Assert.IsTrue(await PluginCompatibility.ConfirmIfRequiredAsync(manifest, PluginCompatibilityAction.Install));
            Assert.IsNotNull(captured);
            Assert.AreEqual(PluginCoreCompatibilityStatus.Future, captured.Status);
            Assert.AreEqual(PluginCompatibilityAction.Install, captured.Action);
        }
        finally
        {
            PluginCompatibility.ConfirmationAsync = previous;
        }
    }
}
