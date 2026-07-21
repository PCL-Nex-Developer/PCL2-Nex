using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Localization;
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
            LabName.Text = Lang.Text("Plugins.Detail.Label.PluginNotFound");
            LabDescription.Text = Lang.Text("Plugins.Detail.Label.CannotReadEntry");
            BtnInstall.IsEnabled = false;
            return;
        }

        PanBack.ScrollToHome();
        PopulateHeader();
        LabReadmeStatus.Text = Lang.Text("Plugins.Detail.Label.LoadingReadme");
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
                         (string.IsNullOrWhiteSpace(_entry.Description) ? Lang.Text("Plugins.Detail.Label.ReadmeNotProvided") : _entry.Description);
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
            LabReadmeStatus.Text = Lang.Text("Plugins.Detail.Label.ReadmeLoadFailed", ex.Message);
            LabReadmeStatus.Visibility = Visibility.Visible;
        }
    }

    private void PopulateHeader()
    {
        if (_entry is null) return;
        const string noIcon = "pack://application:,,,/images/Icons/NoIcon.png";
        ImgLogo.Source = string.IsNullOrWhiteSpace(_entry.Logo) ? noIcon : _entry.Logo!;
        LabName.Text = _entry.Name;
        LabDescription.Text = _entry.Description ?? Lang.Text("Plugins.Detail.Label.NoDescription");
        var developerLabel = Lang.Text("Plugins.Detail.Label.Developer");
        var sourceLabel = Lang.Text("Plugins.Detail.Label.Source");
        var unknownDev = _entry.GitHubLogin ?? _entry.Author ?? Lang.Text("Common.State.Unknown");
        LabMetadata.Text = $"ID: {_entry.Id}  ·  {developerLabel}: {unknownDev}  ·  {sourceLabel}: {_entry.SourceGroup}";
        BtnRepository.Visibility = IsHttpUrl(_entry.SourceRepoUrl ?? _entry.HomepageUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshTrustButton();

        PanBadges.Children.Clear();
        PanBadges.Children.Add(CreateBadge(_entry.DeveloperTrustLevel switch
        {
            PluginDeveloperTrustLevel.Official => Lang.Text("Plugins.Detail.Developer.Official"),
            PluginDeveloperTrustLevel.Local => Lang.Text("Plugins.Detail.Developer.Trusted"),
            _ => Lang.Text("Plugins.Detail.Developer.Untrusted")
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
                ? " · " + Lang.Text("Plugins.Detail.Label.PlatformIncompatible")
                : compatibility == PluginCoreCompatibilityStatus.TooOld ? " · " + Lang.Text("Plugins.Compatibility.Status.TooOld") : string.Empty;
            ComboVersion.Items.Add(new MyComboBoxItem
            {
                Content = $"{version.Version} · PCL.Core {version.PclCoreVersion ?? Lang.Text("Common.State.Unknown")}{suffix}",
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
            LabVersionStatus.Text = Lang.Text("Plugins.Detail.Label.NoHistoryVersion");
            return;
        }

        var download = PluginRepositoryService.SelectDownload(_selectedVersion, RuntimeInformation.OSArchitecture);
        var compatibility = PluginCompatibility.EvaluatePclCoreVersion(_selectedVersion.PclCoreVersion);
        BtnReleaseNotes.Visibility = IsHttpUrl(_selectedVersion.ReleaseNotes) ? Visibility.Visible : Visibility.Collapsed;
        BtnInstall.IsEnabled = download is not null && compatibility != PluginCoreCompatibilityStatus.TooOld;

        var installed = PluginInstallService.GetRecord(_entry.Id);
        BtnUninstall.Visibility = installed is null ? Visibility.Collapsed : Visibility.Visible;
        var action = Lang.Text("Plugins.Detail.Button.InstallSelected");
        if (installed is not null)
        {
            try
            {
                action = PluginUpdateService.CompareVersion(_selectedVersion.Version ?? "0.0.0", installed.InstalledVersion) switch
                {
                    > 0 => Lang.Text("Plugins.Detail.Button.UpdateToThis"),
                    < 0 => Lang.Text("Plugins.Detail.Button.DowngradeToThis"),
                    _ => Lang.Text("Plugins.Detail.Button.ReinstallThisVersion")
                };
            }
            catch { action = Lang.Text("Plugins.Detail.Button.InstallSelected"); }
        }
        BtnInstall.Text = action;

        var platformText = download is null ? Lang.Text("Plugins.Detail.Label.PackageNotAvailable") : Lang.Text("Plugins.Detail.Label.PackageAvailable");
        var installedText = installed is null
            ? string.Empty
            : $" · {Lang.Text("Plugins.Detail.Label.Installed")} {PluginUpdateService.FormatVersion(installed.InstalledVersion)}"
              + (string.IsNullOrWhiteSpace(installed.InstalledSha256) ? string.Empty : " · " + Lang.Text("Plugins.Detail.Label.Sha256Recorded"));
        var versionStr = _selectedVersion.Version ?? Lang.Text("Common.State.Unknown");
        LabVersionStatus.Text = $"{Lang.Text("Plugins.Detail.Label.VersionPrefix")} {versionStr} · {platformText} · {PluginCompatibility.GetDisplayText(compatibility)}{installedText}";
    }

    private async Task InstallSelectedVersionAsync()
    {
        if (_entry is null || _selectedVersion is null) return;
        var download = PluginRepositoryService.SelectDownload(_selectedVersion, RuntimeInformation.OSArchitecture);
        if (download is null)
        {
            ModMain.MyMsgBox(Lang.Text("Plugins.Detail.Message.PackageNotAvailable"), Lang.Text("Plugins.Detail.Dialog.Title.CannotInstall"));
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
            ModMain.MyMsgBox(Lang.Text("Plugins.Store.Uninstall.Running"), Lang.Text("Plugins.Common.Dialog.Title.CannotUninstall"));
            return;
        }
        if (ModMain.MyMsgBox(Lang.Text("Plugins.Detail.Dialog.Message.ConfirmUninstall", _entry.Id),
                Lang.Text("Plugins.Common.Dialog.Title.ConfirmUninstall"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) != 1) return;

        BtnUninstall.IsEnabled = false;
        try
        {
            PluginInstallService.SetEnabled(_entry.Id, false);
            await PluginInstallService.UninstallAsync(_entry.Id);
            ModMain.frmDownloadPluginStore?.RefreshStore();
            HintService.Hint(Lang.Text("Plugins.Detail.Message.UninstallSuccess", _entry.Id), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] 卸载插件失败: " + _entry.Id, ModBase.LogLevel.Debug);
            ModMain.MyMsgBox(Lang.Text("Plugins.Detail.Message.UninstallFailed", ex.Message), Lang.Text("Plugins.Common.Dialog.Title.Error"));
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
            HintService.Hint(Lang.Text("Plugins.Detail.Message.DeveloperUntrusted", _entry.GitHubLogin));
        }
        else
        {
            PluginDeveloperTrustService.AddLocal(_entry.GitHubLogin);
            _entry.DeveloperTrustLevel = PluginDeveloperTrustLevel.Local;
            HintService.Hint(Lang.Text("Plugins.Detail.Message.DeveloperTrusted", _entry.GitHubLogin), HintType.Success);
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
            PluginDeveloperTrustLevel.Official => Lang.Text("Plugins.Detail.Developer.Official"),
            PluginDeveloperTrustLevel.Local => Lang.Text("Plugins.Detail.Button.UntrustDeveloper"),
            _ => Lang.Text("Plugins.Detail.Button.TrustDeveloper")
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
        if (entry.SourceIsOfficial || PluginTrustService.IsOfficialRepository(source)) return Lang.Text("Plugins.Detail.Label.SourceOfficial");
        if (entry.SourceKind is "GitHub" or "Topics") return Lang.Text("Plugins.Detail.Label.SourceGitHubTopic");
        return PluginTrustService.IsRepositoryTrusted(source) ? Lang.Text("Plugins.Detail.Label.SourceTrusted") : Lang.Text("Plugins.Detail.Label.SourceUntrusted");
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
