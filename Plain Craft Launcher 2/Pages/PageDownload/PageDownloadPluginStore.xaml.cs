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
    private IReadOnlyList<PluginRepositoryEntry>? _allEntries;
    // 已安装插件的实时最新版本（从 manifestUrl fetch），用于显示更新按钮
    private Dictionary<string, Version> _latestVersions = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ModLoader.LoaderCombo<int> storeLoader = new("PluginStore", [
        new ModLoader.LoaderTask<int, int>("获取插件列表", _ => LoadStoreData()) { ProgressWeight = 0.6d },
        new ModLoader.LoaderTask<int, int>("检测插件更新", _ => FetchLatestVersionsData()) { ProgressWeight = 0.4d }
    ]);

    public PageDownloadPluginStore()
    {
        InitializeComponent();
        Loaded += (_, _) => PanBack.ScrollToHome();
        PageLoaderInit(Load, PanLoad, CardPlugins, null, storeLoader, _ => Load_OnFinish());
        PanSearchBox.Search += (_, _) => Search();
        PanSearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };
    }

    private static IReadOnlyList<PluginRepositoryEntry>? _storeEntries;
    private static Dictionary<string, Version> _storeLatestVersions = new(StringComparer.OrdinalIgnoreCase);

    private static void LoadStoreData()
    {
        var indexes = PluginUpdateService.FetchEnabledIndexesAsync().GetAwaiter().GetResult();
        _storeEntries = PluginRepositoryService.MergeIndexes(indexes);
    }

    private static void FetchLatestVersionsData()
    {
        var entries = _storeEntries ?? [];
        Dictionary<string, PluginInstallRecord> installed;
        try { installed = PluginInstallService.GetInstalledPlugins().ToDictionary(r => r.PluginId, r => r, StringComparer.OrdinalIgnoreCase); }
        catch { return; }

        _storeLatestVersions.Clear();
        var toCheck = entries.Where(e => installed.ContainsKey(e.Id)).ToList();
        var tasks = toCheck.Select(async entry =>
        {
            var version = await PluginUpdateService.FetchLatestVersionAsync(entry).ConfigureAwait(false);
            if (version is not null)
                lock (_storeLatestVersions)
                    _storeLatestVersions[entry.Id] = version;
        });
        System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    private void Load_OnFinish()
    {
        _allEntries = _storeEntries;
        _latestVersions = new Dictionary<string, Version>(_storeLatestVersions, StringComparer.OrdinalIgnoreCase);
        RenderPluginList(_allEntries ?? []);
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
            Padding = new Thickness(0, 6, 0, 6),
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1)
        };
        row.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray6");

        installed.TryGetValue(entry.Id, out var record);

        // 判断是否有可用更新：优先用实时版本，fallback 到静态 version
        Version? remoteVersion = null;
        var hasUpdate = false;
        if (record is not null)
        {
            if (_latestVersions.TryGetValue(entry.Id, out var live))
            {
                remoteVersion = live;
                hasUpdate = live > record.InstalledVersion;
            }
            else if (PluginUpdateService.TryGetDisplayVersion(entry, out var staticVer))
            {
                remoteVersion = staticVer;
                hasUpdate = staticVer > record.InstalledVersion;
            }
        }
        else if (_latestVersions.TryGetValue(entry.Id, out var live))
        {
            remoteVersion = live;
        }
        else
        {
            PluginUpdateService.TryGetDisplayVersion(entry, out remoteVersion);
        }

        var displayVersion = remoteVersion ?? new Version(0, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ── 左侧：标题 + 描述 ──
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleBar = new DockPanel { LastChildFill = true };
        var title = new TextBlock
        {
            Text = entry.Name,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
        titleBar.Children.Add(title);
        left.Children.Add(titleBar);

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            var desc = new TextBlock
            {
                Text = entry.Description,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            left.Children.Add(desc);
        }

        var tags = CreateTagRow(entry);
        if (tags.Children.Count > 0)
        {
            tags.Margin = new Thickness(0, 5, 0, 0);
            left.Children.Add(tags);
        }
        grid.Children.Add(left);

        // ── 右侧：发布者/版本 Tag + 操作按钮（单行紧凑） ──
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        right.SetValue(Grid.ColumnProperty, 1);

        // 发布者 Tag
        if (!string.IsNullOrWhiteSpace(entry.Author))
            right.Children.Add(CreateBadge(entry.Author!, "ColorBrushGray7", "ColorBrushGray2"));

        // 版本 Tag：有更新显示 old -> new，否则直接版本
        var versionText = record is not null && hasUpdate
            ? record.InstalledVersion + " -> " + displayVersion
            : (displayVersion != new Version(0, 0) ? "v" + displayVersion : "v?");
        right.Children.Add(CreateBadge(versionText, hasUpdate ? "ColorBrush8" : "ColorBrushGray7", hasUpdate ? "ColorBrush2" : "ColorBrushGray2"));

        // 操作按钮
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var installSources = PluginRepositoryService.GetInstallSources(entry).ToList();

        if (record is not null)
        {
            if (hasUpdate)
            {
                foreach (var source in installSources)
                {
                    var button = new MyButton
                    {
                        Text = GetActionLabel("更新", source),
                        Height = 30,
                        MinWidth = 72,
                        Margin = new Thickness(actionRow.Children.Count > 0 ? 6 : 0, 0, 0, 0),
                        ColorType = MyButton.ColorState.Highlight
                    };
                    button.Click += (_, _) => UpdatePlugin(new PluginUpdateCandidate
                    {
                        Installed = record,
                        Entry = entry,
                        Source = source,
                        LatestVersion = remoteVersion!
                    });
                    actionRow.Children.Add(button);
                }
            }
        }
        else
        {
            foreach (var source in installSources)
            {
                var button = new MyButton
                {
                    Text = GetActionLabel("安装", source),
                    Height = 30,
                    MinWidth = 72,
                    Margin = new Thickness(actionRow.Children.Count > 0 ? 6 : 0, 0, 0, 0),
                    ColorType = MyButton.ColorState.Highlight
                };
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
            var linkButton = new MyButton { Text = "主页", Height = 30, MinWidth = 50, Margin = new Thickness(6, 0, 0, 0) };
            var url = entry.HomepageUrl;
            linkButton.Click += (_, _) => ModBase.OpenWebsite(url);
            actionRow.Children.Add(linkButton);
        }

        right.Children.Add(actionRow);
        grid.Children.Add(right);

        row.Child = grid;
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
            PageLoaderRestart();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] Store install failed: " + entry.Id, ModBase.LogLevel.Debug);
            ModMain.MyMsgBox("安装失败: " + ex.Message, "错误");
        }
    }

    private async void UpdatePlugin(PluginUpdateCandidate candidate)
    {
        try
        {
            var trustDecision = PluginUpdateService.EvaluateUpdate(candidate);
            var confirmMsg = "即将更新插件：\n\n名称: " + candidate.Entry.Name + "\n当前版本: v" + candidate.Installed.InstalledVersion + "\n最新版本: v" + candidate.LatestVersion + "\n下载源: " + candidate.Source.Url;
            if (trustDecision == PluginTrustDecision.RequireReconfirm)
                confirmMsg += "\n\n该更新涉及来源变化或能力变化，请确认你信任此版本。";

            if (ModMain.MyMsgBox(confirmMsg, "确认更新", button2: "取消", isWarn: trustDecision != PluginTrustDecision.Allow) != 1) return;

            ModMain.MyMsgBox("正在获取插件更新包，请稍候...", "更新中");
            using var prepared = await PluginRemoteInstallService.PrepareAsync(candidate.Source);
            await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, prepared.Manifest, prepared.SourceType, prepared.SourceUrl);
            ModMain.frmMain?.RefreshRestartButton(true);

            ModMain.MyMsgBox("插件 " + prepared.Manifest.Name + " 已更新到 v" + prepared.Manifest.Version + "！\n重启启动器后生效。", "更新完成");
            PageLoaderRestart();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Plugins] Store update failed: " + candidate.Entry.Id, ModBase.LogLevel.Debug);
            ModMain.MyMsgBox("更新失败: " + ex.Message, "错误");
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
