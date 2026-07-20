using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.App.Plugins;

namespace PCL;

public partial class PageDownloadPluginStore
{
    private CancellationTokenSource? _cts;
    private bool _isLoading;
    private bool _suppressFilterEvents;
    private int _loadGeneration;
    private IReadOnlyList<PluginRepositoryEntry>? _allEntries;
    private PluginDeveloperAllowlist _officialAllowlist = new();
    private readonly Dictionary<string, PluginUpdateService.PluginLatestManifestVersion> _latestVersionCache = new(StringComparer.OrdinalIgnoreCase);

    public PageDownloadPluginStore()
    {
        InitializeComponent();
        PanSearchBox.Search += (_, _) => Search();
        PanSearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };
        Load.Click += (_, _) => { if (Load.State.LoadingState == MyLoading.MyLoadingState.Error) RefreshStore(); };
        BtnFilterReset.Click += (_, _) => ResetFilters();
        CheckStatusArchived.IsChecked = Config.Plugin.ShowArchivedRepositories;
        CheckStatusDisabled.IsChecked = Config.Plugin.ShowDisabledRepositories;
        CheckStatusFork.IsChecked = Config.Plugin.ShowForkRepositories;
        SelectFilter(ComboSort, Config.Plugin.MarketSortOrder);
        SwitchShowOtherDevelopers.IsChecked = Config.Plugin.ShowNonWhitelistedDevelopers;
        CheckStatusArchived.Checked += RepositoryStatusOption_Change;
        CheckStatusArchived.Unchecked += RepositoryStatusOption_Change;
        CheckStatusDisabled.Checked += RepositoryStatusOption_Change;
        CheckStatusDisabled.Unchecked += RepositoryStatusOption_Change;
        CheckStatusFork.Checked += RepositoryStatusOption_Change;
        CheckStatusFork.Unchecked += RepositoryStatusOption_Change;
        SwitchShowOtherDevelopers.Checked += LocalFilter_Change;
        SwitchShowOtherDevelopers.Unchecked += LocalFilter_Change;
        ComboSourceGroup.SelectionChanged += (_, _) => RenderCurrentSearchResults();
        ComboTag.SelectionChanged += (_, _) => RenderCurrentSearchResults();
        ComboSort.SelectionChanged += (_, _) =>
        {
            Config.Plugin.MarketSortOrder = GetSelectedFilter(ComboSort);
            RenderCurrentSearchResults();
        };
    }

    public void LoadStore()
    {
        if (_allEntries is not null)
        {
            RenderCurrentSearchResults();
            PanLoad.Visibility = Visibility.Collapsed;
            CardPlugins.Visibility = Visibility.Visible;
            return;
        }

        if (!_isLoading) _ = LoadStoreAsync();
    }

    public void RefreshStore()
    {
        _ = LoadStoreAsync(clearLatestVersionCache: true);
    }

    private async System.Threading.Tasks.Task LoadStoreAsync(bool clearLatestVersionCache = false)
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        var previousCts = _cts;
        _cts = new CancellationTokenSource();
        previousCts?.Cancel();
        previousCts?.Dispose();
        _isLoading = true;
        var ct = _cts.Token;

        if (clearLatestVersionCache) _latestVersionCache.Clear();

        CardPlugins.Visibility = Visibility.Collapsed;
        PanLoad.Visibility = Visibility.Visible;
        Load.Text = "正在获取插件商店列表";
        Load.TextError = "插件商店列表获取失败";
        Load.State.LoadingState = MyLoading.MyLoadingState.Run;
        PanPlugins.Children.Clear();

        try
        {
            var marketTask = PluginMarketplaceService.LoadAsync(new PluginMarketQueryOptions
            {
                GitHubToken = Config.Plugin.GitHubToken,
                IncludeArchived = true,
                IncludeDisabled = true,
                IncludeForks = true
            }, ct: ct);
            var allowlistTask = PluginDeveloperTrustService.FetchOfficialAsync(ct: ct);
            await Task.WhenAll(marketTask, allowlistTask);

            var market = await marketTask;
            _officialAllowlist = await allowlistTask;
            var localAllowlist = PluginDeveloperTrustService.GetLocalAllowlist();
            foreach (var entry in market.Entries)
                entry.DeveloperTrustLevel = entry.SourceIsOfficial && string.IsNullOrWhiteSpace(entry.GitHubLogin)
                    ? PluginDeveloperTrustLevel.Official
                    : PluginDeveloperTrustService.GetTrustLevel(entry.GitHubLogin, _officialAllowlist, localAllowlist);

            _allEntries = market.Entries;
            if (ct.IsCancellationRequested) return;

            PopulateFilterOptions(_allEntries);
            RenderCurrentSearchResults();
            Load.State.LoadingState = MyLoading.MyLoadingState.Stop;
            PanLoad.Visibility = Visibility.Collapsed;
            CardPlugins.Visibility = Visibility.Visible;
            foreach (var error in market.Errors.Take(20))
                ModBase.Log($"[Plugins] 市场来源加载失败：{error.Repository}: {error.Message}", ModBase.LogLevel.Debug);
            _ = LoadLatestVersionsAsync(_allEntries, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (generation != _loadGeneration) return;
            Load.State.LoadingState = MyLoading.MyLoadingState.Stop;
            PanLoad.Visibility = Visibility.Collapsed;
            CardPlugins.Visibility = Visibility.Visible;
            if (_allEntries is not null) RenderCurrentSearchResults();
        }
        catch (Exception ex)
        {
            Load.TextError = "加载失败: " + ex.Message;
            Load.State.LoadingState = MyLoading.MyLoadingState.Error;
            PanLoad.Visibility = Visibility.Visible;
            CardPlugins.Visibility = Visibility.Collapsed;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _isLoading = false;
            }
        }
    }

    private async Task LoadLatestVersionsAsync(IReadOnlyList<PluginRepositoryEntry> entries, CancellationToken ct)
    {
        try
        {
            using var semaphore = new SemaphoreSlim(4);

            var tasks = entries.Select(async entry =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var latest = await PluginUpdateService.FetchLatestManifestVersionAsync(entry, ct).ConfigureAwait(false);
                    return (Entry: entry, Latest: latest);
                }
                catch
                {
                    return (Entry: entry, Latest: (PluginUpdateService.PluginLatestManifestVersion?)null);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            if (ct.IsCancellationRequested) return;

            var loaded = results
                .Where(r => r.Latest is not null)
                .ToDictionary(r => GetEntryCacheKey(r.Entry), r => r.Latest!, StringComparer.OrdinalIgnoreCase);

            if (loaded.Count == 0) return;

            ModBase.RunInUi(() =>
            {
                foreach (var item in loaded)
                    _latestVersionCache[item.Key] = item.Value;
                RenderCurrentSearchResults();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] Load latest plugin versions failed", ModBase.LogLevel.Debug);
        }
    }

    private void RenderPluginList(IReadOnlyList<PluginRepositoryEntry> entries)
    {
        PanPlugins.Children.Clear();

        if (entries.Count == 0)
        {
            var empty = new TextBlock { Text = "暂无可用插件。", FontSize = 13, TextWrapping = TextWrapping.Wrap };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanPlugins.Children.Add(empty);
            return;
        }

        var installed = PluginUpdateService.GetInstalledPluginRecords();
        foreach (var entry in SortEntries(entries))
            PanPlugins.Children.Add(CreatePluginRow(entry, installed));
    }

    private IEnumerable<PluginRepositoryEntry> SortEntries(IEnumerable<PluginRepositoryEntry> entries)
    {
        return GetSelectedFilter(ComboSort) switch
        {
            "updated" => entries
                .OrderByDescending(entry => entry.LastUpdatedAt ?? DateTimeOffset.MinValue)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "name-desc" => entries.OrderByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "name-asc" => entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            _ => entries
        };
    }

    private MyVirtualizingElement<MyCompItem> CreatePluginRow(
        PluginRepositoryEntry entry,
        IReadOnlyDictionary<string, PluginInstallRecord> installed)
    {
        installed.TryGetValue(entry.Id, out var installedRecord);
        return new MyVirtualizingElement<MyCompItem>(() =>
        {
            var item = new MyCompItem
            {
                Tag = entry,
                Title = entry.Name,
                Description = (entry.Description ?? "暂无简介").Replace("\r", "").Replace("\n", " "),
                Logo = string.IsNullOrWhiteSpace(entry.Logo)
                    ? "pack://application:,,,/images/Icons/NoIcon.png"
                    : entry.Logo!,
                ShowFavoriteBtn = installedRecord is not null
            };
            var author = entry.Author ?? entry.GitHubLogin;
            if (!string.IsNullOrWhiteSpace(author)) item.SubTitle = "  ·  " + author;
            item.Tags = BuildPluginTags(entry, installedRecord);
            item.LabVersion.Text = BuildVersionTag(entry, installedRecord);

            if (entry.DownloadCount is > 0)
                item.LabDownload.Text = Lang.CompactNumber(entry.DownloadCount.Value);
            else
            {
                item.SvgIconDownload.Visibility = Visibility.Collapsed;
                item.LabDownload.Visibility = Visibility.Collapsed;
                item.ColumnDownload1.Width = item.ColumnDownload2.Width = item.ColumnDownload3.Width = new GridLength(0);
            }

            if (entry.LastUpdatedAt is not null)
                item.LabTime.Text = Lang.TimeSpan(entry.LastUpdatedAt.Value.LocalDateTime - DateTime.Now, 1);
            else
            {
                item.SvgIconTime.Visibility = Visibility.Collapsed;
                item.LabTime.Visibility = Visibility.Collapsed;
                item.ColumnTime1.Width = item.ColumnTime2.Width = item.ColumnTime3.Width = new GridLength(0);
            }

            item.LabSource.Text = string.IsNullOrWhiteSpace(entry.SourceGroup) ? "未知" : entry.SourceGroup;
            item.LabSource.ToolTip = entry.SourceRepoUrl ?? entry.ManifestUrl;
            item.Click += (_, e) =>
            {
                e.Handled = true;
                OpenPluginDetail(entry);
            };

            if (installedRecord is not null)
            {
                item.BtnDelete.SvgIcon = "lucide/trash-2";
                item.BtnDelete.ToolTip = "卸载插件";
                item.BtnDelete.Click += (_, _) => UninstallPlugin(entry.Id);
            }

            return item;
        }) { Height = 64 };
    }

    private static void OpenPluginDetail(PluginRepositoryEntry entry)
    {
        ModMain.frmMain?.PageChange(new FormMain.PageStackData
        {
            page = FormMain.PageType.PluginDetail,
            pluginEntry = entry
        });
    }

    private List<string> BuildPluginTags(PluginRepositoryEntry entry, PluginInstallRecord? installed)
    {
        var tags = new List<string>();
        if (installed is not null)
        {
            var hasUpdate = TryGetKnownLatestVersion(entry, out var latestVersion)
                            && PluginUpdateService.CompareVersion(latestVersion, installed.InstalledVersion) > 0;
            tags.Add(hasUpdate ? "可更新" : "已安装");
        }
        tags.Add(PluginCompatibility.GetDisplayText(entry.CompatibilityStatus));
        tags.Add(entry.DeveloperTrustLevel switch
        {
            PluginDeveloperTrustLevel.Official => "官方开发者",
            PluginDeveloperTrustLevel.Local => "用户信任开发者",
            _ => "其他开发者"
        });
        if (entry.SelectedVersion is not null && entry.SelectedDownload is null)
            tags.Insert(0, "平台不兼容");
        if (entry.Archived) tags.Insert(0, "Archived");
        if (entry.Disabled) tags.Insert(0, "Disabled");
        if (entry.Fork) tags.Insert(0, "Fork");
        if (!string.IsNullOrWhiteSpace(entry.Group)) tags.Add(entry.Group);
        tags.AddRange(entry.Tags ?? []);
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }

    private string BuildVersionTag(PluginRepositoryEntry entry, PluginInstallRecord? installed)
    {
        if (installed is not null && TryGetKnownLatestVersion(entry, out var latestVersion))
        {
            if (PluginUpdateService.CompareVersion(latestVersion, installed.InstalledVersion) > 0)
                return PluginUpdateService.FormatVersion(installed.InstalledVersion) + " -> " + PluginUpdateService.FormatVersion(latestVersion);
            return PluginUpdateService.FormatVersion(installed.InstalledVersion);
        }

        if (TryGetKnownLatestVersion(entry, out var knownVersion)) return PluginUpdateService.FormatVersion(knownVersion);
        return entry.Version ?? "?";
    }

    private bool TryGetKnownLatestVersion(PluginRepositoryEntry entry, out string version)
    {
        if (_latestVersionCache.TryGetValue(GetEntryCacheKey(entry), out var latest))
        {
            version = latest.Version;
            return true;
        }

        return PluginUpdateService.TryGetDisplayVersion(entry, out version);
    }

    private static string GetEntryCacheKey(PluginRepositoryEntry entry)
        => string.IsNullOrWhiteSpace(entry.ManifestUrl) ? entry.Id : entry.ManifestUrl!;

    private static string FormatVersionRange(string? minVersion, string? maxVersion)
    {
        var hasMin = !string.IsNullOrWhiteSpace(minVersion);
        var hasMax = !string.IsNullOrWhiteSpace(maxVersion);
        if (hasMin && hasMax) return minVersion + " - " + maxVersion;
        if (hasMin) return ">= " + minVersion;
        if (hasMax) return "<= " + maxVersion;
        return "任意";
    }

    internal async Task<bool> InstallPluginAsync(
        PluginRepositoryEntry entry,
        PluginInstallSourceEntry sourceEntry,
        PluginMarketVersion? selectedVersion = null)
    {
        try
        {
            var trustDecision = PluginTrustService.EvaluateInstall(entry, PluginInstallSourceType.Repository);
            var repoSource = PluginTrustService.GetRepositoryTrustUrl(entry);
            if (string.IsNullOrWhiteSpace(repoSource)) repoSource = "未知来源";
            var confirmMsg = "即将安装插件：\n\n名称: " + entry.Name + "\n更新来源: " + repoSource + "\n下载源: " + sourceEntry.Url + "\n\n重大安全提醒：插件会在启动器内运行代码，可能读取或修改本地文件、访问网络、修改启动器行为，甚至执行恶意操作。\n请只安装你完全信任的来源。";
            if (entry.DeveloperTrustLevel == PluginDeveloperTrustLevel.Other)
                confirmMsg += "\n\n来源提醒：该 GitHub 开发者不在官方或本地白名单中。";
            if (trustDecision == PluginTrustDecision.RequireRepositoryTrust)
            {
                confirmMsg += "\n\n该插件来自未信任的第三方仓库。继续将信任该仓库并安装。";
                if (ModMain.MyMsgBox(confirmMsg, "信任确认", button2: "取消", isWarn: true) != 1) return false;
                var sourceKind = entry.SourceKind switch
                {
                    "GitHub" or "Topics" => PluginRepositorySourceKind.Topic,
                    "Manifest" => PluginRepositorySourceKind.Manifest,
                    _ => PluginRepositorySourceKind.Json
                };
                PluginTrustService.AddTrust(
                    repoSource,
                    string.IsNullOrWhiteSpace(entry.SourceGroup) ? repoSource : entry.SourceGroup,
                    PluginRepositorySourceType.Custom,
                    sourceKind);
            }
            else
            {
                if (ModMain.MyMsgBox(confirmMsg, "确认安装", button2: "取消", isWarn: true) != 1) return false;
            }

            ModMain.MyMsgBox("正在获取插件包，请稍候...", "安装中");
            using var prepared = await PrepareInstallAsync(entry, sourceEntry, selectedVersion);
            var persistentSource = PluginRepositoryService.GetPersistentInstallSource(
                entry, sourceEntry, prepared.SourceType, prepared.SourceUrl);
            await PluginInstallService.InstallFromDirectoryAsync(
                prepared.PluginRoot,
                prepared.Manifest,
                persistentSource.Type,
                persistentSource.Url,
                installedSha256: prepared.VerifiedSha256);
            ModMain.frmMain?.RefreshRestartButton(true);

            ModMain.MyMsgBox("插件 " + prepared.Manifest.Name + " 安装成功！\n重启启动器后生效。", "安装完成");
            _ = LoadStoreAsync();
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] Store install failed: " + entry.Id, ModBase.LogLevel.Debug);
            ModMain.MyMsgBox("安装失败: " + ex.Message, "错误");
            return false;
        }
    }

    private async Task<PluginPreparedInstall> PrepareInstallAsync(
        PluginRepositoryEntry entry,
        PluginInstallSourceEntry sourceEntry,
        PluginMarketVersion? selectedVersion)
    {
        var manifestVersion = selectedVersion ?? entry.SelectedVersion;
        if (manifestVersion is not null)
        {
            var download = PluginRepositoryService.SelectDownload(manifestVersion, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                ?? throw new InvalidDataException("该插件版本没有适用于当前平台的安装包。");
            manifestVersion.ResolvedPackageUrl = download.PackageUrl;
            manifestVersion.ResolvedSha256 = download.Sha256;
            if (entry.ManifestUrlIsDirect && !string.IsNullOrWhiteSpace(entry.ManifestUrl))
                return await PluginRemoteInstallService.PrepareManifestVersionAsync(entry.ManifestUrl, manifestVersion)
                    .ConfigureAwait(false);
            return await PluginRemoteInstallService.PreparePackageAsync(
                    download.PackageUrl,
                    download.Sha256,
                    expectedPluginId: entry.Id,
                    expectedVersion: manifestVersion.Version,
                    expectedDependencies: manifestVersion.ResolvedDependencies)
                .ConfigureAwait(false);
        }

        if (string.Equals(sourceEntry.Type, "manifest", StringComparison.OrdinalIgnoreCase)
            && _latestVersionCache.TryGetValue(GetEntryCacheKey(entry), out var latest))
        {
            return await PluginRemoteInstallService.PrepareManifestVersionAsync(sourceEntry.Url, latest.ManifestVersion).ConfigureAwait(false);
        }

        return await PluginRemoteInstallService.PrepareAsync(sourceEntry).ConfigureAwait(false);
    }

    internal void OnDeveloperTrustChanged()
        => RenderCurrentSearchResults();

    private async void UninstallPlugin(string pluginId)
    {
        if (PluginLoaderService.LoadedPlugins.Any(plugin => string.Equals(plugin.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            ModMain.MyMsgBox("插件正在运行。请先禁用并重启启动器，再进行卸载。", "无法卸载");
            return;
        }
        if (ModMain.MyMsgBox("确定卸载插件 " + pluginId + "？", "确认卸载", button2: "取消", isWarn: true) != 1) return;

        try
        {
            PluginInstallService.SetEnabled(pluginId, false);
            await PluginInstallService.UninstallAsync(pluginId);
            _ = LoadStoreAsync(clearLatestVersionCache: true);
        }
        catch (Exception ex)
        {
            ModMain.MyMsgBox("卸载失败: " + ex.Message, "错误");
        }
    }

    private void Search()
    {
        RenderCurrentSearchResults();
    }

    private void RepositoryStatusOption_Change(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        Config.Plugin.ShowArchivedRepositories = CheckStatusArchived.IsChecked == true;
        Config.Plugin.ShowDisabledRepositories = CheckStatusDisabled.IsChecked == true;
        Config.Plugin.ShowForkRepositories = CheckStatusFork.IsChecked == true;
        RenderCurrentSearchResults();
    }

    private void LocalFilter_Change(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        Config.Plugin.ShowNonWhitelistedDevelopers = SwitchShowOtherDevelopers.IsChecked == true;
        RenderCurrentSearchResults();
    }

    private void ResetFilters()
    {
        _suppressFilterEvents = true;
        try
        {
            CheckStatusArchived.IsChecked = false;
            CheckStatusDisabled.IsChecked = false;
            CheckStatusFork.IsChecked = false;
            SwitchShowOtherDevelopers.IsChecked = true;
            ComboSort.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
        ComboSourceGroup.SelectedIndex = 0;
        ComboTag.SelectedIndex = 0;
        Config.Plugin.ShowArchivedRepositories = false;
        Config.Plugin.ShowDisabledRepositories = false;
        Config.Plugin.ShowForkRepositories = false;
        Config.Plugin.ShowNonWhitelistedDevelopers = true;
        Config.Plugin.MarketSortOrder = "default";
        RenderCurrentSearchResults();
    }

    private void PopulateFilterOptions(IReadOnlyList<PluginRepositoryEntry> entries)
    {
        var selectedSource = GetSelectedFilter(ComboSourceGroup);
        var selectedTag = GetSelectedFilter(ComboTag);
        ComboSourceGroup.Items.Clear();
        ComboSourceGroup.Items.Add(new MyComboBoxItem { Content = "全部", Tag = string.Empty });
        foreach (var group in entries.Select(entry => entry.SourceGroup)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            ComboSourceGroup.Items.Add(new MyComboBoxItem { Content = group, Tag = group });

        ComboTag.Items.Clear();
        ComboTag.Items.Add(new MyComboBoxItem { Content = "全部", Tag = string.Empty });
        foreach (var tag in entries.SelectMany(entry => entry.Tags ?? [])
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            ComboTag.Items.Add(new MyComboBoxItem { Content = tag, Tag = tag });

        SelectFilter(ComboSourceGroup, selectedSource);
        SelectFilter(ComboTag, selectedTag);
    }

    private static string GetSelectedFilter(MyComboBox combo)
        => (combo.SelectedItem as FrameworkElement)?.Tag?.ToString() ?? string.Empty;

    private static void SelectFilter(MyComboBox combo, string value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is FrameworkElement item
                && string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void RenderCurrentSearchResults()
    {
        if (_allEntries is null) return;
        var query = PanSearchBox.Text?.Trim();
        var sourceGroup = GetSelectedFilter(ComboSourceGroup);
        var selectedTag = GetSelectedFilter(ComboTag);
        var filtered = _allEntries.Where(entry =>
                (Config.Plugin.ShowArchivedRepositories || !entry.Archived)
                && (Config.Plugin.ShowDisabledRepositories || !entry.Disabled)
                && (Config.Plugin.ShowForkRepositories || !entry.Fork)
                && (Config.Plugin.ShowNonWhitelistedDevelopers
                    || entry.DeveloperTrustLevel != PluginDeveloperTrustLevel.Other)
                && (string.IsNullOrWhiteSpace(sourceGroup)
                    || string.Equals(entry.SourceGroup, sourceGroup, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(selectedTag)
                    || (entry.Tags ?? []).Contains(selectedTag, StringComparer.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(query)
                    || (entry.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.Id?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.SourceGroup?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.Tags ?? []).Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        RenderPluginList(filtered);
    }
}
