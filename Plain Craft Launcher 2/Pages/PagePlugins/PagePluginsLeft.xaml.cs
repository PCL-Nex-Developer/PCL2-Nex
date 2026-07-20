using System.Windows.Controls;

namespace PCL;

public partial class PagePluginsLeft
{
    private bool isPageSwitched;

    public PagePluginsLeft()
    {
        InitializeComponent();
        pageID = (FormMain.PageSubType)1;
        AnimatedControl = PanItem;
        Loaded += (_, _) =>
        {
            if (!isPageSwitched)
                ItemInstalled.SetChecked(true, false, false);
        };
        Unloaded += (_, _) => isPageSwitched = false;
    }

    public FormMain.PageSubType pageID;

    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (MyListItem)senderRaw;
        if (sender.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(sender.Tag));
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var target = id ?? pageID;
        if ((int)target != 1)
            throw new Exception("未知的插件子页面种类：" + (int)target);

        ModMain.frmPluginsInstalled ??= new PagePluginsInstalled();
        return ModMain.frmPluginsInstalled;
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
