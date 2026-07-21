using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Localization;
using PCL.Core.App.Plugins;

namespace PCL;

internal static class PluginRepositoryListUi
{
    public static void BuildRepoList(StackPanel repoList)
    {
        repoList.Children.Clear();
        repoList.Children.Add(CreateRepoRow("GitHub", "pclnexplugin", Lang.Text("Plugins.Repository.BuiltInTopic"), true, repoList));
        repoList.Children.Add(CreateRepoRow("NexDeveloper", PluginRepositoryService.GetOfficialIndexUrl(), Lang.Text("Plugins.Repository.BuiltInThirdPartyJson"), true, repoList));
        var records = PluginTrustService.GetAllTrustRecords()
            .Where(record => record.SourceKind != PluginRepositorySourceKind.Topic)
            .ToList();
        if (records.Count == 0)
        {
            var hint = new TextBlock { Text = Lang.Text("Plugins.Repository.NoCustomSourceHint"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            repoList.Children.Add(hint);
            return;
        }
        foreach (var record in records)
        {
            var name = record.RepoName;
            var description = record.SourceKind switch
            {
                PluginRepositorySourceKind.Manifest => Lang.Text("Plugins.Repository.SourceType.Manifest"),
                _ => Lang.Text("Plugins.Repository.SourceType.ThirdPartyJson")
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
            var toggleBtn = new MyButton { Text = enabled ? Lang.Text("Common.Action.Disable") : Lang.Text("Common.Action.Enable"), Height = 26, MinWidth = 50, Margin = new Thickness(8, 0, 0, 0) };
            toggleBtn.SetValue(Grid.ColumnProperty, 1);
            toggleBtn.Click += (_, _) => { PluginTrustService.SetRepositoryEnabled(url, !enabled); BuildRepoList(repoList); };
            row.Children.Add(toggleBtn);
            var removeBtn = new MyButton { Text = Lang.Text("Common.Action.Remove"), Height = 26, MinWidth = 50, Margin = new Thickness(4, 0, 0, 0), ColorType = MyButton.ColorState.Red };
            removeBtn.SetValue(Grid.ColumnProperty, 2);
            removeBtn.Click += (_, _) => { if (ModMain.MyMsgBox(Lang.Text("Plugins.Repository.RemoveConfirmMessage", name), Lang.Text("Common.Action.Confirm"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) == 1) { PluginTrustService.RemoveTrust(url); BuildRepoList(repoList); } };
            row.Children.Add(removeBtn);
        }
        return row;
    }

    public static void ShowAddRepoDialog(StackPanel repoList)
    {
        try
        {
            var input = ModMain.MyMsgBoxInput(Lang.Text("Plugins.Repository.AddDialog.Title"), Lang.Text("Plugins.Repository.AddDialog.Message"));
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();
            var sourceKind = PluginRepositorySourceKind.Json;
            var url = input;
            if (input.StartsWith("manifest:", StringComparison.OrdinalIgnoreCase))
            {
                sourceKind = PluginRepositorySourceKind.Manifest;
                url = input[9..].Trim();
            }
            else if (input.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
            {
                url = input[5..].Trim();
            }
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException(Lang.Text("Plugins.Repository.AddDialog.EmptyContent"));
            if (sourceKind == PluginRepositorySourceKind.Manifest)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var manifestUri)
                    || manifestUri.Scheme is not ("http" or "https"))
                    throw new ArgumentException(Lang.Text("Plugins.Repository.AddDialog.InvalidManifest"));
            }
            else if (File.Exists(url))
            {
                if (!url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(Lang.Text("Plugins.Repository.AddDialog.InvalidJson"));
                url = Path.GetFullPath(url);
            }
            else if (!Uri.TryCreate(url, UriKind.Absolute, out var jsonUri)
                     || jsonUri.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException(Lang.Text("Plugins.Repository.AddDialog.InvalidJson"));
            }
            var name = ModMain.MyMsgBoxInput(Lang.Text("Plugins.Repository.NameDialog.Title"), Lang.Text("Plugins.Repository.NameDialog.Message"), Lang.Text("Plugins.Repository.DefaultName"));
            if (string.IsNullOrWhiteSpace(name)) name = Lang.Text("Plugins.Repository.DefaultName");
            PluginTrustService.AddTrust(url, name, PluginRepositorySourceType.Custom, sourceKind);
            BuildRepoList(repoList);
        }
        catch (Exception ex) { ModMain.MyMsgBox(Lang.Text("Plugins.Repository.AddFailed", ex.Message), Lang.Text("Common.Action.Confirm")); }
    }
}
