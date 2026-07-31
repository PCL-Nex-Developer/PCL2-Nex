using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Essentials;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.App.Plugins;
using PCL.Core.Logging;
using PCL.Core.UI.Controls;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;

namespace PCL;

public partial class Application
{
    public Application()
    {
        // 注册生命周期事件
        Lifecycle.When(LifecycleState.Loaded, _ApplicationStartup);
        PluginCompatibility.ConfirmationAsync = ConfirmPluginCompatibilityAsync;
        Lifecycle.When(LifecycleState.WindowCreated, _ShowEnvironmentWarning);
        Lifecycle.When(LifecycleState.WindowCreated, UriActionService.Register);
        SessionEnding += _ApplicationSessionEnding;
    }

    private static Task<bool> ConfirmPluginCompatibilityAsync(
        PluginCompatibilityConfirmationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var action = context.Action == PluginCompatibilityAction.Install ? "安装" : "启用";
        var message = context.Status == PluginCoreCompatibilityStatus.Future
            ? "该插件使用了比当前启动器更新的 PCL.Core 版本，可能无法正常使用或导致崩溃。是否仍然" + action + "？"
            : $"插件 {context.PluginName} 的 pclCoreVersion 缺失或格式错误，无法确认兼容性。是否仍然{action}？";
        var result = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
            message,
            "插件兼容性提示",
            button1: "继续" + action,
            button2: "取消",
            isWarn: true));
        return Task.FromResult(result == 1);
    }

    // 开始
    private static void _ApplicationStartup()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 创建自定义跟踪监听器，用于检测是否存在 Binding 失败
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorTraceListener());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            StartupValidation.EnsureWpfFont();

            // 检查参数调用
            var args = Basics.CommandLineArguments;
            if (args.Length > 0)
                if (args[0] == "--gpu")
                    // 调整显卡设置
                    try
                    {
                        ModMain.SetGPUPreference(args[1].Trim('"'));
                        Environment.Exit((int)ModBase.ProcessReturnValues.TaskDone);
                    }
                    catch (Exception)
                    {
                        Environment.Exit((int)ModBase.ProcessReturnValues.Fail);
                    }

            // 初始化文件结构
            Directory.CreateDirectory(ModBase.exePath + @"PCL\Pictures");
            Directory.CreateDirectory(ModBase.exePath + @"PCL\Musics");
            Directory.CreateDirectory(Path.Combine(ModBase.pathTemp, "Cache"));
            Directory.CreateDirectory(Path.Combine(ModBase.pathTemp, "Download"));
            Directory.CreateDirectory(ModBase.pathAppdata);

            // 设置 ToolTipService 默认值
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject),
                new FrameworkPropertyMetadata(100));
            Tooltip.Enable();

            // 设置初始窗口
            if (Config.Preference.ShowStartupLogo)
            {
                ModMain.frmStart = new ResourceStartupSplash(@"Images\icon.png");
                ModMain.frmStart.Show(false, true);
            }

            // 设置初始化
            _ = Config.Debug.Enabled;
            _ = Config.Debug.AnimationSpeed;
            _ = Config.Network.HttpProxy.CustomAddress;
            _ = Config.Network.HttpProxy.CustomUsername;
            _ = Config.Network.HttpProxy.Type;
            _ = Config.Download.ThreadLimit;
            _ = Config.Download.SpeedLimit;
            _ = Config.Preference.Font;
            // 删除旧日志
            for (var i = 1; i <= 5; i++)
            {
                var oldLogFile = $@"{ModBase.exePath}PCL\Log-Nex{i}.log";
                if (File.Exists(oldLogFile))
                    File.Delete(oldLogFile);
            }

            // 计时
            ModBase.Log("[Start] 第一阶段加载用时：" + (TimeUtils.GetTimeTick() - ModBase.applicationStartTick) + " ms");
            ModBase.applicationStartTick = TimeUtils.GetTimeTick();
            ModAnimation.AniControlEnabled += 1;
        }
        catch (Exception ex)
        {
            try
            {
                LogWrapper.Error(ex, "Application initialization failed");
            }
            catch
            {
                // 初始化日志系统本身不可用时仍然保留原有错误弹窗。
            }
            var filePath = Basics.ExecutablePath;
            MessageBox.Show(ex + "\r\n" + Lang.Text("Application.InitializationError.Path",
                    string.IsNullOrEmpty(filePath)
                        ? Lang.Text("Application.InitializationError.PathUnavailable")
                        : filePath),
                Lang.Text("Application.InitializationError.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            FormMain.EndProgramForce(ModBase.ProcessReturnValues.Exception);
        }
    }

    // 检测异常环境
    private static void _ShowEnvironmentWarning()
    {
        var problemList = new List<string>();
        var currentOsVersion = NtInterop.GetCurrentOsVersion();
        if (currentOsVersion.Build < 17763)
            problemList.Add(Lang.Text("Application.EnvironmentWarning.WindowsVersion"));
        if (SystemInfo.Is32BitSystem)
            problemList.Add(Lang.Text("Application.EnvironmentWarning.System32Bit"));
        if (ModBase.exePath.Contains(Path.GetTempPath()) || ModBase.exePath.Contains(@"AppData\Local\Temp\"))
            problemList.Add(Lang.Text("Application.EnvironmentWarning.TempFolder"));
        if (ModBase.exePath.ContainsF("wechat_files", true) || ModBase.exePath.ContainsF("WeChat Files", true) ||
            ModBase.exePath.ContainsF("Tencent Files", true))
            problemList.Add(Lang.Text("Application.EnvironmentWarning.SocialSoftwareFolder"));
        if (problemList.Count == 0) return;

        ModMain.MyMsgBox(
            Lang.Text("Application.EnvironmentWarning.Message", problemList.Join("\r\n")),
            Lang.Text("Application.EnvironmentWarning.Title"),
            Lang.Text("Application.EnvironmentWarning.IKnow"),
            isWarn: true);
    }

    // 结束
    private static void _ApplicationSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        ModMain.frmMain.EndProgram(false);
    }

    /**
     * Error handling for unhandled exceptions
     */
    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (ModBase.isProgramEnded) return;

            ModBase.FeedbackInfo();

            var detail = e.Exception.ToString();

            // Automatic error analysis for environment issues
            if (detail.Contains("System.Windows.Threading.Dispatcher.Invoke") ||
                detail.Contains("MS.Internal.AppModel.ITaskbarList.HrInit") ||
                detail.Contains("未能加载文件或程序集"))
            {
                ModBase.OpenWebsite("https://get.dot.net/8");
                LogWrapper.Error(e.Exception,
                    "Your .NET Desktop Runtime is outdated or corrupted. Please reinstall .NET 8!");
            }
            else
            {
                LogWrapper.Error(e.Exception, "An unexpected error occurred");
            }
        }
        catch
        {
            // Equivalent to On Error Resume Next for safety in the global handler
        }
    }

    // Win32 API declaration for DLL directory configuration
    [DllImport("kernel32", EntryPoint = "SetDllDirectoryA", CharSet = CharSet.Ansi)]
    private static extern bool _SetDllDirectory(string lpPathName);
    // 切换窗口

    // 控件模板事件
    private void _MyIconButtonClick(object sender, EventArgs e)
    {
    }

    // 自定义监听器类
    public class BindingErrorTraceListener : TraceListener
    {
        public override void Write(string message)
        {
            ModBase.Log($"警告，检测到 Binding 失败：{message}");
        }

        public override void WriteLine(string message)
        {
            ModBase.Log($"警告，检测到 Binding 失败：{message}");
        }
    }

}
