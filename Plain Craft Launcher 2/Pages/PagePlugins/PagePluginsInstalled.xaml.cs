using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;
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

        var manifests = new Dictionary<string, (PluginPackageManifest Manifest, string Directory)>();
        try
        {
            foreach (var (manifest, directory) in PluginLoaderService.EnumerateInstalledPluginPackages(Paths.PluginInstalled))
                manifests[manifest.Id] = (manifest, directory);
        }
        catch { }

        // 列表只展示实际存在于本地插件目录或当前运行中的插件；安装记录仅作为显示元数据。
        var allIds = new HashSet<string>(manifests.Keys);
        foreach (var id in loaded.Keys) allIds.Add(id);

        var enabledOrder = PluginEnablementService.GetEnabledPluginOrder();
        var orderedIds = allIds
            .OrderBy(id => id, Comparer<string>.Create((left, right) =>
                PluginEnablementService.CompareByEnabledOrder(left, right, enabledOrder)))
            .ToList();

        if (allIds.Count == 0)
        {
            var empty = new TextBlock { Text = Lang.Text("Plugins.Installed.Empty"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanInstalled.Children.Add(empty);
            return;
        }

        foreach (var pluginId in orderedIds)
        {
            records.TryGetValue(pluginId, out var record);
            var hasManifest = manifests.TryGetValue(pluginId, out var manifestInfo);
            var manifest = hasManifest ? manifestInfo.Manifest : null;
            loaded.TryGetValue(pluginId, out var loadedRecord);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var noIcon = "pack://application:,,,/images/Icons/NoIcon.png";
            row.Children.Add(new MyImage
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(5),
                Source = ResolveInstalledLogo(manifest, hasManifest ? manifestInfo.Directory : null) ?? noIcon,
                FallbackSource = noIcon,
                VerticalAlignment = VerticalAlignment.Center
            });

            var info = new StackPanel();
            var name = loadedRecord?.Manifest?.Name ?? manifest?.Name ?? pluginId;
            var version = record?.InstalledVersion?.ToString() ?? loadedRecord?.Manifest?.Version?.ToString() ?? manifest?.Version?.ToString() ?? "?";
            var title = new TextBlock { Text = name + "  v" + version, FontSize = 13, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
            info.Children.Add(title);

            var isEnabled = PluginEnablementService.IsEnabled(pluginId);
            var orderIndex = IndexOfPlugin(enabledOrder, pluginId);
            var orderText = orderIndex >= 0 ? "  |  " + Lang.Text("Plugins.Installed.Label.LoadOrder") + (orderIndex + 1) : string.Empty;
            var source = record?.InstalledFrom ?? (manifest is not null ? Lang.Text("Plugins.Installed.Label.SourceLocal") : loadedRecord != null ? Lang.Text("Plugins.Installed.Label.SourceLegacy") : Lang.Text("Common.State.Unknown"));
            var state = loadedRecord != null ? loadedRecord.State.ToString() : (isEnabled ? Lang.Text("Plugins.Installed.Label.StateNotLoaded") : Lang.Text("Plugins.Installed.Label.StateDisabled"));
            var src = new TextBlock { Text = Lang.Text("Plugins.Installed.Label.SourcePrefix") + source + "  |  " + Lang.Text("Plugins.Installed.Label.StatePrefix") + state + orderText, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            src.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            info.Children.Add(src);

            info.SetValue(Grid.ColumnProperty, 2);
            row.Children.Add(info);

            var orderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            orderButtons.SetValue(Grid.ColumnProperty, 3);

            var moveUpBtn = new MyButton { Text = Lang.Text("Select.Folder.MoveUp"), Height = 28, MinWidth = 50, IsEnabled = orderIndex > 0 };
            var idMoveUp = pluginId;
            moveUpBtn.Click += (_, _) => MovePlugin(idMoveUp, -1);
            orderButtons.Children.Add(moveUpBtn);

            var moveDownBtn = new MyButton { Text = Lang.Text("Select.Folder.MoveDown"), Height = 28, MinWidth = 50, Margin = new Thickness(4, 0, 0, 0), IsEnabled = orderIndex >= 0 && orderIndex < enabledOrder.Count - 1 };
            var idMoveDown = pluginId;
            moveDownBtn.Click += (_, _) => MovePlugin(idMoveDown, 1);
            orderButtons.Children.Add(moveDownBtn);

            row.Children.Add(orderButtons);

            var toggleBtn = new MyButton
            {
                Text = isEnabled ? Lang.Text("Common.Action.Disable") : Lang.Text("Common.Action.Enable"),
                Height = 28,
                MinWidth = 60,
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = true
            };
            toggleBtn.SetValue(Grid.ColumnProperty, 4);
            var id1 = pluginId; var en = isEnabled;
            toggleBtn.Click += (_, _) => TogglePlugin(id1, !en);
            row.Children.Add(toggleBtn);

            var uninstallBtn = new MyButton { Text = Lang.Text("Plugins.Installed.Button.Uninstall"), Height = 28, MinWidth = 60, Margin = new Thickness(4, 0, 0, 0), ColorType = MyButton.ColorState.Red, IsEnabled = true };
            uninstallBtn.SetValue(Grid.ColumnProperty, 5);
            var id2 = pluginId;
            uninstallBtn.Click += (_, _) => _Uninstall(id2);
            row.Children.Add(uninstallBtn);

            PanInstalled.Children.Add(row);
        }
    }

    private static string? ResolveInstalledLogo(PluginPackageManifest? manifest, string? pluginDirectory)
    {
        var logo = manifest?.Logo ?? manifest?.Icon;
        if (string.IsNullOrWhiteSpace(logo)) return null;
        logo = logo.Trim();
        if (Uri.TryCreate(logo, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https") return logo;
        if (string.IsNullOrWhiteSpace(pluginDirectory)) return null;
        try
        {
            var root = Path.GetFullPath(pluginDirectory);
            var candidate = Path.GetFullPath(Path.Combine(root, logo));
            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(candidate)
                ? candidate
                : null;
        }
        catch { return null; }
    }

    private async void TogglePlugin(string pluginId, bool enabled)
    {
        try
        {
            if (!await PluginInstallService.SetEnabledAsync(pluginId, enabled)) return;
            ModMain.frmMain?.RefreshRestartButton(true);
            ModMain.MyMsgBox(Lang.Text(enabled ? "Plugins.Installed.Message.PluginEnabled" : "Plugins.Installed.Message.PluginDisabled", pluginId), Lang.Text("Plugins.Installed.Dialog.Title.PluginStatus"));
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.OperationFailed", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error")); }
    }

    private void MovePlugin(string pluginId, int offset)
    {
        try
        {
            if (!PluginEnablementService.MoveEnabledPlugin(pluginId, offset)) return;
            ModMain.frmMain?.RefreshRestartButton(true);
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.MoveFailed", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error")); }
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
            ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.PluginRunning", pluginId), Lang.Text("Plugins.Common.Dialog.Title.CannotUninstall"));
            return;
        }

        if (ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Dialog.Message.ConfirmUninstall", pluginId), Lang.Text("Plugins.Common.Dialog.Title.ConfirmUninstall"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) != 1) return;
        try
        {
            PluginInstallService.SetEnabled(pluginId, false);
            await PluginInstallService.UninstallAsync(pluginId);
            ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.UninstallSuccess", pluginId), Lang.Text("Plugins.Installed.Dialog.Title.UninstallComplete"));
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox(Lang.Text("Plugins.Store.Uninstall.Error", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error")); }
    }

    private async void BtnInstallUrl_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var source = ModMain.MyMsgBoxInput(Lang.Text("Plugins.Installed.Dialog.Title.RemoteInstall"), Lang.Text("Plugins.Installed.Dialog.Message.RemoteInstallUrl"));
            if (string.IsNullOrWhiteSpace(source)) return;
            if (!source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.RemoteInstallProtocolError"), Lang.Text("Plugins.Installed.Dialog.Title.RemoteInstallFailed"));
                return;
            }
            ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.ParsingSource"), Lang.Text("Plugins.Installed.Dialog.Title.InstallPreparing"));
            using var prepared = await PluginRemoteInstallService.PrepareAsync(source);
            var manifest = prepared.Manifest;
            if (ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.InstallSecurityWarning", prepared.SourceLabel, manifest.Name, prepared.SourceUrl), Lang.Text("Plugins.Installed.Dialog.Title.ConfirmInstall"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) != 1) return;
            await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, manifest, prepared.SourceType,
                prepared.SourceUrl, installedSha256: prepared.VerifiedSha256);
            ModMain.frmMain?.RefreshRestartButton(true);
            ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.InstallSuccess", manifest.Name), Lang.Text("Plugins.Installed.Dialog.Title.InstallComplete"));
            BuildInstalledList();
        }
        catch (Exception ex) { ModMain.MyMsgBox(Lang.Text("Plugins.Store.Install.Error", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error")); }
    }

    private async void BtnInstallZip_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var files = SystemDialogs.SelectFiles(Lang.Text("Plugins.Installed.Dialog.FileFilter"), Lang.Text("Plugins.Installed.Dialog.Title.SelectPackage"), allowMultiSelect: false);
            if (files.Length == 0 || string.IsNullOrWhiteSpace(files[0])) return;
            using var prepared = await PluginLocalInstallService.PrepareZipAsync(files[0]);
            await ConfirmAndInstallPreparedAsync(prepared);
        }
        catch (Exception ex) { ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.ImportFailed", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error")); }
    }

    private async System.Threading.Tasks.Task ConfirmAndInstallPreparedAsync(PluginPreparedInstall prepared)
    {
        var manifest = prepared.Manifest;
        if (ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.ImportSecurityWarning", prepared.SourceLabel, manifest.Name, prepared.SourceUrl), Lang.Text("Plugins.Installed.Dialog.Title.ConfirmImport"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) != 1) return;
        await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, manifest, prepared.SourceType,
            prepared.SourceUrl, installedSha256: prepared.VerifiedSha256);
        ModMain.frmMain?.RefreshRestartButton(true);
        ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.ImportSuccess", manifest.Name), Lang.Text("Plugins.Installed.Dialog.Title.ImportComplete"));
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
        var loading = new TextBlock { Text = Lang.Text("Plugins.Installed.Message.CheckingUpdates"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
        loading.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
        PanPluginUpdates.Children.Add(loading);

        try
        {
            _updates = await PluginUpdateService.CheckForUpdatesAsync();
            RenderPluginUpdates();
            if (showHint)
                ModMain.MyMsgBox(_updates.Count == 0 ? Lang.Text("Plugins.Installed.Message.AllUpToDate") : Lang.Text("Plugins.Installed.Message.UpdatesAvailable", _updates.Count), Lang.Text("Plugins.Installed.Dialog.Title.PluginUpdates"));
        }
        catch (Exception ex)
        {
            PanPluginUpdates.Children.Clear();
            var error = new TextBlock { Text = Lang.Text("Plugins.Installed.Message.CheckFailed", ex.Message), FontSize = 13, TextWrapping = TextWrapping.Wrap };
            error.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanPluginUpdates.Children.Add(error);
            if (showHint) ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.CheckUpdateFailed", ex.Message), Lang.Text("Plugins.Installed.Dialog.Title.PluginUpdates"));
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
            var empty = new TextBlock { Text = Lang.Text("Plugins.Installed.Message.NoUpdatesAvailable"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
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
            var title = new TextBlock { Text = candidate.Entry.Name, FontSize = 14, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
            info.Children.Add(title);

            var src = new TextBlock { Text = "v" + PluginUpdateService.FormatVersion(candidate.Installed.InstalledVersion) + " -> v" + PluginUpdateService.FormatVersion(candidate.LatestVersion) + "  |  " + candidate.Source.Url, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            src.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            info.Children.Add(src);
            row.Children.Add(info);

            var updateBtn = new MyButton { Text = Lang.Text("Plugins.Installed.Button.Update"), Height = 28, MinWidth = 60, Margin = new Thickness(8, 0, 0, 0), ColorType = MyButton.ColorState.Highlight };
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
            var confirmMsg = Lang.Text("Plugins.Installed.Message.UpdateConfirm", candidate.Entry.Name, PluginUpdateService.FormatVersion(candidate.Installed.InstalledVersion), PluginUpdateService.FormatVersion(candidate.LatestVersion), candidate.Source.Url);
            if (trustDecision == PluginTrustDecision.RequireReconfirm)
                confirmMsg += Lang.Text("Plugins.Installed.Message.UpdateReconfirm");

            if (ModMain.MyMsgBox(confirmMsg, Lang.Text("Plugins.Installed.Dialog.Title.ConfirmUpdate"), button2: Lang.Text("Common.Action.Cancel"), isWarn: trustDecision != PluginTrustDecision.Allow) != 1) return;

            using var prepared = await PluginRemoteInstallService.PrepareManifestVersionAsync(candidate.Source.Url, candidate.ManifestVersion);
            await PluginInstallService.InstallFromDirectoryAsync(
                prepared.PluginRoot,
                prepared.Manifest,
                candidate.Installed.SourceType,
                candidate.Installed.InstalledFrom,
                installedSha256: prepared.VerifiedSha256);
            ModMain.frmMain?.RefreshRestartButton(true);
            ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.UpdateSuccess", prepared.Manifest.Name, PluginUpdateService.FormatVersion(prepared.Manifest.Version)), Lang.Text("Plugins.Installed.Dialog.Title.UpdateComplete"));
            BuildInstalledList();
            if (refreshAfter) await CheckPluginUpdatesAsync(showHint: false);
        }
        catch (Exception ex)
        {
            ModMain.MyMsgBox(Lang.Text("Plugins.Installed.Message.UpdateFailed", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error"));
        }
    }
}
