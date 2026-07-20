using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PCL;

public interface IStartupSplash
{
    void Show(bool autoClose, bool topMost);
    void Close(TimeSpan fadeoutDuration);
}

public sealed class ResourceStartupSplash : Window, IStartupSplash
{
    private const double SplashSize = 160;
    private bool _closing;

    public ResourceStartupSplash(string resourceName)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = SplashSize;
        Height = SplashSize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var resourcePath = resourceName.Replace('\\', '/');
        Content = new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute)),
            Width = SplashSize,
            Height = SplashSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode((Image)Content, BitmapScalingMode.HighQuality);
    }

    public void Show(bool autoClose, bool topMost)
    {
        Topmost = topMost;
        base.Show();
        if (autoClose) Close(TimeSpan.Zero);
    }

    public void Close(TimeSpan fadeoutDuration)
    {
        if (_closing) return;
        _closing = true;
        if (fadeoutDuration <= TimeSpan.Zero || !IsVisible)
        {
            CloseImmediately();
            return;
        }

        var animation = new DoubleAnimation(Opacity, 0, fadeoutDuration)
        {
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => CloseImmediately();
        BeginAnimation(OpacityProperty, animation);
    }

    private void CloseImmediately()
    {
        BeginAnimation(OpacityProperty, null);
        base.Close();
    }
}

public sealed class FileStartupSplash : Window, IStartupSplash
{
    public FileStartupSplash(string imagePath)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new Border
        {
            Background = Brushes.Transparent,
            Child = new Image
            {
                Source = new BitmapImage(new Uri(imagePath, UriKind.Absolute)),
                Width = 128,
                Height = 128,
                Stretch = Stretch.Uniform
            }
        };
    }

    public void Show(bool autoClose, bool topMost)
    {
        Topmost = topMost;
        Show();
        if (autoClose) Close(TimeSpan.Zero);
    }

    public void Close(TimeSpan fadeoutDuration)
    {
        base.Close();
    }
}
