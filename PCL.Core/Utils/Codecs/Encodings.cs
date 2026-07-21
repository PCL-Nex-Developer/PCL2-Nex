using System.Text;

namespace PCL.Core.Utils.Codecs;

public static class Encodings {
    public static readonly Encoding GB18030;
    public static readonly Encoding GB2312;

    static Encodings() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GB18030 = Encoding.GetEncoding("GB18030");
        GB2312 = Encoding.GetEncoding("GB2312");
    }
}
