using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.UI.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
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

    [TestMethod]
    public void NormalizeToPng_ShouldProduceCanonicalRgbaForRgbAndGrayscaleInputs()
    {
        using var rgb = new Image<Rgb24>(1, 1);
        rgb[0, 0] = new Rgb24(0, 96, 255);
        using var rgbInput = new MemoryStream();
        rgb.Save(rgbInput, new PngEncoder { ColorType = PngColorType.Rgb });
        rgbInput.Position = 0;

        using var rgbPng = ImageConverter.NormalizeToPng(rgbInput);
        AssertCanonicalPng(rgbPng, new Rgba32(0, 96, 255, 255));

        using var gray = new Image<L8>(1, 1);
        gray[0, 0] = new L8(37);
        using var grayInput = new MemoryStream();
        gray.Save(grayInput, new PngEncoder { ColorType = PngColorType.Grayscale });
        grayInput.Position = 0;

        using var grayPng = ImageConverter.NormalizeToPng(grayInput);
        AssertCanonicalPng(grayPng, new Rgba32(37, 37, 37, 255));
    }

    [TestMethod]
    public void NormalizeToPng_ShouldPreservePaletteWebpAndAlphaColors()
    {
        using var source = new Image<Rgba32>(2, 1);
        source[0, 0] = new Rgba32(0, 0, 255, 255);
        source[1, 0] = new Rgba32(255, 128, 0, 64);

        using var paletteInput = new MemoryStream();
        source.Save(paletteInput, new PngEncoder { ColorType = PngColorType.Palette });
        paletteInput.Position = 0;
        using var palettePng = ImageConverter.NormalizeToPng(paletteInput);
        AssertCanonicalPng(palettePng, source[0, 0], source[1, 0]);

        using var webpInput = new MemoryStream();
        source.Save(webpInput, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        webpInput.Position = 0;
        using var webpPng = ImageConverter.NormalizeToPng(webpInput);
        AssertCanonicalPng(webpPng, source[0, 0], source[1, 0]);
    }

    private static void AssertCanonicalPng(Stream stream, params Rgba32[] expected)
    {
        stream.Position = 0;
        using var result = Image.Load<Rgba32>(stream);
        var metadata = result.Metadata.GetPngMetadata();
        Assert.AreEqual(PngColorType.RgbWithAlpha, metadata.ColorType);
        Assert.AreEqual(PngBitDepth.Bit8, metadata.BitDepth);
        Assert.AreEqual(expected.Length, result.Width);
        for (var x = 0; x < expected.Length; x++)
            Assert.AreEqual(expected[x], result[x, 0]);
    }
}
