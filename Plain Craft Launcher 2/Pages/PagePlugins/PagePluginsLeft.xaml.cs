using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Plugins;

namespace PCL;

public partial class PagePluginsLeft
{
    public PagePluginsLeft()
    {
        InitializeComponent();
        pageID = (FormMain.PageSubType)1; // Installed
        AnimatedControl = PanItem;
        Loaded += (_, _) =>
        {
            RebuildPluginEntries();
            if (!isPageSwitched)
                ItemInstalled.SetChecked(true, false, false);
        };
        Unloaded += (_, _) => isPageSwitched = false;
    }

    private bool isPageSwitched;

    public FormMain.PageSubType pageID;

    /// <summary>
    /// 插件动态注册的侧边栏条目与对应页面映射。
    /// Key: 侧边栏 Tag 值（从 1000 开始）。
    /// </summary>
    private readonly Dictionary<int, UiExtensionEntry> _pluginEntries = new();
    private int _nextPluginTag = 1000;

    private void RebuildPluginEntries()
    {
        // 移除旧的插件动态条目
        var toRemove = new List<UIElement>();
        foreach (UIElement child in PanItem.Children)
        {
            if (child is MyListItem item && item.Tag is double tag && tag >= 1000)
                toRemove.Add(child);
        }
        foreach (var child in toRemove) PanItem.Children.Remove(child);

        _pluginEntries.Clear();
        _nextPluginTag = 1000;

        var pages = PluginHostBootstrap.UiExtensions.GetPluginPages();
        if (pages.Count == 0) return;

        // 添加分隔标题
        var header = new TextBlock
        {
            Text = "插件扩展",
            Margin = new Thickness(13, 5, 5, 3),
            Opacity = 0.6,
            FontSize = 12
        };
        // 找到已安装插件条目后面插入
        var installedIndex = -1;
        for (int i = 0; i < PanItem.Children.Count; i++)
        {
            if (PanItem.Children[i] is MyListItem li && li.Name == "ItemInstalled")
            { installedIndex = i; break; }
        }
        var insertAt = installedIndex >= 0 ? installedIndex + 1 : PanItem.Children.Count;
        PanItem.Children.Insert(insertAt, header);

        foreach (var entry in pages)
        {
            var tag = _nextPluginTag++;
            _pluginEntries[tag] = entry;

            var item = new MyListItem
            {
                IsScaleAnimationEnabled = false,
                Type = MyListItem.CheckType.RadioBox,
                Tag = (double)tag,
                MinPaddingRight = 35,
                Height = 36,
                VerticalAlignment = VerticalAlignment.Top,
                Title = entry.Title,
                LogoScale = 0.9,
                SvgIcon = string.IsNullOrWhiteSpace(entry.Icon) ? "lucide/puzzle" : entry.Icon
            };
            item.Check += PageCheck;
            PanItem.Children.Insert(insertAt + 1 + (_pluginEntries.Count - 1), item);
        }
    }

    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (MyListItem)senderRaw;
        if (sender.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(sender.Tag));
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var target = id ?? pageID;
        var tagVal = (int)target;

        switch (tagVal)
        {
            case 1: // Installed
            {
                if (ModMain.frmPluginsInstalled is null)
                    ModMain.frmPluginsInstalled = new PagePluginsInstalled();
                return ModMain.frmPluginsInstalled;
            }
            default:
            {
                // 动态插件页面
                if (tagVal >= 1000 && _pluginEntries.TryGetValue(tagVal, out var entry))
                {
                    try
                    {
                        var page = (UserControl)entry.Factory.DynamicInvoke()!;
                        return _WrapAsPageRight(page, entry.Title);
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(ex, "[Plugins] 创建插件页面失败: " + entry.PluginId, ModBase.LogLevel.Debug);
                        throw;
                    }
                }
                throw new Exception("未知的插件子页面种类：" + tagVal);
            }
        }
    }

    private static MyPageRight _WrapAsPageRight(UserControl content, string title)
    {
        var scrollViewer = new MyScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Name = "PanBack"
        };
        var stackPanel = new StackPanel { Margin = new Thickness(25, 10, 25, 10), Name = "PanMain" };
        var card = new MyCard { Title = title, Margin = new Thickness(0, 15, 0, 15) };
        content.Margin = new Thickness(25, 40, 25, 15);
        card.Children.Add(content);
        stackPanel.Children.Add(card);
        scrollViewer.Content = stackPanel;

        var page = new MyPageRight();
        page.Child = scrollViewer;
        return page;
    }

    public void PageChange(FormMain.PageSubType id)
    {
        if (pageID == id) return;
        ModAnimation.AniControlEnabled += 1;
        isPageSwitched = true;
        try
        {
            PageChangeRun((MyPageRight)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "切换插件子页面失败（ID " + (int)id + "）", ModBase.LogLevel.Feedback);
        }
        finally
        {
            ModAnimation.AniControlEnabled -= 1;
        }
    }

    private static void PageChangeRun(MyPageRight target)
    {
        ModAnimation.AniStop("FrmMain PageChangeRight");
        if (target.Parent is not null)
            target.SetValue(ContentPresenter.ContentProperty, null);
        ModMain.frmMain.pageRight = target;
        ((MyPageRight)ModMain.frmMain.PanMainRight.Child).PageOnExit();
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                ((MyPageRight)ModMain.frmMain.PanMainRight.Child).PageOnForceExit();
                ModMain.frmMain.PanMainRight.Child = ModMain.frmMain.pageRight;
                ModMain.frmMain.pageRight.Opacity = 0d;
            }, 130),
            ModAnimation.AaCode(() =>
            {
                ModMain.frmMain.pageRight.Opacity = 1d;
                ModMain.frmMain.pageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
    }
}
