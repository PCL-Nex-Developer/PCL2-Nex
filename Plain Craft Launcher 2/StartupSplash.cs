using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCL;

public interface IStartupSplash
{
    void Show(bool autoClose, bool topMost);
    void Close(TimeSpan fadeoutDuration);
}

public sealed class ResourceStartupSplash(string resourceName) : IStartupSplash
{
    private readonly SplashScreen _splash = new(resourceName);

    public void Show(bool autoClose, bool topMost) => _splash.Show(autoClose, topMost);

    public void Close(TimeSpan fadeoutDuration) => _splash.Close(fadeoutDuration);
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