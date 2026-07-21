using System.Windows;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class ServerCard
{
    public MinecraftServerInfo server;

    public ServerCard()
    {
        InitializeComponent();
    }

    public event EventHandler? RemoveServer;
    public event EventHandler? EditServer;

    private void BtnSkin_Click(object sender, EventArgs eventArgs)
    {
        BtnSetting.ContextMenu.IsOpen = true;
    }

    public void UpdateServerInfo(MinecraftServerInfo serverInfo)
    {
        server = serverInfo;
        ModBase.RunInUi(UpdateServerUi);
    }

    private async void UpdateServerUi()
    {
        if (server is null) return;
        ServerName.Text = server.Name;
        ServerAddress.Text = server.Address;
        await ImageLoaderHelper.SetServerLogoAsync(server.Icon, ServerIcon);
    }

    private void BtnConnect_Click(object sender, EventArgs e)
    {
        try
        {
            var launchOptions = new ModLaunch.McLaunchOptions
            {
                ServerIp = server.Address,
                instance = PageInstanceLeft.McInstance
            };
            ModLaunch.McLaunchStart(launchOptions);
            ModMain.frmMain.PageChange(new FormMain.PageStackData { page = FormMain.PageType.Launch });
            HintService.Hint(Lang.Text("Instance.Server.Card.ConnectingTo", server.Name));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Server.Card.LaunchFailed"), ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Server.Card.LaunchFailedMsg", ex.Message), HintType.Error);
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(server.Address);
            HintService.Hint(Lang.Text("Instance.Server.Card.AddressCopied", server.Address), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Server.Card.CopyAddressFailed"));
            HintService.Hint(Lang.Text("Instance.Server.Card.CopyAddressFailed"), HintType.Error);
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = PageInstanceServer.GetServerInfo(server);
            if (!result.Success) return;
            EditServer?.Invoke(this, new ResultEventArgs(result.Name, result.Address));
        }
        catch (Exception ex)
        {
            HintService.Hint(Lang.Text("Instance.Server.Card.EditFailed", ex.Message), HintType.Error);
        }
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ModMain.MyMsgBox(
                Lang.Text("Instance.Server.Card.RemoveConfirmMessage", server.Name, server.Address),
                Lang.Text("Instance.Server.Card.RemoveConfirmTitle"), Lang.Text("Common.Action.Confirm"),
                Lang.Text("Common.Action.Cancel")) == 1)
            RemoveServer?.Invoke(this, EventArgs.Empty);
    }

    public class ResultEventArgs : EventArgs
    {
        public ResultEventArgs(string param1, string param2)
        {
            Param1 = param1;
            Param2 = param2;
        }

        public string Param1 { get; set; }
        public string Param2 { get; set; }
    }
}
