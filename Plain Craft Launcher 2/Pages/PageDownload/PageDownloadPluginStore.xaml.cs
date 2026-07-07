using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App.Plugins;

namespace PCL;

public partial class PageDownloadPluginStore
{
    private CancellationTokenSource? _cts;
    private IReadOnlyList<PluginRepositoryEntry>? _allEntries;

    public PageDownloadPluginStore()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStore();
        PanSearchBox.Search += (_, _) => Search();
        PanSearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };
    }

    public void LoadStore()
    {
        _ = LoadStoreAsync();
    }

    private async System.Threading.Tasks.Task LoadStoreAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        TextLoading.Text = "正在从插件市场获取已审核插件列表...";
        TextRepoInfo.Text = "正在加载...";

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
            TextRepoInfo.Text += "  |  合计 " + _allEntries.Count + " 个插件";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TextLoading.Text = "加载失败: " + ex.Message;
            TextRepoInfo.Text = "加载失败";
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

        Dictionary<string, PluginInstallRecord> installed;
        try { installed = PluginInstallService.GetInstalledPlugins().ToDictionary(r => r.PluginId, r => r); }
        catch { installed = new Dictionary<string, PluginInstallRecord>(); }

        for (var i = 0; i < entries.Count; i++)
            PanPlugins.Children.Add(CreatePluginRow(entries[i], installed, i == entries.Count - 1));
    }

    private Border CreatePluginRow(PluginRepositoryEntry entry, Dictionary<string, PluginInstallRecord> installed, bool isLast)
    {
        var row = new Border
        {
            MinHeight = 64,
            Padding = new Thickness(0, 7, 0, 7),
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1)
        };
        row.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray6");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var main = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new DockPanel { LastChildFill = true };
        var title = new TextBlock { Text = entry.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        badgeRow.SetValue(DockPanel.DockProperty, Dock.Right);
        badgeRow.Children.Add(CreateBadge("v" + (entry.Version ?? "?"), "ColorBrush8", "ColorBrush2"));
        if (installed.ContainsKey(entry.Id)) badgeRow.Children.Add(CreateBadge("已安装", "ColorBrushGray7", "ColorBrushGray2"));
        titleRow.Children.Add(badgeRow);
        titleRow.Children.Add(title);
        main.Children.Add(titleRow);

        var infoParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Author)) infoParts.Add(entry.Author!);
        infoParts.Add(entry.Id);
        if (!string.IsNullOrWhiteSpace(entry.License)) infoParts.Add(entry.License!);
        var apiRange = FormatVersionRange(entry.MinApiVersion?.ToString(), entry.MaxApiVersion?.ToString());
        if (apiRange != "任意") infoParts.Add("API " + apiRange);
        if (!string.IsNullOrWhiteSpace(entry.TrustLevel)) infoParts.Add(entry.TrustLevel!);

        var info = new TextBlock
        {
            Text = string.Join("  |  ", infoParts),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        info.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
        main.Children.Add(info);

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            var description = new TextBlock
            {
                Text = entry.Description,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            main.Children.Add(description);
        }

        var tags = CreateTagRow(entry);
        if (tags.Children.Count > 0) main.Children.Add(tags);
        layout.Children.Add(main);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var installSources = PluginRepositoryService.GetInstallSources(entry).ToList();

        if (installed.TryGetValue(entry.Id, out var record))
        {
            var status = new TextBlock { Text = "已安装 v" + record.InstalledVersion, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
            actionRow.Children.Add(status);

            if (!string.IsNullOrWhiteSpace(entry.Version) && Version.TryParse(entry.Version, out var remoteVersion) && remoteVersion > record.InstalledVersion)
            {
                foreach (var source in installSources)
                {
                    var button = new MyButton { Text = GetActionLabel("更新", source), Height = 28, MinWidth = 72, Margin = new Thickness(actionRow.Children.Count > 1 ? 8 : 0, 0, 0, 0), ColorType = MyButton.ColorState.Highlight };
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

        actionRow.VerticalAlignment = VerticalAlignment.Top;
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
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold }
        };
        badge.SetResourceReference(Border.BackgroundProperty, backgroundResource);
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, foregroundResource);
        return badge;
    }

    private static WrapPanel CreateTagRow(PluginRepositoryEntry entry)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (!string.IsNullOrWhiteSpace(entry.ReviewedBy)) row.Children.Add(CreateBadge("已审核", "ColorBrushGray7", "ColorBrushGray2"));
        foreach (var cap in entry.Capabilities.Take(3)) row.Children.Add(CreateBadge(cap.ToString(), "ColorBrush8", "ColorBrush3"));
        if (entry.Capabilities.Length > 3) row.Children.Add(CreateBadge("+" + (entry.Capabilities.Length - 3), "ColorBrushGray7", "ColorBrushGray2"));
        return row;
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
            using var prepared = await PluginRemoteInstallService.PrepareAsync(sourceEntry);
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

    private void Search()
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
