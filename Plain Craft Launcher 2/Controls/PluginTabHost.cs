using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UiExtensionEntry = PCL.Plugins.UiExtensionEntry;

namespace PCL;

/// <summary>
/// 插件面板宿主控件。<br/>
/// 接收一组 <see cref="UiExtensionEntry"/>，以 MyCard 卡片形式垂直排列。
/// 处理空状态与单独插件面板创建失败的错误隔离。
/// </summary>
public class PluginTabHost : UserControl
{
    public void BuildTabs(IReadOnlyList<UiExtensionEntry> entries, string emptyMessage)
    {
        Content = null;

        if (entries is null || entries.Count == 0)
        {
            Content = _EmptyCard(emptyMessage ?? "当前没有可用插件面板");
            return;
        }

        var isFirst = true;
        var panel = new StackPanel();
        foreach (var entry in entries)
        {
            var card = new MyCard
            {
                Title = entry.Title ?? entry.PluginId,
                Margin = new Thickness(0, isFirst ? 15 : 0, 0, 15)
            };
            isFirst = false;

            try
            {
                var control = entry.CreateControl();
                if (control is not null)
                {
                    DetachFromParent(control);
                    control.Margin = new Thickness(25, 40, 25, 15);
                    card.Children.Add(control);
                }
                else
                {
                    card.Children.Add(_ErrorContent(entry.PluginId, "插件面板工厂返回了 null"));
                }
            }
            catch (Exception ex)
            {
                card.Children.Add(_ErrorContent(entry.PluginId, ex.Message));
                try { ModBase.Log(ex, $"[Plugins] 创建插件面板失败: {entry.PluginId}", ModBase.LogLevel.Normal); }
                catch { /* 日志服务不可用时忽略 */ }
            }

            panel.Children.Add(card);
        }

        Content = panel;
    }

    private static void DetachFromParent(FrameworkElement control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, control):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, control):
                contentControl.Content = null;
                break;
            case ItemsControl itemsControl:
                itemsControl.Items.Remove(control);
                break;
        }
    }

    private static MyCard _EmptyCard(string message)
    {
        var card = new MyCard
        {
            Title = "插件",
            Margin = new Thickness(0, 15, 0, 0)
        };
        var text = new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(25, 40, 25, 20)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
        card.Children.Add(text);
        return card;
    }

    private static FrameworkElement _ErrorContent(string pluginId, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(25, 40, 25, 15) };

        var title = new TextBlock
        {
            Text = $"插件面板加载失败: {pluginId}",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedLight");
        panel.Children.Add(title);

        var detailText = new TextBlock
        {
            Text = $"详细信息: {detail}",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
        panel.Children.Add(detailText);

        var hint = new TextBlock
        {
            Text = "请查看日志获取更多信息（设置 → 查看日志 → 导出日志）。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
        panel.Children.Add(hint);

        return panel;
    }
}
