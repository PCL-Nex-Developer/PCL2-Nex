using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins.JavaScript;
using PCL.Plugin.Abstractions;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class JavaScriptDotNetFacadeTest
{
    [TestMethod]
    public void VbEval_ShouldExplainDllReferenceReplacement()
    {
        using var engine = new JavaScriptRuntime();
        var context = new JavaScriptPluginContext(new FakePluginContext(), "", engine);

        NotSupportedException? exception = null;
        try
        {
            context.vb.eval("Return 1");
        }
        catch (NotSupportedException ex)
        {
            exception = ex;
        }

        Assert.IsNotNull(exception);
        StringAssert.Contains(exception.Message, "pcl.dotnet.loadAssembly");
    }

    [TestMethod]
    public void DotNetLoadAssembly_ShouldResolveTypesFromReferencedDll()
    {
        var pluginDirectory = Path.Combine(Path.GetTempPath(), "pcl-js-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            var sourceAssembly = Path.Combine(AppContext.BaseDirectory, "YamlDotNet.dll");
            Assert.IsTrue(File.Exists(sourceAssembly), sourceAssembly);
            File.Copy(sourceAssembly, Path.Combine(pluginDirectory, "YamlDotNet.dll"));

            using var engine = new JavaScriptRuntime();
            var context = new JavaScriptPluginContext(new FakePluginContext(), pluginDirectory, engine);
            engine.SetValue("pcl", context);

            var result = engine.Evaluate("""
pcl.dotnet.loadAssembly('YamlDotNet.dll');
pcl.dotnet.type('YamlDotNet.Serialization.Serializer').FullName;
""", "dotnet-dll-reference.js");

            Assert.AreEqual("YamlDotNet.Serialization.Serializer", result?.ToString());
        }
        finally
        {
            if (Directory.Exists(pluginDirectory)) Directory.Delete(pluginDirectory, true);
        }
    }

    [TestMethod]
    public void JintRuntime_ShouldSupportExamplePluginHostApis()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var runtime = new JavaScriptRuntime();
                var context = new JavaScriptPluginContext(new FakePluginContext(), AppContext.BaseDirectory, runtime);
                runtime.SetValue("pcl", context);
                runtime.SetType("StackPanel", typeof(StackPanel));
                runtime.SetType("TextBlock", typeof(TextBlock));
                runtime.SetType("Button", typeof(Button));
                runtime.SetType("Thickness", typeof(Thickness));
                runtime.SetType("FontWeights", typeof(FontWeights));
                runtime.SetType("TextWrapping", typeof(TextWrapping));

                EvalStep("new StackPanel", "const root = new StackPanel();");
                EvalStep("new TextBlock", "const title = new TextBlock();");
                EvalStep("set text", "title.Text = 'JS 原生 WPF 写法';");
                EvalStep("set font", "title.FontWeight = FontWeights.Bold;");
                EvalStep("new thickness", "title.Margin = new Thickness(0, 0, 0, 10);");
                EvalStep("children add", "root.Children.Add(title);");
                EvalStep("native root", "const wrapped = pcl.ui.native(root);");
                EvalStep("dotnet new text", "const dotnetText = pcl.dotnet.newObject('System.Windows.Controls.TextBlock');");
                EvalStep("dotnet static", "dotnetText.TextWrapping = pcl.dotnet.staticMember('System.Windows.TextWrapping', 'Wrap');");
                EvalStep("append dotnet", "wrapped.append(pcl.ui.native(dotnetText));");
                EvalStep("new button", "const button = new Button();");
                EvalStep("bind click", "let clicked = false; pcl.ui.native(button).on('click', function () { clicked = true; });");
                EvalStep("raise click", "button.RaiseEvent(pcl.dotnet.newObject('System.Windows.RoutedEventArgs', Button.ClickEvent));");
                EvalStep("load dll", "pcl.dotnet.loadAssembly('PCL.Plugin.Abstractions.dll');");
                EvalStep("dll object", "const manifest = pcl.dotnet.newObject('PCL.Plugin.Abstractions.PluginManifest'); manifest.Name = 'DLL OK';");
                EvalStep("append dll", "const dllText = new TextBlock(); dllText.Text = manifest.Name; wrapped.append(pcl.ui.native(dllText));");

                var result = EvalStep("summary", "root.Children.Count + ':' + clicked + ':' + manifest.Name;");

                Assert.AreEqual("3:true:DLL OK", result?.ToString());

                object? EvalStep(string name, string source)
                {
                    try
                    {
                        return runtime.Evaluate(source, "jint-example-plugin-smoke.js");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(name, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
    }

    [TestMethod]
    public void JintRuntime_ShouldRenderRegisteredPluginUiFactories()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var runtime = new JavaScriptRuntime();
                var ui = new FakeUiApi();
                var context = new JavaScriptPluginContext(new FakePluginContext(ui), AppContext.BaseDirectory, runtime);
                runtime.SetValue("pcl", context);
                runtime.SetType("StackPanel", typeof(StackPanel));
                runtime.SetType("TextBlock", typeof(TextBlock));

                runtime.Execute("""
pcl.ui.registerNavPage('nav', 'Nav', 'lucide/puzzle', 1, function () {
    return pcl.ui.vstack().append(pcl.ui.text('nav ok'));
});
pcl.ui.registerToolsPanel('native-tools', 'Native Tools', 1, function () {
    const root = new StackPanel();
    const text = new TextBlock();
    text.Text = 'native ok';
    root.Children.Add(text);
    return pcl.ui.native(root);
});
""", "jint-ui-factory-smoke.js");

                Assert.IsNotNull(ui.NavigationPage?.Factory());
                Assert.IsNotNull(ui.ToolsPanel?.Factory());
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
    }

    private sealed class FakePluginContext : IPluginContext
    {
        private readonly IPluginHost _host;

        public FakePluginContext(IPluginUiApi? ui = null)
        {
            _host = new FakePluginHost(ui);
        }

        public PluginManifest Manifest { get; } = new()
        {
            Id = "test.vb",
            Name = "VB Test",
            Version = new Version(1, 0, 0, 0),
            Author = "Test",
            MinApiVersion = new Version(1, 0, 0, 0)
        };

        public string DataDirectory => Environment.CurrentDirectory;
        public IPluginHost Host => _host;
        public CancellationToken HostStopping => CancellationToken.None;
    }

    private sealed class FakePluginHost(IPluginUiApi? ui = null) : IPluginHost
    {
        public IPluginCoreApi Core { get; } = new FakeCoreApi();
        public IPluginConfigApi Config { get; } = new FakeConfigApi();
        public IPluginEventBusApi Events { get; } = new FakeEventBusApi();
        public IPluginUiApi? Ui { get; } = ui;
        public IInstanceInfoProvider? Instances => null;
        public ICliCommandRegistrar? Commands => null;
        public IUriActionRegistrar? UriActions => null;
        public IPluginExtensionApi? Extensions => null;
        public object? GetOptionalService(string serviceId) => null;
    }

    private sealed class FakeUiApi : IPluginUiApi
    {
        public ToolsPanelDescriptor? ToolsPanel { get; private set; }
        public PluginPanelDescriptor? PluginPanel { get; private set; }
        public NavigationPageDescriptor? NavigationPage { get; private set; }
        public SettingsPanelDescriptor? SettingsPanel { get; private set; }
        public AboutLegalLinkDescriptor? AboutLegalLink { get; private set; }

        public IDisposable ContributeSettingsPanel(SettingsPanelDescriptor descriptor)
        {
            SettingsPanel = descriptor;
            return new EmptyDisposable();
        }

        public IDisposable ContributeToolsPanel(ToolsPanelDescriptor descriptor)
        {
            ToolsPanel = descriptor;
            return new EmptyDisposable();
        }

        public IDisposable ContributePluginPanel(PluginPanelDescriptor descriptor)
        {
            PluginPanel = descriptor;
            return new EmptyDisposable();
        }

        public IDisposable ContributeNavigationPage(NavigationPageDescriptor descriptor)
        {
            NavigationPage = descriptor;
            return new EmptyDisposable();
        }

        public IDisposable ContributeAboutLegalLink(AboutLegalLinkDescriptor descriptor)
        {
            AboutLegalLink = descriptor;
            return new EmptyDisposable();
        }

        public void InvokeOnUi(Action action) => action();
        public T InvokeOnUi<T>(Func<T> action) => action();
        public bool CheckAccess() => true;
    }

    private sealed class FakeCoreApi : IPluginCoreApi
    {
        public IPluginLogger GetLogger(string category) => new FakeLogger();
        public void Hint(string message, PluginHintType type = PluginHintType.Info) { }
        public string CurrentLanguage => "zh-CN";
        public string Localize(string key, string? fallback = null) => fallback ?? key;
        public string HostVersion => "test";
    }

    private sealed class FakeLogger : IPluginLogger
    {
        public void Trace(string message, Exception? exception = null) { }
        public void Debug(string message, Exception? exception = null) { }
        public void Info(string message, Exception? exception = null) { }
        public void Warn(string message, Exception? exception = null) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class FakeConfigApi : IPluginConfigApi
    {
        private readonly Dictionary<string, object> _values = [];
        public string GetString(string key, string defaultValue = "") => _values.TryGetValue(key, out var value) ? value.ToString() ?? defaultValue : defaultValue;
        public int GetInt(string key, int defaultValue = 0) => _values.TryGetValue(key, out var value) ? Convert.ToInt32(value) : defaultValue;
        public bool GetBool(string key, bool defaultValue = false) => _values.TryGetValue(key, out var value) ? Convert.ToBoolean(value) : defaultValue;
        public double GetDouble(string key, double defaultValue = 0) => _values.TryGetValue(key, out var value) ? Convert.ToDouble(value) : defaultValue;
        public void Set(string key, string value) => _values[key] = value;
        public void Set(string key, int value) => _values[key] = value;
        public void Set(string key, bool value) => _values[key] = value;
        public void Set(string key, double value) => _values[key] = value;
        public bool Contains(string key) => _values.ContainsKey(key);
        public void Remove(string key) => _values.Remove(key);
        public IEnumerable<string> Keys() => _values.Keys;
    }

    private sealed class FakeEventBusApi : IPluginEventBusApi
    {
        public IDisposable Subscribe<TEvent>(string channel, Func<TEvent, Task> handler) where TEvent : PluginEvent => new EmptyDisposable();
        public IDisposable Subscribe<TEvent>(string channel, Action<TEvent> handler) where TEvent : PluginEvent => new EmptyDisposable();
        public Task PublishAsync<TEvent>(string channel, TEvent eventData) where TEvent : PluginEvent => Task.CompletedTask;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}