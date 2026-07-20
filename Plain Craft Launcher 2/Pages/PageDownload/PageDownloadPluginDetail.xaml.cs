using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Plugins;

namespace PCL;

public partial class PageDownloadPluginDetail
{
    private CancellationTokenSource? _loadCts;
    private PluginRepositoryEntry? _entry;
    private PluginMarketManifest? _manifest;
    private PluginMarketVersion? _selectedVersion;
    private bool _populatingVersions;

    public PageDownloadPluginDetail()
    {
        InitializeComponent();
        PageEnter += OnPageEnter;
        PageExit += () => _loadCts?.Cancel();
        ComboVersion.SelectionChanged += (_, _) => RefreshSelectedVersion();
        BtnRepository.Click += (_, _) => OpenRepository();
        BtnDeveloperTrust.Click += (_, _) => ToggleDeveloperTrust();
        BtnReleaseNotes.Click += (_, _) => OpenReleaseNotes();
        BtnInstall.Click += async (_, _) => await InstallSelectedVersionAsync();
        BtnUninstall.Click += async (_, _) => await UninstallAsync();
    }

    private void OnPageEnter()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadEntryAsync(_loadCts.Token);
    }

    private async Task LoadEntryAsync(CancellationToken ct)
    {
        _entry = ModMain.frmMain?.pageCurrent.pluginEntry;
        if (_entry is null)
        {
            LabName.Text = "插件不存在";
            LabDescription.Text = "无法读取插件市场条目。";
            BtnInstall.IsEnabled = false;
            return;
        }

        PanBack.ScrollToHome();
        PopulateHeader();
        LabReadmeStatus.Text = "正在加载 README...";
        LabReadmeStatus.Visibility = Visibility.Visible;
        ReadmeViewer.Visibility = Visibility.Collapsed;

        try
        {
            _manifest = _entry.MarketManifest;
            if (_manifest is null && _entry.ManifestUrlIsDirect && !string.IsNullOrWhiteSpace(_entry.ManifestUrl))
            {
                _manifest = await PluginRemoteInstallService.FetchManifestAsync(_entry.ManifestUrl, ct)
                    .ConfigureAwait(true);
                if (_manifest is not null) _entry.MarketManifest = _manifest;
            }
            ct.ThrowIfCancellationRequested();
            PopulateVersions();

            var readme = await PluginRepositoryService.FetchReadmeAsync(_entry, ct: ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(readme))
            {
                readme = "# " + _entry.Name + "\n\n" +
                         (string.IsNullOrWhiteSpace(_entry.Description) ? "该插件没有提供 README。" : _entry.Description);
            }
            ReadmeViewer.Markdown = readme;
            ReadmeViewer.Visibility = Visibility.Visible;
            LabReadmeStatus.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] 加载插件详情失败: " + _entry.Id, ModBase.LogLevel.Debug);
            PopulateVersions();
            LabReadmeStatus.Text = "README 加载失败：" + ex.Message;
            LabReadmeStatus.Visibility = Visibility.Visible;
        }
    }

    private void PopulateHeader()
    {
        if (_entry is null) return;
        const string noIcon = "pack://application:,,,/images/Icons/NoIcon.png";
        ImgLogo.Source = string.IsNullOrWhiteSpace(_entry.Logo) ? noIcon : _entry.Logo!;
        LabName.Text = _entry.Name;
        LabDescription.Text = _entry.Description ?? "暂无简介";
        LabMetadata.Text = $"ID: {_entry.Id}  ·  开发者: {_entry.GitHubLogin ?? _entry.Author ?? "未知"}  ·  来源: {_entry.SourceGroup}";
        BtnRepository.Visibility = IsHttpUrl(_entry.SourceRepoUrl ?? _entry.HomepageUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshTrustButton();

        PanBadges.Children.Clear();
        PanBadges.Children.Add(CreateBadge(_entry.DeveloperTrustLevel switch
        {
            PluginDeveloperTrustLevel.Official => "官方开发者",
            PluginDeveloperTrustLevel.Local => "已信任开发者",
            _ => "未信任开发者"
        }));
        PanBadges.Children.Add(CreateBadge(GetSourceTrustText(_entry)));
        if (_entry.Archived) PanBadges.Children.Add(CreateBadge("Archived"));
        if (_entry.Disabled) PanBadges.Children.Add(CreateBadge("Disabled"));
        if (_entry.Fork) PanBadges.Children.Add(CreateBadge("Fork"));
        foreach (var tag in (_entry.Tags ?? []).Take(8)) PanBadges.Children.Add(CreateBadge(tag));
    }

    private void PopulateVersions()
    {
        if (_entry is null) return;
        var versions = _manifest is not null
            ? PluginRepositoryService.GetVersionsNewestFirst(_manifest)
            : _entry.SelectedVersion is null ? [] : [_entry.SelectedVersion];

        _populatingVersions = true;
        ComboVersion.Items.Clear();
        foreach (var version in versions)
        {
            var download = PluginRepositoryService.SelectDownload(version, RuntimeInformation.OSArchitecture);
            var compatibility = PluginCompatibility.EvaluatePclCoreVersion(version.PclCoreVersion);
            var suffix = download is null
                ? " · 平台不兼容"
                : compatibility == PluginCoreCompatibilityStatus.TooOld ? " · Core 版本过旧" : string.Empty;
            ComboVersion.Items.Add(new MyComboBoxItem
            {
                Content = $"{version.Version} · PCL.Core {version.PclCoreVersion ?? "未知"}{suffix}",
                Tag = version
            });
        }
        ComboVersion.SelectedIndex = FindDefaultVersionIndex(versions);
        _populatingVersions = false;
        RefreshSelectedVersion();
    }

    private int FindDefaultVersionIndex(IReadOnlyList<PluginMarketVersion> versions)
    {
        if (versions.Count == 0) return -1;
        var selected = _entry?.SelectedVersion?.Version;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            for (var index = 0; index < versions.Count; index++)
                if (string.Equals(versions[index].Version, selected, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return 0;
    }

    private void RefreshSelectedVersion()
    {
        if (_populatingVersions || _entry is null) return;
        _selectedVersion = (ComboVersion.SelectedItem as MyComboBoxItem)?.Tag as PluginMarketVersion;
        if (_selectedVersion is null)
        {
            BtnInstall.IsEnabled = false;
            BtnReleaseNotes.Visibility = Visibility.Collapsed;
            LabVersionStatus.Text = "没有可选择的历史版本。";
            return;
        }

        var download = PluginRepositoryService.SelectDownload(_selectedVersion, RuntimeInformation.OSArchitecture);
        var compatibility = PluginCompatibility.EvaluatePclCoreVersion(_selectedVersion.PclCoreVersion);
        BtnReleaseNotes.Visibility = IsHttpUrl(_selectedVersion.ReleaseNotes) ? Visibility.Visible : Visibility.Collapsed;
        BtnInstall.IsEnabled = download is not null && compatibility != PluginCoreCompatibilityStatus.TooOld;

        var installed = PluginInstallService.GetRecord(_entry.Id);
        BtnUninstall.Visibility = installed is null ? Visibility.Collapsed : Visibility.Visible;
        var action = "安装所选版本";
        if (installed is not null)
        {
            try
            {
                action = PluginUpdateService.CompareVersion(_selectedVersion.Version ?? "0.0.0", installed.InstalledVersion) switch
                {
                    > 0 => "更新到此版本",
                    < 0 => "降级到此版本",
                    _ => "重新安装此版本"
                };
            }
            catch { action = "安装所选版本"; }
        }
        BtnInstall.Text = action;

        var platformText = download is null ? "当前平台没有可用安装包" : "当前平台安装包可用";
        var installedText = installed is null
            ? string.Empty
            : $" · 已安装 {PluginUpdateService.FormatVersion(installed.InstalledVersion)}"
              + (string.IsNullOrWhiteSpace(installed.InstalledSha256) ? string.Empty : " · SHA-256 已记录");
        LabVersionStatus.Text = $"版本 {_selectedVersion.Version ?? "未知"} · {platformText} · {PluginCompatibility.GetDisplayText(compatibility)}{installedText}";
    }

    private async Task InstallSelectedVersionAsync()
    {
        if (_entry is null || _selectedVersion is null) return;
        var download = PluginRepositoryService.SelectDownload(_selectedVersion, RuntimeInformation.OSArchitecture);
        if (download is null)
        {
            ModMain.MyMsgBox("该版本没有适用于当前平台的安装包。", "无法安装");
            return;
        }
        var source = new PluginInstallSourceEntry
        {
            Type = "package",
            Name = "Release",
            Url = download.PackageUrl,
            Sha256 = download.Sha256
        };

        BtnInstall.IsEnabled = false;
        try
        {
            ModMain.frmDownloadPluginStore ??= new PageDownloadPluginStore();
            await ModMain.frmDownloadPluginStore.InstallPluginAsync(_entry, source, _selectedVersion);
            RefreshSelectedVersion();
        }
        finally
        {
            RefreshSelectedVersion();
        }
    }

    private async Task UninstallAsync()
    {
        if (_entry is null || PluginInstallService.GetRecord(_entry.Id) is null) return;
        if (PluginLoaderService.LoadedPlugins.Any(plugin =>
                string.Equals(plugin.Id, _entry.Id, StringComparison.OrdinalIgnoreCase)))
        {
            ModMain.MyMsgBox("插件正在运行。请先禁用并重启启动器，再进行卸载。", "无法卸载");
            return;
        }
        if (ModMain.MyMsgBox("确定卸载插件 " + _entry.Id + "？\n此操作会删除插件文件与数据。",
                "确认卸载", button2: "取消", isWarn: true) != 1) return;

        BtnUninstall.IsEnabled = false;
        try
        {
            PluginInstallService.SetEnabled(_entry.Id, false);
            await PluginInstallService.UninstallAsync(_entry.Id);
            ModMain.frmDownloadPluginStore?.RefreshStore();
            HintService.Hint("插件 " + _entry.Id + " 已卸载。", HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] 卸载插件失败: " + _entry.Id, ModBase.LogLevel.Debug);
            ModMain.MyMsgBox("卸载失败：" + ex.Message, "错误");
        }
        finally
        {
            BtnUninstall.IsEnabled = true;
            RefreshSelectedVersion();
        }
    }

    private void ToggleDeveloperTrust()
    {
        if (_entry is null || string.IsNullOrWhiteSpace(_entry.GitHubLogin)
                           || _entry.DeveloperTrustLevel == PluginDeveloperTrustLevel.Official) return;

        if (_entry.DeveloperTrustLevel == PluginDeveloperTrustLevel.Local)
        {
            PluginDeveloperTrustService.RemoveLocal(_entry.GitHubLogin);
            _entry.DeveloperTrustLevel = PluginDeveloperTrustLevel.Other;
            HintService.Hint("已取消信任开发者 " + _entry.GitHubLogin);
        }
        else
        {
            PluginDeveloperTrustService.AddLocal(_entry.GitHubLogin);
            _entry.DeveloperTrustLevel = PluginDeveloperTrustLevel.Local;
            HintService.Hint("已信任开发者 " + _entry.GitHubLogin, HintType.Success);
        }
        RefreshTrustButton();
        PopulateHeader();
        ModMain.frmDownloadPluginStore?.OnDeveloperTrustChanged();
    }

    private void RefreshTrustButton()
    {
        if (_entry is null || string.IsNullOrWhiteSpace(_entry.GitHubLogin))
        {
            BtnDeveloperTrust.Visibility = Visibility.Collapsed;
            return;
        }
        BtnDeveloperTrust.Visibility = Visibility.Visible;
        BtnDeveloperTrust.IsEnabled = _entry.DeveloperTrustLevel != PluginDeveloperTrustLevel.Official;
        BtnDeveloperTrust.Text = _entry.DeveloperTrustLevel switch
        {
            PluginDeveloperTrustLevel.Official => "官方开发者",
            PluginDeveloperTrustLevel.Local => "取消信任",
            _ => "信任开发者"
        };
        BtnDeveloperTrust.ColorType = _entry.DeveloperTrustLevel == PluginDeveloperTrustLevel.Local
            ? MyButton.ColorState.Red
            : MyButton.ColorState.Highlight;
    }

    private void OpenRepository()
    {
        if (_entry is null) return;
        var url = _entry.SourceRepoUrl ?? _entry.HomepageUrl;
        if (IsHttpUrl(url)) ModBase.OpenWebsite(url!);
    }

    private void OpenReleaseNotes()
    {
        if (IsHttpUrl(_selectedVersion?.ReleaseNotes)) ModBase.OpenWebsite(_selectedVersion!.ReleaseNotes!);
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https";

    private static string GetSourceTrustText(PluginRepositoryEntry entry)
    {
        var source = PluginTrustService.GetRepositoryTrustUrl(entry);
        if (entry.SourceIsOfficial || PluginTrustService.IsOfficialRepository(source)) return "官方来源";
        if (entry.SourceKind is "GitHub" or "Topics") return "GitHub Topic";
        return PluginTrustService.IsRepositoryTrusted(source) ? "已信任来源" : "未信任来源";
    }

    private static Border CreateBadge(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2");
        var badge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Child = label
        };
        badge.SetResourceReference(Border.BackgroundProperty, "ColorBrushGray7");
        return badge;
    }
}
