using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using System.Globalization;

namespace PCL;

public partial class PageLogRight
{
    public Run labDebug;
    public Run labError;
    public Run labFatal;
    public Run labInfo;
    public Run labWarn;
    private ModWatcher.Watcher? _boundWatcher;
    private ModWatcher.Watcher.GameExitEventHandler? _boundGameExitHandler;

    public PageLogRight()
    {
        Initialized += (_, _) => Init();
        Loaded += PageLogRight_Loaded;
        Unloaded += PageLogRight_Unloaded;
        InitializeComponent();
    }

    public void Init()
    {
        PanLogCard.Inlines.Clear();
        PanLogCard.Inlines.Add(new Run(Lang.Text("LogPage.Title")));
        PanLogCard.Inlines.Add(new Run(" | "));
        labDebug = new Run($"0 {Lang.Text("LogPage.Level.Debug")}")
            { Foreground = (Brush)System.Windows.Application.Current.Resources["ColorBrushDebug"] };
        PanLogCard.Inlines.Add(labDebug);
        PanLogCard.Inlines.Add(new Run(" | "));
        labInfo = new Run($"0 {Lang.Text("LogPage.Level.Info")}")
        {
            Foreground =
                (Brush)System.Windows.Application.Current.Resources[
                    ThemeManager.IsDarkMode ? "ColorBrushInfoDark" : "ColorBrushInfo"]
        };
        PanLogCard.Inlines.Add(labInfo);
        PanLogCard.Inlines.Add(new Run(" | "));
        labWarn = new Run($"0 {Lang.Text("LogPage.Level.Warn")}")
            { Foreground = (Brush)System.Windows.Application.Current.Resources["ColorBrushWarn"] };
        PanLogCard.Inlines.Add(labWarn);
        PanLogCard.Inlines.Add(new Run(" | "));
        labError = new Run($"0 {Lang.Text("LogPage.Level.Error")}")
            { Foreground = (Brush)System.Windows.Application.Current.Resources["ColorBrushError"] };
        PanLogCard.Inlines.Add(labError);
        PanLogCard.Inlines.Add(new Run(" | "));
        labFatal = new Run($"0 {Lang.Text("LogPage.Level.Fatal")}")
            { Foreground = (Brush)System.Windows.Application.Current.Resources["ColorBrushFatal"] };
        PanLogCard.Inlines.Add(labFatal);
    }

    private void PageLogRight_Loaded(object sender, RoutedEventArgs e)
    {
        ModAnimation.AniControlEnabled += 1;
        Reload();
        ModAnimation.AniControlEnabled -= 1;
    }

    private void PageLogRight_Unloaded(object sender, RoutedEventArgs e)
    {
        UnbindWatcher();
    }

    public void Reload()
    {
        // 初始化
        if (ModMain.frmLogLeft.currentLog is null || ModMain.frmLogLeft.currentUuid <= 0 ||
            ModMain.frmLogLeft.shownLogs.Count == 0)
        {
            UnbindWatcher();
            ModMain.frmMain.PageChange(ModMain.frmMain.pageCurrent);
            return;
        }

        var watcher = ModMain.frmLogLeft.currentLog;

        PanAllBack.Visibility = Visibility.Visible;
        CardOperation.Visibility = Visibility.Visible;
        BindWatcher(watcher);
        BtnOperationKill.IsEnabled = !watcher.gameProcess.HasExited;
        BtnOperationExportStackDump.IsEnabled = !watcher.gameProcess.HasExited &&
                                                !string.IsNullOrWhiteSpace(watcher.jStackPath);
        SliderMaxLog.Value = Config.System.MaxGameLog;
        // y = 10x + 50 (0 <= x <= 5, 50 <= y <= 100)
        // y = 50x - 150 (5 < x <= 13, 100 < y <= 500)
        // y = 100x - 800 (13 < x <= 28, 500 < y <= 2000)
        SliderMaxLog.getHintText = new Func<object, object>(v =>
        {
            return v switch
            {
                _ when (int)v <= 5 => ((int)v * 10 + 50).ToString(),
                _ when (int)v <= 13 => ((int)v * 50 - 150).ToString(),
                _ when (int)v <= 28 => ((int)v * 100 - 800).ToString(),
                _ => Lang.Text("LogPage.MaxLines.Unlimited")
            };
        });
        // 绑定日志输出
        PanLog.Document = ModMain.frmLogLeft.flowDocuments[ModMain.frmLogLeft.currentUuid];
        // 绑定事件
        RefreshLabText(watcher);
    }

    private void BindWatcher(ModWatcher.Watcher watcher)
    {
        if (!IsLoaded || ReferenceEquals(_boundWatcher, watcher))
            return;

        UnbindWatcher();
        _boundWatcher = watcher;
        _boundGameExitHandler = () => OnGameExit(watcher);
        watcher.LogOutput += OnLogOutput;
        watcher.GameExit += _boundGameExitHandler;
    }

    private void UnbindWatcher()
    {
        if (_boundWatcher is null)
            return;

        _boundWatcher.LogOutput -= OnLogOutput;
        if (_boundGameExitHandler is not null)
            _boundWatcher.GameExit -= _boundGameExitHandler;
        _boundWatcher = null;
        _boundGameExitHandler = null;
    }

    private void RefreshLabText(ModWatcher.Watcher watcher)
    {
        // 刷新计数器

        labFatal.Text = $"{watcher.countFatal} {Lang.Text("LogPage.Level.Fatal")}";
        labError.Text = $"{watcher.countError} {Lang.Text("LogPage.Level.Error")}";
        labWarn.Text = $"{watcher.countWarn} {Lang.Text("LogPage.Level.Warn")}";
        labInfo.Text = $"{watcher.countInfo} {Lang.Text("LogPage.Level.Info")}";
        labDebug.Text = $"{watcher.countDebug} {Lang.Text("LogPage.Level.Debug")}";
    }

    private void OnLogOutput(ModWatcher.Watcher sender, ModWatcher.LogOutputEventArgs e)
    {
        if (!ReferenceEquals(_boundWatcher, sender))
            return;

        ModBase.RunInUi(() =>
        {
            if (!IsLoaded || !ReferenceEquals(_boundWatcher, sender))
                return;

            if (CheckAutoScroll.Checked == true) PanBack.ScrollToBottom();
            RefreshLabText(sender);
        });
    }

    #region 滑动条

    private void SliderMaxLog_ValueChanged(object o, bool user)
    {
        var sender = (MySlider)o;
        Config.System.MaxGameLog = sender.Value;
        if (ModMain.frmSetupLauncherMisc is null)
            return;
        ModMain.frmSetupLauncherMisc.SliderMaxLog.Value = sender.Value;
    }

    #endregion

    #region 卡片按钮

    private void BtnOperationClear_Click(object sender, ModBase.RouteEventArgs e)
    {
        ModMain.frmLogLeft.flowDocuments[ModMain.frmLogLeft.currentUuid].Blocks.Clear();
    }

    private void BtnOperationExport_Click(object sender, ModBase.RouteEventArgs e)
    {
        var savePath = SystemDialogs.SelectSaveFile(Lang.Text("LogPage.Export.SelectLocation"),
            Lang.Text("LogPage.Export.GameLog.FileName", ModMain.frmLogLeft.currentLog.version.Name),
            Lang.Text("LogPage.Export.GameLog.Filter"));
        if (savePath.Length < 3)
            return;
        File.WriteAllLines(savePath, ModMain.frmLogLeft.currentLog.fullLog);
        HintService.Hint(Lang.Text("LogPage.Export.Success"), HintType.Success);
        ModBase.OpenExplorer(savePath);
    }

    private void BtnOperationKill_Click(object sender, ModBase.RouteEventArgs e)
    {
        if (ModMain.frmLogLeft.currentLog.State <= ModWatcher.Watcher.MinecraftState.Running)
        {
            ModMain.frmLogLeft.currentLog.Kill();
            HintService.Hint(Lang.Text("LogPage.Action.GameClosed", ModMain.frmLogLeft.currentLog.version.Name),
                HintType.Success);
        }
    }

    private void BtnOperationExportStackDump_Click(object sender, ModBase.RouteEventArgs e)
    {
        var formattedDate = DateTime.Now.ToString("G", CultureInfo.InvariantCulture)
            .Replace("/", "-")
            .Replace(":", ".")
            .Replace(" ", "_");
        var savePath = SystemDialogs.SelectSaveFile(Lang.Text("LogPage.Export.SelectLocation"),
            Lang.Text("LogPage.ExportStack.FileName", formattedDate),
            Lang.Text("LogPage.ExportStack.Filter"));
        if (savePath.Length < 3)
            return;
        HintService.Hint(Lang.Text("LogPage.ExportStack.Progress"));
        BtnOperationExportStackDump.IsEnabled = false;
        ModBase.RunInNewThread(() =>
        {
            var dump = ModMain.frmLogLeft.currentLog.ExportStackDump(savePath);
            File.WriteAllLines(savePath, dump);
            ModBase.RunInUi(() =>
            {
                HintService.Hint(Lang.Text("LogPage.ExportStack.Success"), HintType.Success);
                BtnOperationExportStackDump.IsEnabled = true;
            });
            ModBase.OpenExplorer(savePath);
        });
    }

    private void OnGameExit(ModWatcher.Watcher watcher)
    {
        ModBase.RunInUi(() =>
        {
            if (!IsLoaded || !ReferenceEquals(_boundWatcher, watcher))
                return;

            BtnOperationKill.IsEnabled = false;
            BtnOperationExportStackDump.IsEnabled = false;
        });
    }

    #endregion
}
