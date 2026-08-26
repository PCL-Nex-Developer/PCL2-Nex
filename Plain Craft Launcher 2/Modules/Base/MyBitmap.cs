using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCL.Core.IO;
using PCL.Core.UI.Media;

namespace PCL;

/// <summary>
/// A small bitmap wrapper backed by WPF-compatible image APIs.
/// </summary>
public sealed class MyBitmap
{
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new(StringComparer.Ordinal);

    private readonly BitmapSource _source;

    public int PixelWidth => _source.PixelWidth;

    public int PixelHeight => _source.PixelHeight;

    public BitmapSource Source => _source;

    public MyBitmap() : this(CreateSource(1, 1, new byte[4]), true)
    {
    }

    public MyBitmap(string filePathOrResourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathOrResourceName);
        filePathOrResourceName = filePathOrResourceName.Replace(
            "pack://application:,,,/images/", ModBase.pathImage, StringComparison.OrdinalIgnoreCase);
        _source = Cache.GetOrAdd(filePathOrResourceName, LoadSource);
    }

    public MyBitmap(ImageSource image) : this(ToBitmapSource(image), true)
    {
    }

    public MyBitmap(ImageBrush image) : this(image.ImageSource ?? throw new ArgumentException("ImageBrush has no image source.", nameof(image)))
    {
    }

    private MyBitmap(BitmapSource source, bool normalized)
    {
        _source = normalized ? source : ToBitmapSource(source);
    }

    public static implicit operator ImageSource?(MyBitmap? image) => image?._source;

    public static implicit operator ImageBrush?(MyBitmap? image) => image is null ? null : new ImageBrush(image._source);

    public MyBitmap Clip(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x > PixelWidth - width || y > PixelHeight - height)
            throw new ArgumentOutOfRangeException(nameof(width), "The requested crop is outside the image bounds.");

        var pixels = new byte[checked(width * height * 4)];
        _source.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        return new MyBitmap(CreateSource(width, height, pixels), true);
    }

    public MyBitmap Scale(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var sourcePixels = GetPixels();
        var targetPixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sourceY = y * PixelHeight / height;
            for (var x = 0; x < width; x++)
            {
                var sourceX = x * PixelWidth / width;
                var sourceOffset = (sourceY * PixelWidth + sourceX) * 4;
                var targetOffset = (y * width + x) * 4;
                targetPixels[targetOffset] = sourcePixels[sourceOffset];
                targetPixels[targetOffset + 1] = sourcePixels[sourceOffset + 1];
                targetPixels[targetOffset + 2] = sourcePixels[sourceOffset + 2];
                targetPixels[targetOffset + 3] = sourcePixels[sourceOffset + 3];
            }
        }

        return new MyBitmap(CreateSource(width, height, targetPixels), true);
    }

    /// <summary>
    /// Overlays non-transparent source pixels. This is intentionally nearest-neighbor friendly for skins.
    /// </summary>
    public MyBitmap Overlay(MyBitmap overlay, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var targetPixels = GetPixels();
        var overlayPixels = overlay.GetPixels();

        for (var overlayY = 0; overlayY < overlay.PixelHeight; overlayY++)
        {
            var targetY = y + overlayY;
            if (targetY < 0 || targetY >= PixelHeight) continue;

            for (var overlayX = 0; overlayX < overlay.PixelWidth; overlayX++)
            {
                var targetX = x + overlayX;
                if (targetX < 0 || targetX >= PixelWidth) continue;

                var sourceOffset = (overlayY * overlay.PixelWidth + overlayX) * 4;
                if (overlayPixels[sourceOffset + 3] == 0) continue;

                var targetOffset = (targetY * PixelWidth + targetX) * 4;
                targetPixels[targetOffset] = overlayPixels[sourceOffset];
                targetPixels[targetOffset + 1] = overlayPixels[sourceOffset + 1];
                targetPixels[targetOffset + 2] = overlayPixels[sourceOffset + 2];
                targetPixels[targetOffset + 3] = overlayPixels[sourceOffset + 3];
            }
        }

        return new MyBitmap(CreateSource(PixelWidth, PixelHeight, targetPixels), true);
    }

    public System.Windows.Media.Color GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= PixelWidth || y >= PixelHeight)
            throw new ArgumentOutOfRangeException(nameof(x));

        var pixels = new byte[4];
        _source.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return System.Windows.Media.Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    public void Save(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_source));
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fileStream);
    }

    public static MyBitmap Create(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return new MyBitmap(CreateSource(width, height, new byte[checked(width * height * 4)]), true);
    }

    private static BitmapSource LoadSource(string source)
    {
        if (source.StartsWith(ModBase.pathImage, StringComparison.OrdinalIgnoreCase))
            return LoadUri(source);

        var path = FileSystemPath.NormalizeSeparators(source);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > 2 && stream.ReadByte() == 'R' && stream.ReadByte() == 'I')
        {
            stream.Position = 0;
            using var pngStream = stream.FromWebpToPng();
            return Decode(pngStream);
        }

        stream.Position = 0;
        return Decode(stream);
    }

    private static BitmapSource LoadUri(string source)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(source, UriKind.Absolute);
        bitmap.EndInit();
        return ToBitmapSource(bitmap);
    }

    private static BitmapSource Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The image contains no decodable frames.");
        return ToBitmapSource(decoder.Frames[0]);
    }

    private static BitmapSource ToBitmapSource(ImageSource image)
    {
        if (image is not BitmapSource bitmap)
            throw new ArgumentException("The image source must be a bitmap.", nameof(image));

        var converted = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return CreateSource(converted.PixelWidth, converted.PixelHeight, pixels, converted.DpiX, converted.DpiY);
    }

    private static BitmapSource CreateSource(int width, int height, byte[] pixels, double dpiX = 96, double dpiY = 96)
    {
        var source = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private byte[] GetPixels()
    {
        var pixels = new byte[checked(PixelWidth * PixelHeight * 4)];
        _source.CopyPixels(pixels, PixelWidth * 4, 0);
        return pixels;
    }
}
