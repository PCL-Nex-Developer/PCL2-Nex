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
        repoList.Children.Add(CreateRepoRow("官方市场", PluginRepositoryService.GetOfficialIndexUrl(), PluginRepositorySourceType.Official, true, true, repoList));
        var records = PluginTrustService.GetAllTrustRecords();
        if (records.Count == 0)
        {
            var hint = new TextBlock { Text = "暂无自定义插件源。点击上方「添加源」按钮添加第三方市场注册表。", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            repoList.Children.Add(hint);
            return;
        }
        foreach (var record in records) repoList.Children.Add(CreateRepoRow(record.RepoName, record.RepoUrl, record.SourceType, record.Enabled, false, repoList));
    }

    private static Grid CreateRepoRow(string name, string url, PluginRepositorySourceType sourceType, bool enabled, bool isOfficial, StackPanel repoList)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        var titleText = new TextBlock { Text = name + "  " + (sourceType == PluginRepositorySourceType.Official ? "[Official]" : "[Custom]"), FontSize = 12, FontWeight = FontWeights.SemiBold };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "ColorBrush2" : "ColorBrushGray4");
        info.Children.Add(titleText);
        if (!isOfficial)
        {
            var urlText = new TextBlock { Text = url, FontSize = 11 };
            urlText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            info.Children.Add(urlText);
        }
        row.Children.Add(info);
        if (!isOfficial)
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
            var url = ModMain.MyMsgBoxInput("添加插件源", "请输入插件市场索引 URL：\n（应指向一个 index.json 或 registry.json 文件）");
            if (string.IsNullOrWhiteSpace(url)) return;
            var name = ModMain.MyMsgBoxInput("插件源名称", "请输入插件源名称（便于识别）：", "自定义插件源");
            if (string.IsNullOrWhiteSpace(name)) name = "自定义插件源";
            PluginTrustService.AddTrust(url, name, PluginRepositorySourceType.Custom);
            BuildRepoList(repoList);
        }
        catch (Exception ex) { ModMain.MyMsgBox("添加失败: " + ex.Message, "错误"); }
    }
}
