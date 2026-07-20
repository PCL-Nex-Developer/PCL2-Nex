using System;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Plugins;

namespace PCL;

internal static class PluginRepositoryListUi
{
    public static void BuildRepoList(StackPanel repoList)
    {
        repoList.Children.Clear();
        repoList.Children.Add(CreateRepoRow("GitHub", "pclnexplugin", "内置 Topic", true, repoList));
        repoList.Children.Add(CreateRepoRow("NexDeveloper", PluginRepositoryService.GetOfficialIndexUrl(), "内置第三方 JSON", true, repoList));
        var records = PluginTrustService.GetAllTrustRecords();
        if (records.Count == 0)
        {
            var hint = new TextBlock { Text = "暂无自定义插件源。可添加 Topic 关键词、Manifest 或本地/网络 JSON。", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            repoList.Children.Add(hint);
            return;
        }
        foreach (var record in records)
        {
            var name = record.SourceKind == PluginRepositorySourceKind.Topic
                ? record.RepoUrl
                : record.RepoName;
            var description = record.SourceKind switch
            {
                PluginRepositorySourceKind.Topic => "Topic",
                PluginRepositorySourceKind.Manifest => "Manifest",
                _ => "第三方 JSON"
            };
            repoList.Children.Add(CreateRepoRow(name, record.RepoUrl, description, false, repoList, record.Enabled));
        }
    }

    private static Grid CreateRepoRow(
        string name,
        string url,
        string description,
        bool isBuiltIn,
        StackPanel repoList,
        bool enabled = true)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        var titleText = new TextBlock
        {
            Text = name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "ColorBrush2" : "ColorBrushGray4");
        info.Children.Add(titleText);
        var detailText = new TextBlock
        {
            Text = description + (isBuiltIn ? string.Empty : " · " + url),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
        info.Children.Add(detailText);
        row.Children.Add(info);
        if (!isBuiltIn)
        {
            var toggleBtn = new MyButton { Text = enabled ? "禁用" : "启用", Height = 26, MinWidth = 50, Margin = new Thickness(8, 0, 0, 0) };
            toggleBtn.SetValue(Grid.ColumnProperty, 1);
            toggleBtn.Click += (_, _) => { PluginTrustService.SetRepositoryEnabled(url, !enabled); BuildRepoList(repoList); };
            row.Children.Add(toggleBtn);
            var removeBtn = new MyButton { Text = "移除", Height = 26, MinWidth = 50, Margin = new Thickness(4, 0, 0, 0), ColorType = MyButton.ColorState.Red };
            removeBtn.SetValue(Grid.ColumnProperty, 2);
            removeBtn.Click += (_, _) => { if (ModMain.MyMsgBox("确定移除仓库 " + name + "？", "确认", button2: "取消", isWarn: true) == 1) { PluginTrustService.RemoveTrust(url); BuildRepoList(repoList); } };
            row.Children.Add(removeBtn);
        }
        return row;
    }

    public static void ShowAddRepoDialog(StackPanel repoList)
    {
        try
        {
            var input = ModMain.MyMsgBoxInput("添加插件源", "请输入来源：\n- topic:pclnexplugin 或直接输入 Topic 关键词\n- manifest:https://example.com/manifest.json\n- 网络或本地 JSON 文件地址");
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();
            var sourceKind = PluginRepositorySourceKind.Json;
            var url = input;
            if (input.StartsWith("topic:", StringComparison.OrdinalIgnoreCase))
            {
                sourceKind = PluginRepositorySourceKind.Topic;
                url = input[6..].Trim();
            }
            else if (input.StartsWith("manifest:", StringComparison.OrdinalIgnoreCase))
            {
                sourceKind = PluginRepositorySourceKind.Manifest;
                url = input[9..].Trim();
            }
            else if (!Uri.TryCreate(input, UriKind.Absolute, out _) && !System.IO.File.Exists(input))
            {
                sourceKind = PluginRepositorySourceKind.Topic;
            }
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("插件源内容不能为空。");
            var name = sourceKind == PluginRepositorySourceKind.Topic
                ? url
                : ModMain.MyMsgBoxInput("插件源名称", "请输入此来源在插件商店中显示的名称：", "自定义插件源");
            if (string.IsNullOrWhiteSpace(name)) name = sourceKind == PluginRepositorySourceKind.Topic ? url : "自定义插件源";
            PluginTrustService.AddTrust(url, name, PluginRepositorySourceType.Custom, sourceKind);
            BuildRepoList(repoList);
        }
        catch (Exception ex) { ModMain.MyMsgBox("添加失败: " + ex.Message, "错误"); }
    }
}
