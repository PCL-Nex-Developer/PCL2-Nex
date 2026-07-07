using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Plugins;
using PCL.Core.UI;

namespace PCL;

public partial class PagePluginsInstalled
{
    private IReadOnlyList<PluginUpdateCandidate> _updates = [];

    public PagePluginsInstalled()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    public void Build()
    {
        CheckPluginAutoUpdate.Checked = Config.Plugin.AutoUpdate;
        BuildInstalledList();
    }

    private void BuildInstalledList()
    {
        PanInstalled.Children.Clear();

        // 合并两个来源：安装记录 + 运行时已加载插件
        var records = new Dictionary<string, PluginInstallRecord>();
        try
        {
            foreach (var r in PluginInstallService.GetInstalledPlugins())
                records[r.PluginId] = r;
        }
        catch { }

        var loaded = new Dictionary<string, PluginRecord>();
        try
        {
            foreach (var lr in PluginLoaderService.LoadedPlugins)
                loaded[lr.Id] = lr;
        }
        catch { }

        var manifests = new Dictionary<string, PluginPackageManifest>();
        try
        {
            foreach (var (manifest, _) in PluginLoaderService.EnumerateInstalledPluginPackages(Paths.PluginInstalled))
                manifests[manifest.Id] = manifest;
        }
        catch { }

        // 合并所有插件 ID
        var allIds = new HashSet<string>(records.Keys);
        foreach (var id in manifests.Keys) allIds.Add(id);
        foreach (var id in loaded.Keys) allIds.Add(id);

        var enabledOrder = PluginEnablementService.GetEnabledPluginOrder();
        var orderedIds = allIds
            .OrderBy(id => id, Comparer<string>.Create((left, right) =>
                PluginEnablementService.CompareByEnabledOrder(left, right, enabledOrder)))
            .ToList();

        if (allIds.Count == 0)
        {
            var empty = new TextBlock { Text = "暂无已安装插件。可从商店安装，或通过下方远程安装。", FontSize = 13, TextWrapping = TextWrapping.Wrap };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanInstalled.Children.Add(empty);
            return;
        }

        foreach (var pluginId in orderedIds)
        {
            records.TryGetValue(pluginId, out var record);
            manifests.TryGetValue(pluginId, out var manifest);
            loaded.TryGetValue(pluginId, out var loadedRecord);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            var name = loadedRecord?.Manifest?.Name ?? manifest?.Name ?? pluginId;
            var version = record?.InstalledVersion?.ToString() ?? loadedRecord?.Manifest?.Version?.ToString() ?? manifest?.Version?.ToString() ?? "?";
            var title = new TextBlock { Text = name + "  v" + version, FontSize = 13, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
            info.Children.Add(title);

            var isEnabled = PluginEnablementService.IsEnabled(pluginId);
            var orderIndex = IndexOfPlugin(enabledOrder, pluginId);
            var orderText = orderIndex >= 0 ? "  |  加载顺序: " + (orderIndex + 1) : string.Empty;
            var source = record?.InstalledFrom ?? (manifest is not null ? "本地插件目录" : loadedRecord != null ? "旧布局（根目录 DLL）" : "未知");
            var state = loadedRecord != null ? loadedRecord.State.ToString() : (isEnabled ? "未加载" : "已禁用");
            var src = new TextBlock { Text = "来源: " + source + "  |  状态: " + state + orderText, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            src.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            info.Children.Add(src);

            var caps = loadedRecord?.Manifest?.Capabilities ?? PCL.Plugin.Abstractions.PluginCapabilities.None;
            var capsTextValue = caps != PCL.Plugin.Abstractions.PluginCapabilities.None
                ? caps.ToString()
                : manifest?.Capabilities is { Length: > 0 } manifestCaps
                    ? string.Join(", ", manifestCaps)
                    : string.Empty;
            if (!string.IsNullOrWhiteSpace(capsTextValue))
            {
                var capsText = new TextBlock { Text = "能力: " + capsTextValue, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
                capsText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
                info.Children.Add(capsText);
            }
            row.Children.Add(info);

            var orderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            orderButtons.SetValue(Grid.ColumnProperty, 1);

            var moveUpBtn = new MyButton { Text = "上移", Height = 28, MinWidth = 50, IsEnabled = orderIndex > 0 };
            var idMoveUp = pluginId;
            moveUpBtn.Click += (_, _) => MovePlugin(idMoveUp, -1);
            orderButtons.Children.Add(moveUpBtn);

            var moveDownBtn = new MyButton { Text = "下移", Height = 28, MinWidth = 50, Margin = new Thickness(4, 0, 0, 0), IsEnabled = orderIndex >= 0 && orderIndex < enabledOrder.Count - 1 };
            var idMoveDown = pluginId;
            moveDownBtn.Click += (_, _) => MovePlugin(idMoveDown, 1);
            orderButtons.Children.Add(moveDownBtn);

            row.Children.Add(orderButtons);

            var toggleBtn = new MyButton { Text = isEnabled ? "禁用" : "启用", Height = 28, MinWidth = 60, Margin = new Thickness(8, 0, 0, 0) };
            toggleBtn.SetValue(Grid.ColumnProperty, 2);
            var id1 = pluginId; var en = isEnabled;
            toggleBtn.Click += (_, _) => TogglePlugin(id1, !en);
            row.Children.Add(toggleBtn);

            var uninstallBtn = new MyButton { Text = "卸载", Height = 28, MinWidth = 60, Margin = new Thickness(4, 0, 0, 0), ColorType = MyButton.ColorState.Red };
            uninstallBtn.SetValue(Grid.ColumnProperty, 3);
            var id2 = pluginId;
            uninstallBtn.Click += (_, _) => _Uninstall(id2);
            row.Children.Add(uninstallBtn);

            PanInstalled.Children.Add(row);
        }
    }

    private void TogglePlugin(string pluginId, bool enabled)
    {
        try
        {
            PluginInstallService.SetEnabled(pluginId, enabled);
            ModMain.frmMain?.RefreshRestartButton(true);
            ModMain.MyMsgBox("插件 " + pluginId + (enabled ? " 已启用。" : " 已禁用。") + "\n请重启启动器后生效。", "插件状态");
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox("操作失败: " + ex.Message, "错误"); }
    }

    private void MovePlugin(string pluginId, int offset)
    {
        try
        {
            if (!PluginEnablementService.MoveEnabledPlugin(pluginId, offset)) return;
            ModMain.frmMain?.RefreshRestartButton(true);
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox("调整顺序失败: " + ex.Message, "错误"); }
    }

    private static int IndexOfPlugin(IReadOnlyList<string> pluginOrder, string pluginId)
    {
        for (var i = 0; i < pluginOrder.Count; i++)
        {
            if (string.Equals(pluginOrder[i], pluginId, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private async void _Uninstall(string pluginId)
    {
        if (PluginLoaderService.LoadedPlugins.Any(record => string.Equals(record.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            ModMain.MyMsgBox("插件 " + pluginId + " 正在运行。\n请先禁用插件并重启启动器，再进行卸载。", "无法卸载");
            return;
        }

        if (ModMain.MyMsgBox("确定卸载插件 " + pluginId + "？\n此操作将删除插件文件和数据，需要重启启动器。", "确认卸载", button2: "取消", isWarn: true) != 1) return;
        try
        {
            PluginInstallService.SetEnabled(pluginId, false);
            await PluginInstallService.UninstallAsync(pluginId);
            ModMain.MyMsgBox("插件 " + pluginId + " 已卸载。", "卸载完成");
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox("卸载失败: " + ex.Message, "错误"); }
    }

    private async void BtnInstallUrl_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var source = ModMain.MyMsgBoxInput("远程安装", "请输入插件 manifest URL，或直接输入 .pclx / .zip 插件包 URL：");
            if (string.IsNullOrWhiteSpace(source)) return;
            if (!source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                ModMain.MyMsgBox("远程安装仅支持 HTTP 或 HTTPS 地址。", "安装失败");
                return;
            }
            ModMain.MyMsgBox("正在解析插件来源，请稍候...", "安装准备");
            using var prepared = await PluginRemoteInstallService.PrepareAsync(source);
            var manifest = prepared.Manifest;
            if (ModMain.MyMsgBox("即将安装插件（" + prepared.SourceLabel + "）：\n\n名称: " + manifest.Name + "\n来源: " + prepared.SourceUrl + "\n\n未经仓库信任验证。\n\n重大安全提醒：插件会在启动器内运行代码，可能读取或修改本地文件、访问网络、修改启动器界面，甚至执行恶意操作。\n请只安装你完全信任的来源。", "确认安装", button2: "取消", isWarn: true) != 1) return;
            await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, manifest, prepared.SourceType, prepared.SourceUrl);
            ModMain.frmMain?.RefreshRestartButton(true);
            ModMain.MyMsgBox("插件 " + manifest.Name + " 安装成功！\n请重启启动器后生效。", "安装完成");
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox("安装失败: " + ex.Message, "错误"); }
    }

    private async void BtnInstallZip_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var files = SystemDialogs.SelectFiles("PCL 插件包(*.pclx;*.zip)|*.pclx;*.zip", "选择插件包", allowMultiSelect: false);
            if (files.Length == 0 || string.IsNullOrWhiteSpace(files[0])) return;
            using var prepared = await PluginLocalInstallService.PrepareZipAsync(files[0]);
            await ConfirmAndInstallPreparedAsync(prepared);
        }
        catch (Exception ex) { ModMain.MyMsgBox("导入失败: " + ex.Message, "错误"); }
    }

    private async System.Threading.Tasks.Task ConfirmAndInstallPreparedAsync(PluginPreparedInstall prepared)
    {
        var manifest = prepared.Manifest;
        if (ModMain.MyMsgBox("即将导入插件（" + prepared.SourceLabel + "）：\n\n名称: " + manifest.Name + "\n来源: " + prepared.SourceUrl + "\n\n重大安全提醒：插件会在启动器内运行代码，可能读取或修改本地文件、访问网络、修改启动器界面，甚至执行恶意操作。\n请只导入你完全信任的插件。", "确认导入", button2: "取消", isWarn: true) != 1) return;
        await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, manifest, prepared.SourceType, prepared.SourceUrl);
        ModMain.frmMain?.RefreshRestartButton(true);
        ModMain.MyMsgBox("插件 " + manifest.Name + " 导入成功！\n请重启启动器后生效。", "导入完成");
        BuildInstalledList();
    }

    private void CheckPluginAutoUpdate_Change(object senderRaw, bool user)
    {
        if (!user) return;
        Config.Plugin.AutoUpdate = CheckPluginAutoUpdate.Checked == true;
    }

    private async void BtnCheckPluginUpdates_Click(object sender, MouseButtonEventArgs e)
    {
        await CheckPluginUpdatesAsync(showHint: true);
    }

    private async System.Threading.Tasks.Task CheckPluginUpdatesAsync(bool showHint)
    {
        BtnCheckPluginUpdates.IsEnabled = false;
        PanPluginUpdates.Children.Clear();
        var loading = new TextBlock { Text = "正在检查插件更新...", FontSize = 13, TextWrapping = TextWrapping.Wrap };
        loading.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
        PanPluginUpdates.Children.Add(loading);

        try
        {
            _updates = await PluginUpdateService.CheckForUpdatesAsync();
            RenderPluginUpdates();
            if (showHint)
                ModMain.MyMsgBox(_updates.Count == 0 ? "所有市场插件都是最新版本。" : "发现 " + _updates.Count + " 个可更新插件。", "插件更新");
        }
        catch (Exception ex)
        {
            PanPluginUpdates.Children.Clear();
            var error = new TextBlock { Text = "检查失败: " + ex.Message, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            error.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanPluginUpdates.Children.Add(error);
            if (showHint) ModMain.MyMsgBox("检查更新失败: " + ex.Message, "插件更新");
        }
        finally
        {
            BtnCheckPluginUpdates.IsEnabled = true;
        }
    }

    private void RenderPluginUpdates()
    {
        PanPluginUpdates.Children.Clear();
        if (_updates.Count == 0)
        {
            var empty = new TextBlock { Text = "暂无可用更新。", FontSize = 13, TextWrapping = TextWrapping.Wrap };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanPluginUpdates.Children.Add(empty);
            return;
        }

        foreach (var candidate in _updates)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            var title = new TextBlock { Text = candidate.Entry.Name + "  v" + candidate.Installed.InstalledVersion + " -> v" + candidate.LatestVersion, FontSize = 13, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
            info.Children.Add(title);

            var src = new TextBlock { Text = "来源: " + candidate.Source.Url, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            src.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            info.Children.Add(src);
            row.Children.Add(info);

            var updateBtn = new MyButton { Text = "更新", Height = 28, MinWidth = 60, Margin = new Thickness(8, 0, 0, 0), ColorType = MyButton.ColorState.Highlight };
            updateBtn.SetValue(Grid.ColumnProperty, 1);
            updateBtn.Click += (_, _) => UpdatePlugin(candidate, refreshAfter: true);
            row.Children.Add(updateBtn);

            PanPluginUpdates.Children.Add(row);
        }
    }

    private async void UpdatePlugin(PluginUpdateCandidate candidate, bool refreshAfter)
    {
        try
        {
            var trustDecision = PluginUpdateService.EvaluateUpdate(candidate);
            var confirmMsg = "即将更新插件：\n\n名称: " + candidate.Entry.Name + "\n当前版本: v" + candidate.Installed.InstalledVersion + "\n最新版本: v" + candidate.LatestVersion + "\n下载源: " + candidate.Source.Url;
            if (trustDecision == PluginTrustDecision.RequireReconfirm)
                confirmMsg += "\n\n该更新涉及来源变化或能力变化，请确认你信任此版本。";

            if (ModMain.MyMsgBox(confirmMsg, "确认更新", button2: "取消", isWarn: trustDecision != PluginTrustDecision.Allow) != 1) return;

            using var prepared = await PluginRemoteInstallService.PrepareAsync(candidate.Source);
            await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, prepared.Manifest, prepared.SourceType, prepared.SourceUrl);
            ModMain.frmMain?.RefreshRestartButton(true);
            ModMain.MyMsgBox("插件 " + prepared.Manifest.Name + " 已更新到 v" + prepared.Manifest.Version + "！\n请重启启动器后生效。", "更新完成");
            BuildInstalledList();
            if (refreshAfter) await CheckPluginUpdatesAsync(showHint: false);
        }
        catch (Exception ex)
        {
            ModMain.MyMsgBox("更新失败: " + ex.Message, "错误");
        }
    }
}
