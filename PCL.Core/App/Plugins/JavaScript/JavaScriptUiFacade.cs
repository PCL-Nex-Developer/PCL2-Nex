using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.JavaScript;

public sealed class JavaScriptUiFacade(JavaScriptPluginContext context, JavaScriptRuntime runtime)
{
    public JavaScriptElement VStack() => new(new StackPanel { Orientation = Orientation.Vertical }, runtime);
    public JavaScriptElement vstack() => VStack();
    public JavaScriptElement HStack() => new(new StackPanel { Orientation = Orientation.Horizontal }, runtime);
    public JavaScriptElement hstack() => HStack();
    public JavaScriptElement Grid() => new(new Grid(), runtime);
    public JavaScriptElement grid() => Grid();
    public JavaScriptElement Wrap() => new(new WrapPanel(), runtime);
    public JavaScriptElement wrap() => Wrap();
    public JavaScriptElement Border(JavaScriptElement? child = null)
    {
        var border = new Border();
        if (child is not null) border.Child = child.Native;
        return new JavaScriptElement(border, runtime);
    }
    public JavaScriptElement border(JavaScriptElement? child = null) => Border(child);
    public JavaScriptElement Text(string text = "") => new(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, runtime);
    public JavaScriptElement text(string text = "") => Text(text);
    public JavaScriptElement Heading(string text = "")
    {
        var element = Text(text);
        element.Set("FontSize", 18.0);
        element.Set("FontWeight", FontWeights.Bold);
        element.SetMargin(0, 0, 0, 10);
        return element;
    }
    public JavaScriptElement heading(string text = "") => Heading(text);
    public JavaScriptElement Button(string text = "") => new(new Button { Content = text }, runtime);
    public JavaScriptElement button(string text = "") => Button(text);
    public JavaScriptElement Input(string text = "") => new(new TextBox { Text = text }, runtime);
    public JavaScriptElement input(string text = "") => Input(text);
    public JavaScriptElement TextArea(string text = "") => new(new TextBox
    {
        Text = text,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 80
    }, runtime);
    public JavaScriptElement textarea(string text = "") => TextArea(text);
    public JavaScriptElement CheckBox(string text = "") => new(new CheckBox { Content = text }, runtime);
    public JavaScriptElement checkbox(string text = "") => CheckBox(text);
    public JavaScriptElement ComboBox() => new(new ComboBox { MinWidth = 120 }, runtime);
    public JavaScriptElement comboBox() => ComboBox();
    public JavaScriptElement ListBox() => new(new ListBox { MinHeight = 90 }, runtime);
    public JavaScriptElement listBox() => ListBox();
    public JavaScriptElement Slider(double minimum = 0, double maximum = 100, double value = 0)
        => new(new Slider { Minimum = minimum, Maximum = maximum, Value = value, MinWidth = 120 }, runtime);
    public JavaScriptElement slider(double minimum = 0, double maximum = 100, double value = 0) => Slider(minimum, maximum, value);
    public JavaScriptElement Progress(double minimum = 0, double maximum = 100, double value = 0)
        => new(new ProgressBar { Minimum = minimum, Maximum = maximum, Value = value, MinWidth = 120, Height = 6 }, runtime);
    public JavaScriptElement progress(double minimum = 0, double maximum = 100, double value = 0) => Progress(minimum, maximum, value);
    public JavaScriptElement Separator() => new(new Separator(), runtime);
    public JavaScriptElement separator() => Separator();
    public JavaScriptElement Scroll(JavaScriptElement? child = null)
    {
        var scroll = new ScrollViewer();
        if (child is not null) scroll.Content = child.Native;
        return new JavaScriptElement(scroll, runtime);
    }
    public JavaScriptElement scroll(JavaScriptElement? child = null) => Scroll(child);

    public JavaScriptElement Create(string typeName)
    {
        var type = ResolveElementType(typeName);
        if (Activator.CreateInstance(type) is not FrameworkElement element)
            throw new InvalidOperationException($"{type.FullName} 不是 WPF FrameworkElement。");
        return new JavaScriptElement(element, runtime);
    }
    public JavaScriptElement create(string typeName) => Create(typeName);

    public JavaScriptElement Native(FrameworkElement element) => new(element, runtime);
    public JavaScriptElement native(FrameworkElement element) => Native(element);

    public JavaScriptElement? Body()
    {
        return System.Windows.Application.Current?.MainWindow is FrameworkElement window
            ? new JavaScriptElement(window, runtime)
            : null;
    }
    public JavaScriptElement? body() => Body();
    public JavaScriptElement? Root() => Body();
    public JavaScriptElement? root() => Body();

    public JavaScriptElement? Get(string name)
    {
        if (context.Host.Ui is { } ui && !ui.CheckAccess())
            return ui.InvokeOnUi(() => GetCore(name));
        return GetCore(name);
    }
    public JavaScriptElement? get(string name) => Get(name);
    public JavaScriptElement? Query(string name) => Get(name);
    public JavaScriptElement? query(string name) => Get(name);
    public JavaScriptElement? ByName(string name) => Get(name);
    public JavaScriptElement? byName(string name) => Get(name);

    private JavaScriptElement? GetCore(string name)
    {
        if (System.Windows.Application.Current?.MainWindow is not FrameworkElement window) return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        if (window.GetType().GetField(name, flags)?.GetValue(window) is FrameworkElement fieldElement)
            return new JavaScriptElement(fieldElement, runtime);
        if (window.GetType().GetProperty(name, flags)?.GetValue(window) is FrameworkElement propertyElement)
            return new JavaScriptElement(propertyElement, runtime);
        if (window.FindName(name) is FrameworkElement namedElement)
            return new JavaScriptElement(namedElement, runtime);
        return null;
    }

    public JavaScriptElement? Search(string name) => Body()?.Find(name);
    public JavaScriptElement? search(string name) => Search(name);

    public IDisposable When(string name, object action, int timeoutMs = 10000, int intervalMs = 120)
    {
        if (!runtime.IsCallable(action))
            throw new ArgumentException("when 回调必须是 JavaScript 函数或 .NET 委托。", nameof(action));

        var ui = context.Host.Ui;
        JavaScriptElement? found = null;
        void InvokeCallback(JavaScriptElement element)
        {
            try
            {
                runtime.InvokeCallback(action, element);
            }
            catch (Exception ex)
            {
                context.Core.GetLogger("js-dom").Error($"执行宿主元素回调失败: {name}", ex);
            }
        }

        void CheckNow()
        {
            found = Search(name) ?? Get(name);
            if (found is not null) InvokeCallback(found);
        }

        if (ui is { } hostUi && !hostUi.CheckAccess()) hostUi.InvokeOnUi(CheckNow);
        else CheckNow();
        if (found is not null) return DisposableAction.Empty;

        var app = System.Windows.Application.Current;
        var dispatcher = app?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        DispatcherTimer? timer = null;
        var startedAt = DateTime.UtcNow;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(16, intervalMs))
        };
        timer.Tick += (_, _) =>
        {
            var element = Search(name) ?? Get(name);
            if (element is not null)
            {
                timer.Stop();
                InvokeCallback(element);
                return;
            }

            if ((DateTime.UtcNow - startedAt).TotalMilliseconds >= timeoutMs)
            {
                timer.Stop();
                context.Core.GetLogger("js-dom").Warn($"等待宿主元素超时: {name}");
            }
        };
        timer.Start();

        var registration = new DisposableAction(() => timer.Stop());
        context.Track(registration);
        return registration;
    }
    public IDisposable when(string name, object action, int timeoutMs = 10000, int intervalMs = 120) => When(name, action, timeoutMs, intervalMs);

    public void Run(object action)
    {
        void Invoke()
        {
            runtime.InvokeCallback(action);
        }

        if (context.Host.Ui is { } ui) ui.InvokeOnUi(Invoke);
        else Invoke();
    }
    public void run(object action) => Run(action);

    public IDisposable RegisterPluginPage(string id, string title, string icon, int order, object factory)
    {
        var ui = context.Host.Ui ?? throw new InvalidOperationException("插件未声明 UI 能力或宿主 UI 未就绪。");
        PluginPanelFactory panelFactory = () => ui.InvokeOnUi(() => ToUserControl(InvokeFactory(factory), "页面"));

        var registration = ui.ContributePluginPanel(new PluginPanelDescriptor
        {
            Id = id,
            Title = title,
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon,
            Order = order,
            Factory = panelFactory
        });
        context.Track(registration);
        return registration;
    }
    public IDisposable registerPluginPage(string id, string title, string icon, int order, object factory)
        => RegisterPluginPage(id, title, icon, order, factory);

    public IDisposable RegisterNavPage(string id, string title, string icon, int order, object factory)
    {
        var ui = context.Host.Ui ?? throw new InvalidOperationException("插件未声明 UI 能力或宿主 UI 未就绪。");
        PluginPanelFactory panelFactory = () => ui.InvokeOnUi(() => ToUserControl(InvokeFactory(factory), "页面"));
        var registration = ui.ContributeNavigationPage(new NavigationPageDescriptor
        {
            Id = id,
            Title = title,
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon,
            Order = order,
            Factory = panelFactory
        });
        context.Track(registration);
        return registration;
    }
    public IDisposable registerNavPage(string id, string title, string icon, int order, object factory)
        => RegisterNavPage(id, title, icon, order, factory);
    public IDisposable RegisterNavigationPage(string id, string title, string icon, int order, object factory)
        => RegisterNavPage(id, title, icon, order, factory);
    public IDisposable registerNavigationPage(string id, string title, string icon, int order, object factory)
        => RegisterNavPage(id, title, icon, order, factory);

    public IDisposable RegisterToolsPanel(string id, string title, int order, object factory)
        => RegisterToolsPanel(id, title, null, null, order, factory);

    public IDisposable RegisterToolsPanel(string id, string title, string? icon, int order, object factory)
        => RegisterToolsPanel(id, title, null, icon, order, factory);

    public IDisposable RegisterToolsPanel(string id, string title, string? group, string? icon, int order, object factory)
    {
        var ui = context.Host.Ui ?? throw new InvalidOperationException("插件未声明 UI 能力或宿主 UI 未就绪。");
        ToolsPanelFactory panelFactory = () => ui.InvokeOnUi(() => ToFrameworkElement(InvokeFactory(factory), "面板"));
        var registration = ui.ContributeToolsPanel(new ToolsPanelDescriptor
        {
            Id = id,
            Title = title,
            Group = string.IsNullOrWhiteSpace(group) ? null : group,
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon,
            Order = order,
            Factory = panelFactory
        });
        context.Track(registration);
        return registration;
    }
    public IDisposable registerToolsPanel(string id, string title, int order, object factory)
        => RegisterToolsPanel(id, title, order, factory);
    public IDisposable registerToolsPanel(string id, string title, string? icon, int order, object factory)
        => RegisterToolsPanel(id, title, icon, order, factory);
    public IDisposable registerToolsPanel(string id, string title, string? group, string? icon, int order, object factory)
        => RegisterToolsPanel(id, title, group, icon, order, factory);

    public IDisposable RegisterSettingsPanel(string id, string title, int order, object factory)
        => RegisterSettingsPanel(id, title, null, null, order, factory);

    public IDisposable RegisterSettingsPanel(string id, string title, string? icon, int order, object factory)
        => RegisterSettingsPanel(id, title, null, icon, order, factory);

    public IDisposable RegisterSettingsPanel(string id, string title, string? group, string? icon, int order, object factory)
    {
        var ui = context.Host.Ui ?? throw new InvalidOperationException("插件未声明 UI 能力或宿主 UI 未就绪。");
        SettingsPanelFactory panelFactory = () => ui.InvokeOnUi(() => ToFrameworkElement(InvokeFactory(factory), "面板"));
        var registration = ui.ContributeSettingsPanel(new SettingsPanelDescriptor
        {
            Id = id,
            Title = title,
            Group = string.IsNullOrWhiteSpace(group) ? null : group,
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon,
            Order = order,
            Factory = panelFactory
        });
        context.Track(registration);
        return registration;
    }
    public IDisposable registerSettingsPanel(string id, string title, int order, object factory)
        => RegisterSettingsPanel(id, title, order, factory);
    public IDisposable registerSettingsPanel(string id, string title, string? icon, int order, object factory)
        => RegisterSettingsPanel(id, title, icon, order, factory);
    public IDisposable registerSettingsPanel(string id, string title, string? group, string? icon, int order, object factory)
        => RegisterSettingsPanel(id, title, group, icon, order, factory);

    public IDisposable RegisterAboutLegalLink(string id, string title, string url, int order = 100, bool isHighlighted = false)
    {
        var ui = context.Host.Ui ?? throw new InvalidOperationException("插件未声明 UI 能力或宿主 UI 未就绪。");
        var registration = ui.ContributeAboutLegalLink(new AboutLegalLinkDescriptor
        {
            Id = id,
            Title = title,
            Url = url,
            Order = order,
            IsHighlighted = isHighlighted
        });
        context.Track(registration);
        return registration;
    }
    public IDisposable registerAboutLegalLink(string id, string title, string url, int order = 100, bool isHighlighted = false)
        => RegisterAboutLegalLink(id, title, url, order, isHighlighted);

    private object? InvokeFactory(object factory)
    {
        if (runtime.IsCallable(factory)) return runtime.InvokeCallback(factory);
        return factory;
    }

    private UserControl ToUserControl(object? result, string kind)
        => ToFrameworkElement(result, kind) switch
        {
            UserControl userControl => userControl,
            FrameworkElement frameworkElement => new JavaScriptElement(frameworkElement, runtime).ToUserControl(),
            _ => throw new InvalidOperationException($"JS {kind}工厂必须返回 JavaScriptElement、FrameworkElement 或 UserControl。")
        };

    private FrameworkElement ToFrameworkElement(object? result, string kind)
    {
        result = JavaScriptRuntime.ToObject(result);
        if (result is JavaScriptElement element) return element.ToUserControl();
        if (result is FrameworkElement frameworkElement) return frameworkElement;
        throw new InvalidOperationException($"JS {kind}工厂必须返回 JavaScriptElement、FrameworkElement 或 UserControl。");
    }

    private static Type ResolveElementType(string typeName)
    {
        var candidates = typeName.Contains('.')
            ? new[] { typeName }
            : new[]
            {
                "PCL." + typeName,
                "System.Windows.Controls." + typeName,
                "System.Windows.Shapes." + typeName,
                "System.Windows." + typeName
            };

        foreach (var candidate in candidates)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(candidate, throwOnError: false, ignoreCase: false);
                if (type is not null) return type;
            }
        }

        throw new TypeLoadException($"无法找到 WPF 控件类型: {typeName}");
    }

    private sealed class DisposableAction(Action action) : IDisposable
    {
        public static readonly IDisposable Empty = new DisposableAction(static () => { });
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            action();
        }
    }
}