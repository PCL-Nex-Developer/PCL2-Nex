using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Plugin.Abstractions;
using PCL.Plugins;

namespace PCL;

public partial class PageSetupGameLink
{
    private new bool isLoaded;

    private static ILobbyService? Lobby =>
        PluginHostBootstrap.Extensions.GetDefault<ILobbyService>(PluginExtensionPoints.LobbyService);

    public PageSetupGameLink()
    {
        InitializeComponent();
        TextUdpNatType.Text = Lang.Text("Setup.GameLink.NetworkTest.UdpNatType", Lang.Text("Setup.GameLink.NetworkTest.NotTested"));
        TextTcpNatType.Text = Lang.Text("Setup.GameLink.NetworkTest.TcpNatType", Lang.Text("Setup.GameLink.NetworkTest.NotTested"));
        TextIpv6Status.Text = Lang.Text("Setup.GameLink.NetworkTest.Ipv6Status", Lang.Text("Setup.GameLink.NetworkTest.NotTested"));
        Loaded += PageSetupLink_Loaded;
        Loaded += (_, _) => Reload();
    }

    private void PageSetupLink_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();

        // 非重复加载部分
        if (isLoaded)
            return;
        isLoaded = true;

        ModAnimation.AniControlEnabled += 1;
        Reload();
        ModAnimation.AniControlEnabled -= 1;
    }

    public void Reload()
    {
        var settings = Lobby?.Settings ?? new LobbySettingsSnapshot();
        TextLinkUsername.Text = settings.Username;
        CheckLatencyFirstMode.Checked = settings.UseLatencyFirstMode;
        ComboPreferProtocol.SelectedIndex = (int)settings.ProtocolPreference;
        CheckTryPunchSym.Checked = settings.TryPunchSymmetricNat;
        CheckEnableIPv6.Checked = settings.EnableIPv6;
        CheckEnableCliOutput.Checked = settings.EnableDebugOutput;

        // TextRelays.Text = "正在获取信息..."
        // Do While Not (PageLinkLobby.LobbyAnnouncementLoader.State = LoadState.Finished OrElse PageLinkLobby.LobbyAnnouncementLoader.State = LoadState.Failed)
        // Thread.Sleep(500)
        // Loop
    }

    // 初始化
    public void Reset()
    {
        try
        {
            Lobby?.ResetSettings();
            ModBase.Log("[Setup] 已初始化联机页设置");
            HintService.Hint(Lang.Text("Setup.GameLink.Initialized"), HintType.Success, false);
            Reload();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Setup.GameLink.Error.InitFailed"), ModBase.LogLevel.Msgbox);
        }

        Reload();
    }

    // 将控件改变路由到设置改变
    private void TextBoxChange(object senderRaw, TextChangedEventArgs e)
    {
        var sender = (MyTextBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Text);
    }

    private static void ComboBoxChange(MyComboBox sender, object e)
    {
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.SelectedIndex);
    }

    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (MyCheckBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Checked);
    }

    private static void SetByTag(string tag, object value)
    {
        var lobby = Lobby;
        if (lobby is null) return;

        var settings = lobby.Settings;
        settings = tag switch
        {
            "PluginLobbyUsername" => settings with { Username = value?.ToString() ?? string.Empty },
            "PluginLobbyLatencyFirstMode" => settings with { UseLatencyFirstMode = value is bool latencyFirst && latencyFirst },
            "PluginLobbyTryPunchSym" => settings with { TryPunchSymmetricNat = value is bool tryPunch && tryPunch },
            "PluginLobbyEnableIPv6" => settings with { EnableIPv6 = value is bool enableIPv6 && enableIPv6 },
            "PluginLobbyEnableCliOutput" => settings with { EnableDebugOutput = value is bool enableDebug && enableDebug },
            _ => settings
        };

        lobby.UpdateSettings(settings);
    }

    private void LinkProtocolPerferenceChange(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniControlEnabled == 0)
            try
            {
                var lobby = Lobby;
                if (lobby is null) return;
                var selection = (LobbyProtocolPreference)((MyComboBox)sender).SelectedIndex;
                lobby.UpdateSettings(lobby.Settings with { ProtocolPreference = selection });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, Lang.Text("Setup.GameLink.Error.ConfigChangeFailed"), ModBase.LogLevel.Hint);
            }
    }

    // 网络测试
    private void BtnNetTest_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            HintService.Hint("网络测试由联机隧道插件提供，当前启动器不再内置网络测试。", HintType.Info);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Link] 获取网络测试结果失败", ModBase.LogLevel.Hint);
            BtnNetTest.IsEnabled = true;
            BtnNetTest.Text = Lang.Text("Setup.GameLink.NetworkTest.Start");
        }
    }
}