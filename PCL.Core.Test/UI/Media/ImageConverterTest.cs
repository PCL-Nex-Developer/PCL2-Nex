using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.UI.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace PCL.Core.Test.UI.Media;

[TestClass]
public class ImageConverterTest
{
    [TestMethod]
    public void FromWebpToPng_ShouldPreserveRedAndBlueChannels()
    {
        using var source = new Image<Rgba32>(2, 1);
        source[0, 0] = new Rgba32(0, 0, 255, 255);
        source[1, 0] = new Rgba32(255, 0, 0, 255);

        using var webp = new MemoryStream();
        source.Save(webp, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        webp.Position = 0;

        using var png = webp.FromWebpToPng();
        using var result = Image.Load<Rgba32>(png);

        Assert.AreEqual(new Rgba32(0, 0, 255, 255), result[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0, 255), result[1, 0]);
    }

    [TestMethod]
    public void Bgra32ToPng_ShouldPreserveChannelsAndAlpha()
    {
        byte[] pixels =
        [
            255, 0, 0, 255,
            0, 0, 255, 64
        ];

        using var png = ImageConverter.Bgra32ToPng(pixels, 2, 1);
        using var result = Image.Load<Rgba32>(png);

        Assert.AreEqual(new Rgba32(0, 0, 255, 255), result[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0, 64), result[1, 0]);
    }
}
