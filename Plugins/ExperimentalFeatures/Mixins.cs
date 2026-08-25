using System;
using System.Windows;
using PCL;
using PCL.Mixin;

namespace PclNex.ExperimentalFeatures.Mixins;

/// <summary>来源：Meloong-Git/PCL #9168。</summary>
[Mixin(typeof(MySlider))]
public static class SliderKeyboardPrecisionMixin
{
    [Inject(".ctor", At = MixinAt.Tail)]
    public static void EnablePrecisionKeyboardStep([This] MySlider slider)
    {
        // XAML 属性在构造函数结束后才会赋值，因此在 Loaded 时覆盖各页面的 ValueByKey。
        slider.Loaded += OnSliderLoaded;
    }

    private static void OnSliderLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is MySlider slider) slider.ValueByKey = 1;
    }
}

/// <summary>来源：Meloong-Git/PCL #9274，仅覆盖打开网页的入口。</summary>
[Mixin(typeof(ModBase))]
public static class OpenWebsiteHttpsMixin
{
    [Inject(nameof(ModBase.OpenWebsite), At = MixinAt.Head)]
    public static void AddHttpsScheme([Arg(0)] ref string url)
    {
        if (string.IsNullOrEmpty(url) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("minecraft://", StringComparison.OrdinalIgnoreCase)) return;

        url = "https://" + url;
    }
}
