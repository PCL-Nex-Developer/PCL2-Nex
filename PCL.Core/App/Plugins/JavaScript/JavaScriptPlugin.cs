using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PCL.Plugin.Abstractions;

namespace PCL.Core.App.Plugins.JavaScript;

internal sealed class JavaScriptPlugin(PluginPackageManifest packageManifest, string pluginDirectory) : IPclPlugin, IDisposable
{
    private JavaScriptRuntime? _runtime;
    private JavaScriptPluginContext? _scriptContext;

    public Task LoadAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        var entryPath = Path.Combine(pluginDirectory, packageManifest.EntryScript.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(entryPath))
            throw new FileNotFoundException($"JavaScript 插件入口脚本不存在: {packageManifest.EntryScript}", entryPath);

        _runtime = new JavaScriptRuntime();
        _scriptContext = new JavaScriptPluginContext(context, pluginDirectory, _runtime);

        _runtime.SetValue("pcl", _scriptContext);
        _runtime.SetValue("ctx", _scriptContext);
        _runtime.SetValue("host", _scriptContext.DotNet);
        _runtime.SetValue("dotnet", _scriptContext.DotNet);
        _runtime.SetValue("clr", _scriptContext.DotNet);
        RegisterHostTypes(_runtime);
        _runtime.SetValue("load", new Func<string, string>(ResolveScript));
        _runtime.SetValue("require", new Func<string, object?>(Require));

        var source = File.ReadAllText(entryPath);
        _runtime.Execute(source, entryPath);
        InvokeIfFunction("load", _scriptContext);
        return Task.CompletedTask;

        string ResolveScript(string relativePath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(pluginDirectory) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("脚本只能加载插件目录内的文件。");
            return File.ReadAllText(fullPath);
        }

        object? Require(string relativePath)
        {
            var script = ResolveScript(relativePath);
            return _runtime.Evaluate(script, relativePath);
        }
    }

    public Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        try { InvokeIfFunction("unload"); }
        finally
        {
            _scriptContext?.Dispose();
            _scriptContext = null;
            _runtime?.Dispose();
            _runtime = null;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _scriptContext?.Dispose();
        _runtime?.Dispose();
    }

    private void InvokeIfFunction(string name, params object?[] args)
    {
        if (_runtime is null || !_runtime.HasFunction(name)) return;
        _runtime.InvokeFunction(name, args);
    }

    private static void RegisterHostTypes(JavaScriptRuntime runtime)
    {
        runtime.SetType("Console", typeof(Console));
        runtime.SetType("Application", typeof(System.Windows.Application));
        runtime.SetType("Window", typeof(System.Windows.Window));
        runtime.SetType("UserControl", typeof(System.Windows.Controls.UserControl));
        runtime.SetType("Border", typeof(System.Windows.Controls.Border));
        runtime.SetType("Canvas", typeof(System.Windows.Controls.Canvas));
        runtime.SetType("ComboBox", typeof(System.Windows.Controls.ComboBox));
        runtime.SetType("DockPanel", typeof(System.Windows.Controls.DockPanel));
        runtime.SetType("StackPanel", typeof(System.Windows.Controls.StackPanel));
        runtime.SetType("Grid", typeof(System.Windows.Controls.Grid));
        runtime.SetType("ListBox", typeof(System.Windows.Controls.ListBox));
        runtime.SetType("ProgressBar", typeof(System.Windows.Controls.ProgressBar));
        runtime.SetType("Separator", typeof(System.Windows.Controls.Separator));
        runtime.SetType("Slider", typeof(System.Windows.Controls.Slider));
        runtime.SetType("TextBlock", typeof(System.Windows.Controls.TextBlock));
        runtime.SetType("Button", typeof(System.Windows.Controls.Button));
        runtime.SetType("TextBox", typeof(System.Windows.Controls.TextBox));
        runtime.SetType("CheckBox", typeof(System.Windows.Controls.CheckBox));
        runtime.SetType("ScrollViewer", typeof(System.Windows.Controls.ScrollViewer));
        runtime.SetType("WrapPanel", typeof(System.Windows.Controls.WrapPanel));
        runtime.SetType("CornerRadius", typeof(System.Windows.CornerRadius));
        runtime.SetType("HorizontalAlignment", typeof(System.Windows.HorizontalAlignment));
        runtime.SetType("Thickness", typeof(System.Windows.Thickness));
        runtime.SetType("VerticalAlignment", typeof(System.Windows.VerticalAlignment));
        runtime.SetType("FontStyles", typeof(System.Windows.FontStyles));
        runtime.SetType("FontWeights", typeof(System.Windows.FontWeights));
        runtime.SetType("Orientation", typeof(System.Windows.Controls.Orientation));
        runtime.SetType("TextWrapping", typeof(System.Windows.TextWrapping));
        runtime.SetType("BrushConverter", typeof(System.Windows.Media.BrushConverter));
        runtime.SetType("Brushes", typeof(System.Windows.Media.Brushes));
        runtime.SetType("Color", typeof(System.Windows.Media.Color));
        runtime.SetType("Colors", typeof(System.Windows.Media.Colors));
        runtime.SetType("SolidColorBrush", typeof(System.Windows.Media.SolidColorBrush));
    }
}