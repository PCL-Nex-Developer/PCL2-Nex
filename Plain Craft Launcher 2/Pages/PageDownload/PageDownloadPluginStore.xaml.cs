using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App.Plugins;

namespace PCL;

public partial class PageDownloadPluginStore
{
    private CancellationTokenSource? _cts;
    private IReadOnlyList<PluginRepositoryEntry>? _allEntries;
    private readonly Dictionary<string, PluginUpdateService.PluginLatestManifestVersion> _latestVersionCache = new(StringComparer.OrdinalIgnoreCase);

    public PageDownloadPluginStore()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStore();
        PanSearchBox.Search += (_, _) => Search();
        PanSearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };
        Load.Click += (_, _) => { if (Load.State.LoadingState == MyLoading.MyLoadingState.Error) RefreshStore(); };
    }

    public void LoadStore()
    {
        _ = LoadStoreAsync();
    }

    public void RefreshStore()
    {
        _ = LoadStoreAsync(clearLatestVersionCache: true);
    }

    private async System.Threading.Tasks.Task LoadStoreAsync(bool clearLatestVersionCache = false)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        if (clearLatestVersionCache) _latestVersionCache.Clear();

        CardPlugins.Visibility = Visibility.Collapsed;
        PanLoad.Visibility = Visibility.Visible;
        Load.Text = "正在获取插件商店列表";
        Load.TextError = "插件商店列表获取失败";
        Load.State.LoadingState = MyLoading.MyLoadingState.Run;
        TextRepoInfo.Text = "正在加载...";
        PanPlugins.Children.Clear();

        try
        {
            var indexes = new List<PluginRepositoryIndex>();

            var officialUrl = PluginRepositoryService.GetOfficialIndexUrl();
            var officialIndex = await PluginRepositoryService.FetchIndexAsync(officialUrl, ct);
            if (officialIndex is not null)
            {
                indexes.Add(officialIndex);
                TextRepoInfo.Text = "官方市场: " + officialIndex.Name + " (" + officialIndex.Plugins.Count + " 个插件)";
            }
            else
            {
                TextRepoInfo.Text = "官方市场: 加载失败（网络不可用或注册表未创建）";
            }

            var trustRecords = PluginTrustService.GetAllTrustRecords();
            foreach (var repo in trustRecords.Where(r => r.Enabled))
            {
                try
                {
                    var index = await PluginRepositoryService.FetchIndexAsync(repo.RepoUrl, ct);
                    if (index is not null) indexes.Add(index);
                }
                catch { }
            }

            _allEntries = PluginRepositoryService.MergeIndexes(indexes);
            if (ct.IsCancellationRequested) return;

            RenderPluginList(_allEntries);
            Load.State.LoadingState = MyLoading.MyLoadingState.Stop;
            PanLoad.Visibility = Visibility.Collapsed;
            CardPlugins.Visibility = Visibility.Visible;
            TextRepoInfo.Text += "  |  合计 " + _allEntries.Count + " 个插件";
            _ = LoadLatestVersionsAsync(_allEntries, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Load.TextError = "加载失败: " + ex.Message;
            Load.State.LoadingState = MyLoading.MyLoadingState.Error;
            PanLoad.Visibility = Visibility.Visible;
            CardPlugins.Visibility = Visibility.Collapsed;
            TextRepoInfo.Text = "加载失败";
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

        for (var i = 0; i < entries.Count; i++)
            PanPlugins.Children.Add(CreatePluginRow(entries[i], installed, i == entries.Count - 1));
    }

    private Border CreatePluginRow(PluginRepositoryEntry entry, IReadOnlyDictionary<string, PluginInstallRecord> installed, bool isLast)
    {
        var row = new Border
        {
            MinHeight = 44,
            Padding = new Thickness(0, 4, 0, 4),
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1)
        };
        row.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray6");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        installed.TryGetValue(entry.Id, out var installedRecord);

        var main = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        var title = new TextBlock { Text = entry.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
        var titleUrl = GetTitleUrl(entry);
        if (!string.IsNullOrWhiteSpace(titleUrl))
        {
            title.Cursor = Cursors.Hand;
            title.MouseLeftButtonUp += (_, _) => ModBase.OpenWebsite(titleUrl);
        }
        titleRow.Children.Add(title);

        var tags = CreateTagRow(entry, installedRecord);
        titleRow.Children.Add(tags);
        main.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            var description = new TextBlock
            {
                Text = entry.Description,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            main.Children.Add(description);
        }

        layout.Children.Add(main);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal };
        var installSources = PluginRepositoryService.GetInstallSources(entry).ToList();

        if (installedRecord is not null)
        {
            var hasUpdate = TryGetKnownLatestVersion(entry, out var latestVersion)
                && PluginUpdateService.CompareVersion(latestVersion, installedRecord.InstalledVersion) > 0;
            var status = new TextBlock { Text = hasUpdate ? "可更新" : "已安装", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, hasUpdate ? "ColorBrush2" : "ColorBrushGray3");
            actionRow.Children.Add(status);

            if (hasUpdate)
            {
                foreach (var source in installSources)
                {
                    var button = new MyButton { Text = GetActionLabel("更新", source), Height = 28, MinWidth = 72, Margin = new Thickness(actionRow.Children.Count > 0 ? 8 : 0, 0, 0, 0), ColorType = MyButton.ColorState.Highlight };
                    button.Click += (_, _) => InstallPlugin(entry, source);
                    actionRow.Children.Add(button);
                }
            }
        }
        else
        {
            foreach (var source in installSources)
            {
                var button = new MyButton { Text = GetActionLabel("安装", source), Height = 28, MinWidth = 72, Margin = new Thickness(actionRow.Children.Count > 0 ? 8 : 0, 0, 0, 0), ColorType = MyButton.ColorState.Highlight };
                button.Click += (_, _) => InstallPlugin(entry, source);
                actionRow.Children.Add(button);
            }
            if (installSources.Count == 0)
            {
                var status = new TextBlock { Text = "无可用安装源", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                status.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
                actionRow.Children.Add(status);
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.HomepageUrl))
        {
            var linkButton = new MyButton { Text = "主页", Height = 28, MinWidth = 50, Margin = new Thickness(8, 0, 0, 0) };
            var url = entry.HomepageUrl;
            linkButton.Click += (_, _) => ModBase.OpenWebsite(url);
            actionRow.Children.Add(linkButton);
        }

        actionRow.VerticalAlignment = VerticalAlignment.Center;
        actionRow.SetValue(Grid.ColumnProperty, 1);
        layout.Children.Add(actionRow);
        row.Child = layout;
        return row;
    }

    private static Border CreateBadge(string text, string backgroundResource, string foregroundResource)
    {
        var badge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        badge.SetResourceReference(Border.BackgroundProperty, backgroundResource);
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, foregroundResource);
        return badge;
    }

    private WrapPanel CreateTagRow(PluginRepositoryEntry entry, PluginInstallRecord? installed)
    {
        var row = new WrapPanel { Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(CreateBadge(string.IsNullOrWhiteSpace(entry.Author) ? "未知发布者" : entry.Author!, "ColorBrushGray7", "ColorBrushGray2"));
        row.Children.Add(CreateBadge(BuildVersionTag(entry, installed), "ColorBrush8", "ColorBrush2"));
        return row;
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

    private bool TryGetKnownLatestVersion(PluginRepositoryEntry entry, out Version version)
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

    private static string? GetTitleUrl(PluginRepositoryEntry entry)
    {
        if (IsAbsoluteHttpUri(entry.HomepageUrl)) return entry.HomepageUrl!.Trim();
        if (IsAbsoluteHttpUri(entry.Repository?.Url)) return entry.Repository!.Url!.Trim();
        return null;
    }

    private static bool IsAbsoluteHttpUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static string GetActionLabel(string action, PluginInstallSourceEntry source)
    {
        if (string.Equals(source.Type, "manifest", StringComparison.OrdinalIgnoreCase)) return action;
        if (!string.IsNullOrWhiteSpace(source.Name)) return action + " " + source.Name;
        return action;
    }

    private static string FormatVersionRange(string? minVersion, string? maxVersion)
    {
        var hasMin = !string.IsNullOrWhiteSpace(minVersion);
        var hasMax = !string.IsNullOrWhiteSpace(maxVersion);
        if (hasMin && hasMax) return minVersion + " - " + maxVersion;
        if (hasMin) return ">= " + minVersion;
        if (hasMax) return "<= " + maxVersion;
        return "任意";
    }

    private async void InstallPlugin(PluginRepositoryEntry entry, PluginInstallSourceEntry sourceEntry)
    {
        try
        {
            var trustDecision = PluginTrustService.EvaluateInstall(entry, PluginInstallSourceType.Repository);
            var capList = entry.Capabilities.Length > 0 ? string.Join(", ", entry.Capabilities.Select(c => c.ToString())) : "无特殊能力";
            var repoSource = entry.SourceRepoUrl ?? "未知仓库";
            var confirmMsg = "即将安装插件：\n\n名称: " + entry.Name + "\n能力: " + capList + "\n市场注册表: " + repoSource + "\n下载源: " + sourceEntry.Url + "\n\n重大安全提醒：插件会在启动器内运行代码，可能读取或修改本地文件、访问网络、修改启动器界面，甚至执行恶意操作。\n请只安装你完全信任的来源。";
            if (trustDecision == PluginTrustDecision.RequireRepositoryTrust)
            {
                confirmMsg += "\n\n该插件来自未信任的第三方仓库。继续将信任该仓库并安装。";
                if (ModMain.MyMsgBox(confirmMsg, "信任确认", button2: "取消", isWarn: true) != 1) return;
                PluginTrustService.AddTrust(repoSource, repoSource, PluginRepositorySourceType.Custom);
            }
            else
            {
                if (ModMain.MyMsgBox(confirmMsg, "确认安装", button2: "取消", isWarn: true) != 1) return;
            }

            ModMain.MyMsgBox("正在获取插件包，请稍候...", "安装中");
            using var prepared = await PrepareInstallAsync(entry, sourceEntry);
            await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, prepared.Manifest, prepared.SourceType, prepared.SourceUrl);
            ModMain.frmMain?.RefreshRestartButton(true);

            ModMain.MyMsgBox("插件 " + prepared.Manifest.Name + " 安装成功！\n重启启动器后生效。", "安装完成");
            _ = LoadStoreAsync();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] Store install failed: " + entry.Id, ModBase.LogLevel.Debug);
            ModMain.MyMsgBox("安装失败: " + ex.Message, "错误");
        }
    }

    private async Task<PluginPreparedInstall> PrepareInstallAsync(PluginRepositoryEntry entry, PluginInstallSourceEntry sourceEntry)
    {
        if (string.Equals(sourceEntry.Type, "manifest", StringComparison.OrdinalIgnoreCase)
            && _latestVersionCache.TryGetValue(GetEntryCacheKey(entry), out var latest))
        {
            return await PluginRemoteInstallService.PrepareManifestVersionAsync(sourceEntry.Url, latest.ManifestVersion).ConfigureAwait(false);
        }

        return await PluginRemoteInstallService.PrepareAsync(sourceEntry).ConfigureAwait(false);
    }

    private void Search()
    {
        RenderCurrentSearchResults();
    }

    private void RenderCurrentSearchResults()
    {
        if (_allEntries is null) return;
        var query = PanSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            RenderPluginList(_allEntries);
            return;
        }

        var filtered = _allEntries.Where(entry =>
            (entry.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.Id?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();

        RenderPluginList(filtered);
    }
}
