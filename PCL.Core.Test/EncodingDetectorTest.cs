using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Utils.Codecs;

namespace PCL.Core.Test;
[TestClass]
public class EncodingDetectorTest
{
    [TestMethod]
    public void TestEncoding()
    {
        var utf8 = Encoding.UTF8.GetBytes("Hi, There!");
        Assert.AreEqual(EncodingDetector.DetectEncoding(utf8), Encoding.UTF8);
        utf8 = Encoding.UTF8.GetBytes("棍斤拷烫烫烫");
        Assert.AreEqual(EncodingDetector.DetectEncoding(utf8), Encoding.UTF8);
        var gb = Encodings.GB2312.GetBytes("你好世界");
        Assert.AreEqual(EncodingDetector.DetectEncoding(gb), Encodings.GB2312);
        var gb18030 = Encodings.GB18030.GetBytes("😀");
        Assert.AreEqual(EncodingDetector.DetectEncoding(gb18030), Encodings.GB18030);
        byte[] nonEncode = [0xfe, 0x5f, 0xa1];
        Assert.AreEqual(Encoding.Default, EncodingDetector.DetectEncoding(nonEncode));
    }

    [TestMethod]
    public void DetectEncodingShouldRecognizeBomForNonEmptyFiles()
    {
        Assert.AreEqual(Encoding.UTF8,
            EncodingDetector.DetectEncoding([0xef, 0xbb, 0xbf, 0x41]));
        Assert.AreEqual(Encoding.Unicode,
            EncodingDetector.DetectEncoding([0xff, 0xfe, 0x41, 0x00]));
        Assert.AreEqual(Encoding.BigEndianUnicode,
            EncodingDetector.DetectEncoding([0xfe, 0xff, 0x00, 0x41]));
        Assert.AreEqual(Encoding.UTF32,
            EncodingDetector.DetectEncoding([0xff, 0xfe, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00]));
    }
}
