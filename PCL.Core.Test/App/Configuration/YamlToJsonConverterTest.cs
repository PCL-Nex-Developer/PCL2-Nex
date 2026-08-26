using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Configuration.Storage;

namespace PCL.Core.Test.App.Configuration;

[TestClass]
public class YamlToJsonConverterTest
{
    [TestMethod]
    public void Convert_ShouldPreserveScalarAndCollectionTypes()
    {
        const string yaml = "Enabled: true\nCount: 3\nNames:\n- alpha\n- beta\nNested:\n  Value: text\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        using var output = new MemoryStream();

        YamlToJsonConverter.Convert(input, output, leaveOpen: true);
        output.Position = 0;
        var root = JsonNode.Parse(output)!.AsObject();

        Assert.IsTrue(root["Enabled"]!.GetValue<bool>());
        Assert.AreEqual(3, root["Count"]!.GetValue<int>());
        Assert.AreEqual("beta", root["Names"]![1]!.GetValue<string>());
        Assert.AreEqual("text", root["Nested"]!["Value"]!.GetValue<string>());
    }
}
