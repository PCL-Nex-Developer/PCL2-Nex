using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Plugins;

namespace PCL;

public partial class PageToolsLeft
{
    private bool isLoad;
    private bool isPageSwitched; // 如果在 Loaded 前切换到其他页面，会导致触发 Loaded 时再次切换一次
    private readonly Dictionary<int, UiExtensionEntry> _pluginEntries = new();
    private int _nextPluginTag = 1000;

    public PageToolsLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += PageLinkLeft_Loaded;
        Unloaded += PageOtherLeft_Unloaded;
        PluginHostBootstrap.UiExtensions.Changed += (_, _) => ModBase.RunInUi(() =>
        {
            RebuildPluginEntries();
            PageSetupUI.HiddenRefresh();
        });
    }

    private void PageLinkLeft_Loaded(object sender, RoutedEventArgs e)
    {
        RebuildPluginEntries();
        var isHiddenPage = false;
        var hide = Config.Preference.Hide;

        if (ItemTest.Checked && hide.ToolsTest) isHiddenPage = true;
        if (PageSetupUI.HiddenForceShow)
            isHiddenPage = false;
        // 若页面错误，或尚未加载，则继续
        if (isLoad && !isHiddenPage)
            return;
        isLoad = true;
        // 刷新子页面隐藏情况
        PageSetupUI.HiddenRefresh();
        // 选择第一个未被禁用的子页面
        if (isPageSwitched) 
            return;
        ItemTest.SetChecked(true, false, false);
    }

    private void RebuildPluginEntries()
    {
        var toRemove = new List<UIElement>();
        foreach (UIElement child in PanItem.Children)
        {
            if (child is MyListItem item && item.Tag is double tag && tag >= 1000)
                toRemove.Add(child);
            if (child is TextBlock { Tag: "PluginToolsGroup" })
                toRemove.Add(child);
        }
        foreach (var child in toRemove) PanItem.Children.Remove(child);

        _pluginEntries.Clear();
        _nextPluginTag = 1000;

        var pages = PluginHostBootstrap.UiExtensions.GetTools();
        if (pages.Count == 0) return;

        string? currentGroup = null;
        foreach (var entry in pages)
        {
            var group = string.IsNullOrWhiteSpace(entry.Group) ? null : entry.Group;
            if (!string.Equals(currentGroup, group, StringComparison.Ordinal))
            {
                currentGroup = group;
                if (group is not null)
                    InsertPluginElement(new TextBlock
                    {
                        Tag = "PluginToolsGroup",
                        Text = group,
                        Margin = new Thickness(13, 5, 5, 3),
                        Opacity = 0.6,
                        FontSize = 12
                    });
            }

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
            InsertPluginElement(item);
        }
    }

    private void InsertPluginElement(UIElement element)
    {
        var index = PanItem.Children.IndexOf(TextToolsCategory);
        if (index < 0) PanItem.Children.Add(element);
        else PanItem.Children.Insert(index, element);
    }

    private void SelectFirstPluginEntry()
    {
        foreach (UIElement child in PanItem.Children)
        {
            if (child is MyListItem { Tag: double tag } item && tag >= 1000)
            {
                item.SetChecked(true, false, false);
                return;
            }
        }
    }

    private void PageOtherLeft_Unloaded(object sender, RoutedEventArgs e)
    {
        isPageSwitched = false;
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。
    /// </summary>
    public FormMain.PageSubType pageID = Config.Preference.Hide.ToolsTest
        ? FormMain.PageSubType.ToolsTest
        : FormMain.PageSubType.ToolsTest;

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (MyListItem)senderRaw;
        // 尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
        // 若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        if (sender.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(sender.Tag));
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var targetID = id ?? pageID;
        switch (targetID)
        {
            case FormMain.PageSubType.ToolsTest:
            {
                if (ModMain.frmToolsTest is null)
                    ModMain.frmToolsTest = new PageToolsTest();
                return ModMain.frmToolsTest;
            }
            default:
            {
                var tagVal = (int)targetID;
                if (tagVal >= 1000 && _pluginEntries.TryGetValue(tagVal, out var entry))
                {
                    try
                    {
                        var page = (FrameworkElement)entry.Factory.DynamicInvoke()!;
                        return WrapPluginPage(page, entry.Title);
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(ex, "[Tools] 创建插件扩展页面失败: " + entry.PluginId, ModBase.LogLevel.Debug);
                        throw;
                    }
                }
                throw new Exception("未知的工具子页面种类：" + (int)targetID);
            }
        }
    }

    private static MyPageRight WrapPluginPage(FrameworkElement content, string title)
    {
        if (content is MyPageRight pageRight) return pageRight;

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

    /// <summary>
    ///     切换现有页面。
    /// </summary>
    public void PageChange(FormMain.PageSubType id)
    {
        if (pageID == id)
            return;
        ModAnimation.AniControlEnabled += 1;
        isPageSwitched = true;
        try
        {
            PageChangeRun((MyPageRight)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "切换分页面失败（ID " + (int)id + "）", ModBase.LogLevel.Feedback);
        }
        finally
        {
            ModAnimation.AniControlEnabled -= 1;
        }
    }

    private static void PageChangeRun(MyPageRight target)
    {
        ModAnimation.AniStop("FrmMain PageChangeRight"); // 停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
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
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                ModMain.frmMain.pageRight.Opacity = 1d;
                ModMain.frmMain.pageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
    }

    #endregion
}
