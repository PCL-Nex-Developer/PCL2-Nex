using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL.Core.App.Plugins.JavaScript;

public sealed class JavaScriptElement(FrameworkElement native, JavaScriptRuntime runtime)
{
    internal FrameworkElement Native => native;

    public JavaScriptRawObject Raw => new(native, runtime);
    public JavaScriptRawObject raw => Raw;

    public string Name => native.Name;
    public string name => Name;

    public string Text
    {
        get => native switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            ContentControl contentControl => contentControl.Content?.ToString() ?? string.Empty,
            _ => string.Empty
        };
        set
        {
            switch (native)
            {
                case TextBlock textBlock: textBlock.Text = value; break;
                case TextBox textBox: textBox.Text = value; break;
                case ContentControl contentControl: contentControl.Content = value; break;
            }
        }
    }

    public string text { get => Text; set => Text = value; }
    public double Width { get => native.Width; set => native.Width = value; }
    public double width { get => Width; set => Width = value; }
    public double Height { get => native.Height; set => native.Height = value; }
    public double height { get => Height; set => Height = value; }
    public double MinWidth { get => native.MinWidth; set => native.MinWidth = value; }
    public double minWidth { get => MinWidth; set => MinWidth = value; }
    public double MinHeight { get => native.MinHeight; set => native.MinHeight = value; }
    public double minHeight { get => MinHeight; set => MinHeight = value; }
    public object? Tag { get => native.Tag; set => native.Tag = value; }
    public object? tag { get => Tag; set => Tag = value; }
    public Thickness Margin { get => native.Margin; set => native.Margin = value; }
    public Thickness margin { get => Margin; set => Margin = value; }
    public Thickness Padding
    {
        get => native is Control control ? control.Padding : default;
        set { if (native is Control control) control.Padding = value; }
    }
    public Thickness padding { get => Padding; set => Padding = value; }
    public double Opacity { get => native.Opacity; set => native.Opacity = value; }
    public double opacity { get => Opacity; set => Opacity = value; }
    public string? Tooltip { get => native.ToolTip?.ToString(); set => native.ToolTip = value; }
    public string? tooltip { get => Tooltip; set => Tooltip = value; }
    public bool IsEnabled { get => native.IsEnabled; set => native.IsEnabled = value; }
    public bool isEnabled { get => IsEnabled; set => IsEnabled = value; }
    public bool Visible
    {
        get => native.Visibility == Visibility.Visible;
        set => native.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    public bool visible { get => Visible; set => Visible = value; }
    public object? Value
    {
        get => native switch
        {
            TextBox textBox => textBox.Text,
            CheckBox checkBox => checkBox.IsChecked == true,
            Slider slider => slider.Value,
            ProgressBar progress => progress.Value,
            ComboBox comboBox => comboBox.SelectedItem,
            ListBox listBox => listBox.SelectedItem,
            _ => Text
        };
        set
        {
            switch (native)
            {
                case TextBox textBox:
                    textBox.Text = value?.ToString() ?? string.Empty;
                    break;
                case CheckBox checkBox:
                    checkBox.IsChecked = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;
                case Slider slider:
                    slider.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    break;
                case ProgressBar progress:
                    progress.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    break;
                case ComboBox comboBox:
                    comboBox.SelectedItem = value;
                    break;
                case ListBox listBox:
                    listBox.SelectedItem = value;
                    break;
                default:
                    Text = value?.ToString() ?? string.Empty;
                    break;
            }
        }
    }
    public object? value { get => Value; set => Value = value; }

    public JavaScriptElement? Parent
    {
        get
        {
            var parent = native.Parent as FrameworkElement
                ?? VisualTreeHelper.GetParent(native) as FrameworkElement;
            return parent is null ? null : new JavaScriptElement(parent, runtime);
        }
    }
    public JavaScriptElement? parent => Parent;

    public JavaScriptElement[] Children()
    {
        return EnumerateChildren(native).Select(child => new JavaScriptElement(child, runtime)).ToArray();
    }
    public JavaScriptElement[] children() => Children();

    public JavaScriptElement? Find(string name)
    {
        var found = FindByName(native, name);
        return found is null ? null : new JavaScriptElement(found, runtime);
    }
    public JavaScriptElement? find(string name) => Find(name);
    public JavaScriptElement? Query(string name) => Find(name);
    public JavaScriptElement? query(string name) => Find(name);

    public JavaScriptElement SetMargin(double left, double top, double right, double bottom)
    {
        native.Margin = new Thickness(left, top, right, bottom);
        return this;
    }
    public JavaScriptElement setMargin(double left, double top, double right, double bottom) => SetMargin(left, top, right, bottom);

    public JavaScriptElement SetPadding(double left, double top, double right, double bottom)
    {
        if (native is Control control) control.Padding = new Thickness(left, top, right, bottom);
        return this;
    }
    public JavaScriptElement setPadding(double left, double top, double right, double bottom) => SetPadding(left, top, right, bottom);

    public JavaScriptElement SetSize(double width, double height)
    {
        native.Width = width;
        native.Height = height;
        return this;
    }
    public JavaScriptElement setSize(double width, double height) => SetSize(width, height);

    public JavaScriptElement SetMinSize(double width, double height)
    {
        native.MinWidth = width;
        native.MinHeight = height;
        return this;
    }
    public JavaScriptElement setMinSize(double width, double height) => SetMinSize(width, height);

    public JavaScriptElement SetBackground(string color)
    {
        switch (native)
        {
            case Panel panel:
                panel.Background = ParseBrush(color);
                break;
            case Border border:
                border.Background = ParseBrush(color);
                break;
            case Control control:
                control.Background = ParseBrush(color);
                break;
        }
        return this;
    }
    public JavaScriptElement setBackground(string color) => SetBackground(color);

    public JavaScriptElement SetForeground(string color)
    {
        switch (native)
        {
            case TextBlock textBlock:
                textBlock.Foreground = ParseBrush(color);
                break;
            case Control control:
                control.Foreground = ParseBrush(color);
                break;
        }
        return this;
    }
    public JavaScriptElement setForeground(string color) => SetForeground(color);

    public JavaScriptElement SetBorder(string color, double thickness = 1, double radius = 0)
    {
        if (native is Border border)
        {
            border.BorderBrush = ParseBrush(color);
            border.BorderThickness = new Thickness(thickness);
            border.CornerRadius = new CornerRadius(radius);
        }
        return this;
    }
    public JavaScriptElement setBorder(string color, double thickness = 1, double radius = 0) => SetBorder(color, thickness, radius);

    public JavaScriptElement SetResource(string propertyName, string resourceKey)
    {
        var property = ResolveDependencyProperty(propertyName);
        native.SetResourceReference(property, resourceKey);
        return this;
    }
    public JavaScriptElement setResource(string propertyName, string resourceKey) => SetResource(propertyName, resourceKey);

    public JavaScriptElement SetStyle(string propertyName, object? value)
    {
        switch (NormalizePropertyName(propertyName))
        {
            case "background":
            case "backgroundcolor":
                SetBackgroundValue(value);
                return this;
            case "backgroundbrush":
                SetBackgroundBrushValue(value);
                return this;
            case "backgroundresource":
                return SetResource("background", Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            case "foreground":
            case "foregroundcolor":
            case "color":
            case "textcolor":
                SetForegroundValue(value);
                return this;
            case "foregroundresource":
            case "colorresource":
                return SetResource("foreground", Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            case "bordercolor":
                SetBorderBrush(value);
                return this;
            case "borderthickness":
                SetBorderThickness(ToThickness(value));
                return this;
            case "borderradius":
            case "cornerradius":
                if (native is Border border) border.CornerRadius = new CornerRadius(ToDouble(value));
                return this;
            case "fontfamily":
                SetFontFamily(value);
                return this;
            case "fontsize":
                SetFontSize(ToDouble(value));
                return this;
            case "fontweight":
                SetFontWeight(value);
                return this;
            case "fontstyle":
                SetFontStyle(value);
                return this;
            case "margin":
                Margin = ToThickness(value);
                return this;
            case "padding":
                Padding = ToThickness(value);
                return this;
            case "width":
                Width = ToDouble(value);
                return this;
            case "height":
                Height = ToDouble(value);
                return this;
            case "minwidth":
                MinWidth = ToDouble(value);
                return this;
            case "minheight":
                MinHeight = ToDouble(value);
                return this;
            case "maxwidth":
                native.MaxWidth = ToDouble(value);
                return this;
            case "maxheight":
                native.MaxHeight = ToDouble(value);
                return this;
            case "opacity":
                Opacity = ToDouble(value);
                return this;
            case "visible":
            case "display":
                Visible = ToBoolean(value);
                return this;
            case "visibility":
                native.Visibility = ToVisibility(value);
                return this;
            case "enabled":
            case "isenabled":
                IsEnabled = ToBoolean(value);
                return this;
            case "tooltip":
            case "toolTip":
                Tooltip = Convert.ToString(value, CultureInfo.InvariantCulture);
                return this;
            case "text":
            case "content":
                Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return this;
            case "value":
                Value = value;
                return this;
            case "horizontalalignment":
                native.HorizontalAlignment = ToEnum<HorizontalAlignment>(value);
                return this;
            case "verticalalignment":
                native.VerticalAlignment = ToEnum<VerticalAlignment>(value);
                return this;
            default:
                return Set(propertyName, value);
        }
    }
    public JavaScriptElement setStyle(string propertyName, object? value) => SetStyle(propertyName, value);
    public JavaScriptElement Style(string propertyName, object? value) => SetStyle(propertyName, value);
    public JavaScriptElement style(string propertyName, object? value) => SetStyle(propertyName, value);

    public JavaScriptElement SetStyles(object? styles)
    {
        if (JavaScriptRuntime.TryEnumerateObject(styles, out var scriptProperties))
        {
            foreach (var (propertyName, value) in scriptProperties)
                SetStyle(propertyName, value);
            return this;
        }

        if (styles is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                if (entry.Key is not null)
                    SetStyle(entry.Key.ToString()!, entry.Value);
            return this;
        }

        throw new ArgumentException("样式必须是 JavaScript 对象或字典。", nameof(styles));
    }
    public JavaScriptElement setStyles(object? styles) => SetStyles(styles);
    public JavaScriptElement Styles(object? styles) => SetStyles(styles);
    public JavaScriptElement styles(object? styles) => SetStyles(styles);

    public JavaScriptElement Css(string cssText)
    {
        foreach (var declaration in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0) continue;
            SetStyle(declaration[..separator], declaration[(separator + 1)..].Trim());
        }
        return this;
    }
    public JavaScriptElement css(string cssText) => Css(cssText);

    public JavaScriptElement Append(JavaScriptElement child)
    {
        child.RemoveSelf();
        switch (native)
        {
            case Panel panel:
                panel.Children.Add(child.Native);
                break;
            case Border border:
                border.Child = child.Native;
                break;
            case ScrollViewer scrollViewer:
                scrollViewer.Content = child.Native;
                break;
            case ContentControl contentControl:
                contentControl.Content = child.Native;
                break;
            default:
                throw new InvalidOperationException($"{native.GetType().Name} 不能包含子元素。");
        }
        return this;
    }
    public JavaScriptElement append(JavaScriptElement child) => Append(child);

    public JavaScriptElement Clear()
    {
        switch (native)
        {
            case Panel panel:
                panel.Children.Clear();
                break;
            case Border border:
                border.Child = null;
                break;
            case ScrollViewer scrollViewer:
                scrollViewer.Content = null;
                break;
            case ContentControl contentControl:
                contentControl.Content = null;
                break;
        }
        return this;
    }
    public JavaScriptElement clear() => Clear();

    public JavaScriptElement On(string eventName, object handler)
    {
        if (!runtime.IsCallable(handler))
            throw new ArgumentException("事件处理器必须是 JavaScript 函数。", nameof(handler));

        if (string.Equals(eventName, "click", StringComparison.OrdinalIgnoreCase) && native is Button button)
        {
            button.Click += (_, _) => runtime.InvokeCallback(handler, this);
            return this;
        }

        if (string.Equals(eventName, "click", StringComparison.OrdinalIgnoreCase) && native is UIElement uiElement)
        {
            uiElement.MouseLeftButtonUp += (_, args) => runtime.InvokeCallback(handler, this, args);
            return this;
        }

        if (string.Equals(eventName, "changed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventName, "change", StringComparison.OrdinalIgnoreCase))
        {
            switch (native)
            {
                case TextBox changedTextBox:
                    changedTextBox.TextChanged += (_, _) => runtime.InvokeCallback(handler, this, changedTextBox.Text);
                    return this;
                case CheckBox changedCheckBox:
                    changedCheckBox.Checked += (_, _) => runtime.InvokeCallback(handler, this, true);
                    changedCheckBox.Unchecked += (_, _) => runtime.InvokeCallback(handler, this, false);
                    return this;
                case Slider slider:
                    slider.ValueChanged += (_, _) => runtime.InvokeCallback(handler, this, slider.Value);
                    return this;
                case ComboBox comboBox:
                    comboBox.SelectionChanged += (_, _) => runtime.InvokeCallback(handler, this, comboBox.SelectedItem);
                    return this;
                case ListBox listBox:
                    listBox.SelectionChanged += (_, _) => runtime.InvokeCallback(handler, this, listBox.SelectedItem);
                    return this;
            }
        }

        if (string.Equals(eventName, "textChanged", StringComparison.OrdinalIgnoreCase) && native is TextBox textBox)
        {
            textBox.TextChanged += (_, _) => runtime.InvokeCallback(handler, this, textBox.Text);
            return this;
        }

        if (string.Equals(eventName, "checked", StringComparison.OrdinalIgnoreCase) && native is CheckBox checkBox)
        {
            checkBox.Checked += (_, _) => runtime.InvokeCallback(handler, this, true);
            checkBox.Unchecked += (_, _) => runtime.InvokeCallback(handler, this, false);
            return this;
        }

        throw new NotSupportedException($"{native.GetType().Name} 不支持事件 {eventName}。");
    }
    public JavaScriptElement on(string eventName, object handler) => On(eventName, handler);

    public JavaScriptElement Set(string propertyName, object? value)
    {
        var property = FindProperty(propertyName);
        if (property is null || !property.CanWrite)
            throw new MissingMemberException(native.GetType().FullName, propertyName);
        property.SetValue(native, ConvertForProperty(value, property.PropertyType));
        return this;
    }
    public JavaScriptElement set(string propertyName, object? value) => Set(propertyName, value);

    public JavaScriptElement AddItem(object? item)
    {
        switch (native)
        {
            case ItemsControl itemsControl:
                itemsControl.Items.Add(Unwrap(item));
                break;
            default:
                throw new NotSupportedException($"{native.GetType().Name} 不支持 addItem。");
        }
        return this;
    }
    public JavaScriptElement addItem(object? item) => AddItem(item);

    public JavaScriptElement AddItems(object? items)
    {
        if (items is IEnumerable enumerable && items is not string)
        {
            foreach (var item in enumerable) AddItem(item);
            return this;
        }

        AddItem(items);
        return this;
    }
    public JavaScriptElement addItems(object? items) => AddItems(items);

    public JavaScriptElement Remove(JavaScriptElement child)
    {
        switch (native)
        {
            case Panel panel:
                panel.Children.Remove(child.Native);
                break;
            case ItemsControl itemsControl:
                itemsControl.Items.Remove(child.Native);
                break;
        }
        return this;
    }
    public JavaScriptElement remove(JavaScriptElement child) => Remove(child);

    public JavaScriptElement RemoveSelf()
    {
        switch (native.Parent)
        {
            case Panel panel:
                panel.Children.Remove(native);
                return this;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, native):
                contentControl.Content = null;
                return this;
            case Decorator decorator when ReferenceEquals(decorator.Child, native):
                decorator.Child = null;
                return this;
            case ItemsControl itemsControl:
                itemsControl.Items.Remove(native);
                return this;
        }

        var visualParent = VisualTreeHelper.GetParent(native);
        switch (visualParent)
        {
            case Panel panel:
                panel.Children.Remove(native);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, native):
                contentControl.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, native):
                decorator.Child = null;
                break;
        }
        return this;
    }
    public JavaScriptElement removeSelf() => RemoveSelf();

    public object? Get(string propertyName)
    {
        var property = FindProperty(propertyName);
        if (property is null || !property.CanRead)
            throw new MissingMemberException(native.GetType().FullName, propertyName);
        return property.GetValue(native);
    }
    public object? get(string propertyName) => Get(propertyName);

    public UserControl ToUserControl()
    {
        if (native is UserControl userControl) return userControl;
        return new UserControl { Content = native };
    }

    private static string UpperFirst(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsUpper(value[0])) return value;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private PropertyInfo? FindProperty(string propertyName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        return FindDeclaredProperty(propertyName, flags)
            ?? FindDeclaredProperty(UpperFirst(propertyName), flags);
    }

    private PropertyInfo? FindDeclaredProperty(string propertyName, BindingFlags flags)
    {
        for (var type = native.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperties(flags | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0 && string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal))
                .ThenByDescending(p => p.CanWrite)
                .ThenByDescending(p => p.CanRead)
                .FirstOrDefault();
            if (property is not null) return property;
        }

        return null;
    }

    private DependencyProperty ResolveDependencyProperty(string propertyName)
    {
        return ResolveDependencyProperty(native, propertyName)
            ?? throw new MissingMemberException(native.GetType().FullName, propertyName + "Property");
    }

    private static DependencyProperty? ResolveDependencyProperty(FrameworkElement element, string propertyName)
    {
        var normalized = NormalizePropertyName(propertyName);
        string[] candidates = normalized switch
        {
            "background" or "backgroundcolor" => new[] { "BackgroundProperty", "BackgroundBrushProperty" },
            "backgroundbrush" => new[] { "BackgroundBrushProperty", "BackgroundProperty" },
            "foreground" or "foregroundcolor" or "color" or "textcolor" => new[] { "ForegroundProperty" },
            "bordercolor" or "borderbrush" => new[] { "BorderBrushProperty" },
            "borderthickness" => new[] { "BorderThicknessProperty" },
            "fontsize" => new[] { "FontSizeProperty" },
            "fontfamily" => new[] { "FontFamilyProperty" },
            "fontweight" => new[] { "FontWeightProperty" },
            "fontstyle" => new[] { "FontStyleProperty" },
            "opacity" => new[] { "OpacityProperty" },
            "visibility" or "visible" or "display" => new[] { "VisibilityProperty" },
            _ => new[] { UpperFirst(propertyName) + "Property" }
        };

        foreach (var candidate in candidates)
        {
            for (var type = element.GetType(); type is not null; type = type.BaseType)
            {
                var field = type.GetField(candidate, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (field?.GetValue(null) is DependencyProperty dependencyProperty) return dependencyProperty;
            }
        }

        return null;
    }

    private static Brush ParseBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private void SetBackgroundValue(object? value)
    {
        var brush = ToBrush(value);
        switch (native)
        {
            case Panel panel:
                panel.Background = brush;
                return;
            case Border border:
                border.Background = brush;
                return;
            case Control control:
                control.Background = brush;
                return;
        }

        var backgroundProperty = ResolveDependencyProperty(native, "Background");
        if (backgroundProperty is not null)
        {
            native.SetValue(backgroundProperty, brush);
            return;
        }

        TrySetExactDependencyProperty("BackgroundBrush", brush);
    }

    private void SetBackgroundBrushValue(object? value)
    {
        if (!TrySetExactDependencyProperty("BackgroundBrush", ToBrush(value)))
            SetBackgroundValue(value);
    }

    private bool TrySetExactDependencyProperty(string propertyName, object? value)
    {
        var property = FindExactDependencyProperty(native, propertyName + "Property");
        if (property is null) return false;
        native.SetValue(property, ConvertForProperty(value, property.PropertyType));
        return true;
    }

    private static DependencyProperty? FindExactDependencyProperty(FrameworkElement element, string fieldName)
    {
        for (var type = element.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty dependencyProperty) return dependencyProperty;
        }

        return null;
    }

    private void SetForegroundValue(object? value)
    {
        var brush = ToBrush(value);
        switch (native)
        {
            case TextBlock textBlock:
                textBlock.Foreground = brush;
                break;
            case Control control:
                control.Foreground = brush;
                break;
            default:
                if (ResolveDependencyProperty(native, "Foreground") is { } property)
                    native.SetValue(property, brush);
                break;
        }
    }

    private void SetBorderBrush(object? value)
    {
        var brush = ToBrush(value);
        switch (native)
        {
            case Border border:
                border.BorderBrush = brush;
                break;
            case Control control:
                control.BorderBrush = brush;
                break;
        }
    }

    private void SetBorderThickness(Thickness thickness)
    {
        switch (native)
        {
            case Border border:
                border.BorderThickness = thickness;
                break;
            case Control control:
                control.BorderThickness = thickness;
                break;
        }
    }

    private void SetFontFamily(object? value)
    {
        var fontFamily = value is FontFamily family ? family : new FontFamily(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "Segoe UI");
        switch (native)
        {
            case TextBlock textBlock:
                textBlock.FontFamily = fontFamily;
                break;
            case Control control:
                control.FontFamily = fontFamily;
                break;
        }
    }

    private void SetFontSize(double value)
    {
        switch (native)
        {
            case TextBlock textBlock:
                textBlock.FontSize = value;
                break;
            case Control control:
                control.FontSize = value;
                break;
        }
    }

    private void SetFontWeight(object? value)
    {
        var fontWeight = value is FontWeight weight ? weight : GetStaticValue<FontWeight>(typeof(FontWeights), value, FontWeights.Normal);
        switch (native)
        {
            case TextBlock textBlock:
                textBlock.FontWeight = fontWeight;
                break;
            case Control control:
                control.FontWeight = fontWeight;
                break;
        }
    }

    private void SetFontStyle(object? value)
    {
        var fontStyle = value is FontStyle style ? style : GetStaticValue<FontStyle>(typeof(FontStyles), value, FontStyles.Normal);
        switch (native)
        {
            case TextBlock textBlock:
                textBlock.FontStyle = fontStyle;
                break;
            case Control control:
                control.FontStyle = fontStyle;
                break;
        }
    }

    internal static object? ConvertForProperty(object? value, Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        value = JavaScriptRuntime.ToObject(Unwrap(value));
        if (value is null) return null;
        if (targetType.IsInstanceOfType(value)) return value;
        if (targetType == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(double)) return ToDouble(value);
        if (targetType == typeof(int)) return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(bool)) return ToBoolean(value);
        if (targetType == typeof(Thickness)) return ToThickness(value);
        if (targetType == typeof(Brush)) return ToBrush(value);
        if (targetType == typeof(SolidColorBrush)) return ToBrush(value);
        if (targetType == typeof(FontFamily)) return new FontFamily(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "Segoe UI");
        if (targetType == typeof(FontWeight)) return GetStaticValue<FontWeight>(typeof(FontWeights), value, FontWeights.Normal);
        if (targetType == typeof(FontStyle)) return GetStaticValue<FontStyle>(typeof(FontStyles), value, FontStyles.Normal);
        if (targetType.IsEnum) return Enum.Parse(targetType, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, true);
        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static Brush ToBrush(object? value)
    {
        return value switch
        {
            Brush brush => brush,
            _ => ParseBrush(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "Transparent")
        };
    }

    private static double ToDouble(object? value)
    {
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static bool ToBoolean(object? value)
    {
        if (value is bool boolValue) return boolValue;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "0", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "collapsed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static Thickness ToThickness(object? value)
    {
        if (value is Thickness thickness) return thickness;
        if (JavaScriptRuntime.TryEnumerateObject(value, out var properties))
        {
            var values = properties.ToDictionary(static p => p.Name, static p => p.Value, StringComparer.OrdinalIgnoreCase);
            var left = GetScriptNumber(values, "left", 0);
            var top = GetScriptNumber(values, "top", left);
            var right = GetScriptNumber(values, "right", left);
            var bottom = GetScriptNumber(values, "bottom", top);
            return new Thickness(left, top, right, bottom);
        }
        if (value is IConvertible && value is not string) return new Thickness(ToDouble(value));

        var parts = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0")
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Convert.ToDouble(part, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length switch
        {
            0 => default,
            1 => new Thickness(parts[0]),
            2 => new Thickness(parts[0], parts[1], parts[0], parts[1]),
            _ => new Thickness(parts[0], parts[1], parts[2], parts.Length > 3 ? parts[3] : parts[1])
        };
    }

    private static double GetScriptNumber(IReadOnlyDictionary<string, object?> properties, string propertyName, double defaultValue)
    {
        return properties.TryGetValue(propertyName, out var value)
            ? ToDouble(value)
            : defaultValue;
    }

    private static Visibility ToVisibility(object? value)
    {
        if (value is Visibility visibility) return visibility;
        if (value is bool boolValue) return boolValue ? Visibility.Visible : Visibility.Collapsed;
        return ToEnum<Visibility>(value);
    }

    private static T ToEnum<T>(object? value) where T : struct
    {
        if (value is T enumValue) return enumValue;
        return Enum.Parse<T>(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, true);
    }

    private static T GetStaticValue<T>(Type type, object? value, T fallback)
    {
        var name = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;
        return type.GetProperty(name, flags)?.GetValue(null) is T propertyValue ? propertyValue : fallback;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        return new string(propertyName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        return FindByName(root, name, []);
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return null;

        if (root is FrameworkElement frameworkElement)
        {
            if (string.Equals(frameworkElement.Name, name, StringComparison.OrdinalIgnoreCase)) return frameworkElement;
            if (frameworkElement.FindName(name) is FrameworkElement namedElement) return namedElement;
        }

        foreach (var child in EnumerateChildren(root, visited))
        {
            var found = FindByName(child, name, visited);
            if (found is not null) return found;
        }

        return null;
    }

    private static IEnumerable<FrameworkElement> EnumerateChildren(DependencyObject root, HashSet<DependencyObject>? visited = null)
    {
        var yielded = new HashSet<DependencyObject>();
        var visualCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < visualCount; i++)
            if (VisualTreeHelper.GetChild(root, i) is FrameworkElement visualChild &&
                yielded.Add(visualChild) &&
                (visited is null || !visited.Contains(visualChild)))
                yield return visualChild;

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root))
            if (logicalChild is FrameworkElement frameworkElement &&
                yielded.Add(frameworkElement) &&
                (visited is null || !visited.Contains(frameworkElement)))
                yield return frameworkElement;
    }

    internal static object? Unwrap(object? value)
    {
        return value switch
        {
            JavaScriptElement element => element.Native,
            JavaScriptRawObject raw => raw.Target,
            _ => value
        };
    }
}