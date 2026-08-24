using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL;

/// <summary>
/// 非完整媒体管线环境下的 MediaElement 替身，仅保持布局与 API 兼容，不进行实际播放。
/// </summary>
public class StubMediaElement : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Uri), typeof(StubMediaElement), new PropertyMetadata(null));

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(StubMediaElement), new PropertyMetadata(TimeSpan.Zero));

    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
        nameof(Volume), typeof(double), typeof(StubMediaElement), new PropertyMetadata(0.5));

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public Stretch Stretch { get; set; } = Stretch.Uniform;

    public MediaState LoadedBehavior { get; set; } = MediaState.Manual;

    public MediaState UnloadedBehavior { get; set; } = MediaState.Stop;

    public event RoutedEventHandler? MediaEnded;

    public event EventHandler<ExceptionRoutedEventArgs>? MediaFailed;

    public void Play() { }

    public void Pause() { }

    public void Stop() { }

    public void Close() { }

    protected virtual void OnMediaEnded() => MediaEnded?.Invoke(this, new RoutedEventArgs());

    protected virtual void OnMediaFailed(ExceptionRoutedEventArgs args) =>
        MediaFailed?.Invoke(this, args);
}
