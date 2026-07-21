using System;
using System.IO;
using System.Text;

namespace PCL.Core.Utils.Codecs;

public static class EncodingDetector
{
    private const int MaxDetectionBytes = 64 * 1024;

    private static readonly Encoding _StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding _StrictGb2312 = Encoding.GetEncoding(
        Encodings.GB2312.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);
    private static readonly Encoding _StrictGb18030 = Encoding.GetEncoding(
        Encodings.GB18030.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);
    private static readonly Encoding _Utf32BigEndian = new UTF32Encoding(true, true);

    /// <summary>
    /// 检测流中的文本编码方式（支持 Seek 的流）
    /// </summary>
    /// <param name="stream">输入流，必须支持 Seek</param>
    /// <param name="readFromBegin">是否将流重置到起始点</param>
    /// <returns>检测到的 Unicode 或中文代码页编码；未识别时返回系统默认编码。</returns>
    public static Encoding DetectEncoding(Stream stream, bool readFromBegin = false)
    {
        if (!stream.CanRead)
            throw new ArgumentException("流必须支持读操作");
        if (!stream.CanSeek)
            throw new ArgumentException("流必须支持 Seek 操作");

        var originalPosition = stream.Position;
        if (readFromBegin) stream.Seek(0, SeekOrigin.Begin);

        try
        {
            return _DetectByBom(stream, originalPosition) ?? _DetectWithoutBOM(stream, originalPosition) ?? Encoding.Default;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    public static Encoding DetectEncoding(byte[] bytes)
    {
        return DetectEncoding(new MemoryStream(bytes), true);
    }

    /// <summary>
    /// 根据 BOM 判断编码
    /// </summary>
    private static Encoding? _DetectByBom(Stream stream, long originalPosition)
    {
        stream.Position = originalPosition;
        // 获取最长样本长度
        var readableLength = stream.Length - stream.Position;
        var sampleLength = Math.Min(readableLength, 4);
        var buffer = new byte[sampleLength];
        var actualRead = stream.Read(buffer, 0, buffer.Length);
        if (actualRead != sampleLength) throw new Exception("无法获取样本长度");

        // 对样本进行分析
        if (sampleLength >= 4)
        {
            if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xfe && buffer[3] == 0xff)
                return _Utf32BigEndian;
            if (buffer[0] == 0xff && buffer[1] == 0xfe && buffer[2] == 0x00 && buffer[3] == 0x00)
                return Encoding.UTF32;
        }

        if (sampleLength >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
            return Encoding.UTF8;

        if (sampleLength >= 2 && buffer[0] == 0xfe && buffer[1] == 0xff)
            return Encoding.BigEndianUnicode;
        if (sampleLength >= 2 && buffer[0] == 0xff && buffer[1] == 0xfe)
            return Encoding.Unicode;

        return null;
    }

    /// <summary>
    /// BOM 不存在时的备用检测策略
    /// </summary>
    private static Encoding? _DetectWithoutBOM(Stream stream, long originalPosition)
    {
        if (_CanDecode(stream, originalPosition, _StrictUtf8)) return Encoding.UTF8;
        if (_CanDecode(stream, originalPosition, _StrictGb2312)) return Encodings.GB2312;
        if (_CanDecode(stream, originalPosition, _StrictGb18030)) return Encodings.GB18030;
        return null;
    }

    /// <summary>
    /// 使用严格解码器验证流开头是否符合指定编码。
    /// </summary>
    private static bool _CanDecode(Stream stream, long originalPosition, Encoding encoding)
    {
        stream.Position = originalPosition;
        var buffer = new byte[4096];
        var decoder = encoding.GetDecoder();
        var remaining = Math.Min(MaxDetectionBytes, stream.Length - originalPosition);

        try
        {
            while (remaining > 0)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) break;

                remaining -= read;
                var reachedEnd = stream.Position >= stream.Length;
                _ = decoder.GetCharCount(buffer, 0, read, reachedEnd);
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
