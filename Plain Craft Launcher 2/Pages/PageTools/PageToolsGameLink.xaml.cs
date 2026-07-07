using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using PCL.Core.Link;
using PCL.Core.Link.McPing;
using PCL.Core.Logging;
using PCL.Core.Utils.Validate;
using PCL.Network;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Plugins;
using PCL.Plugin.Abstractions;

namespace PCL;

public partial class PageToolsGameLink
{
    private static ILobbyService? Lobby =>
        PluginHostBootstrap.Extensions.GetDefault<ILobbyService>(PluginExtensionPoints.LobbyService);

    private static ILobbyNetworkTestService? NetworkTest =>
        PluginHostBootstrap.Extensions.GetDefault<ILobbyNetworkTestService>(PluginExtensionPoints.LobbyNetworkTestService);

    static PageToolsGameLink()
    {
        initLoader = new ModLoader.LoaderCombo<int>(Lang.Text("Link.Mod.Task.InitLobby"),
            new[] { new ModLoader.LoaderTask<int, int>(Lang.Text("Common.Action.Initialize"), InitTask) { ProgressWeight = 0.5d } });
    }

    public PageToolsGameLink()
    {
        InitializeComponent();
        LoaderInit();
        Loaded += (_, _) => Reload();
        PageEnter += PageLinkLobby_OnPageEnter;
    }

    public void RefreshProviderState()
    {
        var hasProvider = Lobby is not null;
        PanContent.Visibility = hasProvider ? Visibility.Visible : Visibility.Collapsed;
        PanNoProvider.Visibility = hasProvider ? Visibility.Collapsed : Visibility.Visible;
    }

    #region 初始化

    // 加载器初始化
    private void LoaderInit()
    {
        PageLoaderInit(Load, PanLoad, PanContent, null, initLoader, autoRun: false);
        // 注册自定义的 OnStateChanged
        initLoader.OnStateChangedUi += OnLoadStateChanged;

        SubscribeLobbyEvents();

        if (lobbyAnnouncementLoader is null)
        {
            var loaders = new List<ModLoader.LoaderBase>();
            loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.InitLobbyUi"), _ => ModBase.RunInUi(() =>
            {
                HintAnnounce.Visibility = Visibility.Visible;
                HintAnnounce.Theme = MyHint.Themes.Blue;
                HintAnnounce.Text = Lang.Text("Tools.GameLink.Loading.ConnectingServer");
            })));
            loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.FetchAnnouncement"), _ => GetAnnouncement()) { ProgressWeight = 0.5d });
            lobbyAnnouncementLoader = new ModLoader.LoaderCombo<int>("Lobby Announcement", loaders) { show = false };
        }
    }

    private async void OnServerExceptionHandler(Exception ex)
    {
        ModBase.RunInUi(() => HintService.Hint(ex.Message, HintType.Error));

        try
        {
            if (Lobby is not null) await Lobby.LeaveLobbyAsync();

            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                CurrentSubpage = Subpages.PanSelect;
            });
        }
        catch (Exception secEx)
        {
            ModBase.Log(secEx, "Occurred an exception when exit server.");
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerExit"), HintType.Error);
        }
    }

    public async void Reload()
    {
        RefreshProviderState();
        if (Lobby is null)
            return;

        SubscribeLobbyEvents();

        HintAnnounce.Visibility = Visibility.Visible;
        HintAnnounce.Text = Lang.Text("Tools.GameLink.Loading.ConnectingServer");
        HintAnnounce.Theme = MyHint.Themes.Blue;

        // 加载公告
        lobbyAnnouncementLoader.Start();
        if (_linkAnnounceUpdateCancelSource is not null)
            _linkAnnounceUpdateCancelSource.Cancel();
        _linkAnnounceUpdateCancelSource = new CancellationTokenSource();
        await Dispatcher.BeginInvoke(new Action(async () =>
            await _LinkAnnounceUpdateAsync())); // 我实在不理解为啥 BeginInvoke 这个委托要 MustBeInherit

        await Lobby.InitializeAsync().ConfigureAwait(false);
    }

    private void BtnOpenPluginInstalled_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupPluginInstalled);
    }

    private void BtnAgreeEula_Click(object sender, MouseButtonEventArgs e)
    {
        if (Lobby is not null) Lobby.IsEulaAccepted = true;
        CurrentSubpage = Subpages.PanSelect;
    }

    private void BtnEulaStop_Click(object sender, EventArgs eventArgs)
    {
        if (ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Eula.RevokeConfirm"),
                Lang.Text("Tools.GameLink.Eula.RevokeTitle"),
                Lang.Text("Common.Action.Confirm"),
                Lang.Text("Common.Action.Cancel"),
                isWarn: true
            ) == 1)
        {
            Lobby?.LogoutAccount();
            if (Lobby is not null) Lobby.IsEulaAccepted = false;
            HintService.Hint(Lang.Text("Tools.GameLink.Eula.Disabled"));
            CurrentSubpage = Subpages.PanEula;
        }
    }

    private static readonly ModLoader.LoaderCombo<int> initLoader;

    private static async void InitTask(ModLoader.LoaderTask<int, int> task)
    {
        if (Lobby is not null) await Lobby.InitializeAsync();
    }

    private ILobbyService? _subscribedLobby;

    private void SubscribeLobbyEvents()
    {
        var lobby = Lobby;
        if (lobby is null || ReferenceEquals(_subscribedLobby, lobby)) return;
        if (_subscribedLobby is not null)
        {
            _subscribedLobby.DiscoveredWorlds.CollectionChanged -= OnDiscoveredWorldsChanged;
            _subscribedLobby.Players.CollectionChanged -= OnPlayersChanged;
            _subscribedLobby.OnUserStopGame -= OnUserStopGame;
            _subscribedLobby.OnClientPing -= OnClientPingHandler;
            _subscribedLobby.OnServerShutDown -= OnServerShuttedDownHandler;
            _subscribedLobby.OnServerStarted -= OnServerStartedHandler;
            _subscribedLobby.OnServerException -= OnServerExceptionHandler;
        }

        lobby.DiscoveredWorlds.CollectionChanged += OnDiscoveredWorldsChanged;
        lobby.Players.CollectionChanged += OnPlayersChanged;
        lobby.OnUserStopGame += OnUserStopGame;
        lobby.OnClientPing += OnClientPingHandler;
        lobby.OnServerShutDown += OnServerShuttedDownHandler;
        lobby.OnServerStarted += OnServerStartedHandler;
        lobby.OnServerException += OnServerExceptionHandler;
        _subscribedLobby = lobby;
    }

    #region Subscribser

    private void OnServerStartedHandler()
    {
        ModBase.Log("Received server started event.");
        ModBase.RunInUi(() =>
        {
            LabFinishId.Text = Lobby?.CurrentLobbyCode;
            StackPlayerList.Children.Clear();
            foreach (var player in Lobby?.Players ?? [])
                StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
        });
    }

    private async void OnServerShuttedDownHandler()
    {
        try
        {
            if (Lobby is not null) await Lobby.LeaveLobbyAsync();

            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                CurrentSubpage = Subpages.PanSelect;
            });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "Occurred an exception when exit server.");
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerExit"), HintType.Error);
        }
    }

    private void OnClientPingHandler(long latency)
    {
        ModBase.RunInUi(() =>
        {
            LabFinishQuality.Text = Lang.Text("Tools.GameLink.Finish.Connected");
            LabFinishPing.Text = Lang.Text("Tools.GameLink.Finish.PingMs", latency);
            LabConnectType.Text = Lang.Text("Tools.GameLink.Finish.Unavailable");
        });
    }

    private void OnUserStopGame()
    {
        ModBase.RunInUi(() =>
        {
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
            StackPlayerList.Children.Clear();
            CurrentSubpage = Subpages.PanSelect;
        });
        ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Exit.Disbanded"), Lang.Text("Tools.GameLink.Exit.DisbandedTitle"));
    }


    private void OnPlayersChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        ModBase.Log("接收到玩家列表改变事件");
        ModBase.RunInUi(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems is not null)
                        foreach (LobbyPlayerProfile player in e.NewItems)
                            StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems is not null)
                        foreach (LobbyPlayerProfile player in e.OldItems)
                        {
                            var itemToRemove = StackPlayerList.Children.OfType<MyListItem>()
                                .FirstOrDefault(item => ((LobbyPlayerProfile)item.Tag).MachineId == player.MachineId);
                            if (itemToRemove is not null) StackPlayerList.Children.Remove(itemToRemove);
                        }

                    break;
                default:
                    StackPlayerList.Children.Clear();
                    foreach (var player in Lobby?.Players ?? [])
                        StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
                    break;
            }

            LabFinishQuality.Text = Lang.Text("Tools.GameLink.Finish.Connected");
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListCount", Lobby?.Players.Count ?? 0);
        });
    }


    private void OnDiscoveredWorldsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        LogWrapper.Info("[Lobby] Found new world changes");

        ModBase.RunInUi(() =>
        {
            #region 处理集合变更

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    ComboWorldList.Items.Clear();
                    foreach (var world in Lobby?.DiscoveredWorlds ?? [])
                        ComboWorldList.Items.Add(new MyComboBoxItem
                        {
                            Tag = world.Port,
                            Content = world.Name
                        });
                    break;

                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems is not null)
                        foreach (LobbyFoundWorld world in e.NewItems)
                            ComboWorldList.Items.Add(new MyComboBoxItem
                            {
                                Tag = world.Port,
                                Content = world.Name
                            });

                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems is not null)
                    {
                        // 使用 HashSet 提高查询效率
                        var portsToRemove = e.OldItems.Cast<LobbyFoundWorld>().Select(w => w.Port).ToHashSet();
                        var itemsToRemove = ComboWorldList.Items
                            .Cast<MyComboBoxItem>()
                            .Where(item => portsToRemove.Contains((int)item.Tag))
                            .ToList();

                        foreach (var item in itemsToRemove) ComboWorldList.Items.Remove(item);
                    }

                    break;
            }

            #endregion

            #region 更新 UI 状态

            var hasItems = ComboWorldList.Items.Count > 0;
            ComboWorldList.IsEnabled = hasItems;
            BtnCreate.IsEnabled = hasItems;

            if (hasItems && ComboWorldList.SelectedIndex == -1) ComboWorldList.SelectedIndex = 0;

            #endregion
        });
    }

    #endregion

    #endregion

    #region 公告

    public static ModLoader.LoaderCombo<int> lobbyAnnouncementLoader;
    private readonly ObservableCollection<LinkAnnounceInfo> _linkAnnounces = new();

    private CancellationTokenSource _linkAnnounceUpdateCancelSource;

    // 公告轮播实现
    private async Task _LinkAnnounceUpdateAsync()
    {
        var currentIndex = 0;
        var globalCancelToken = _linkAnnounceUpdateCancelSource.Token;
        CancellationTokenSource waiterCts = null;

        _linkAnnounces.CollectionChanged += (sender, e) =>
        {
            if (waiterCts is not null) waiterCts.Cancel();
        };

        while (!globalCancelToken.IsCancellationRequested)
        {
            waiterCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancelToken);
            var waiterCancelToken = waiterCts.Token;

            if (_linkAnnounces.Count > 0)
            {
                var info = _linkAnnounces[currentIndex];
                string prefix;
                if (info.Type == LinkAnnounceType.Important)
                {
                    HintAnnounce.Theme = MyHint.Themes.Red;
                    prefix = Lang.Text("Tools.GameLink.Announcement.Important");
                }
                else if (info.Type == LinkAnnounceType.Warning)
                {
                    HintAnnounce.Theme = MyHint.Themes.Yellow;
                    prefix = Lang.Text("Tools.GameLink.Announcement.Warning");
                }
                else
                {
                    HintAnnounce.Theme = MyHint.Themes.Blue;
                    prefix = Lang.Text("Tools.GameLink.Announcement.Notice");
                }

                HintAnnounce.Text = Lang.Text("Tools.GameLink.Announcement.Format", prefix,
                    info.Content.Replace("\n", "\r\n"));
            }
            else
            {
                HintAnnounce.Visibility = Visibility.Collapsed;
            }

            try
            {
                await Task.Delay(10000, waiterCancelToken);
            }
            catch (TaskCanceledException)
            {
                // 忽略取消任务的异常
            }

            if (!waiterCancelToken.IsCancellationRequested)
                currentIndex += 1;
            if (currentIndex >= _linkAnnounces.Count)
                currentIndex = 0;
            waiterCts = null;
        }
    }

    // 获取公告信息
    private void GetAnnouncement()
    {
        ModBase.RunInNewThread(() =>
        {
            try
            {
                var lobby = Lobby;
                if (lobby is null) return;
                var announcement = lobby.FetchAnnouncementAsync().GetAwaiter().GetResult();

                #region 解析基础状态与版本限制

                lobby.IsLobbyAvailable = announcement.Available;
                lobby.AllowCustomName = announcement.AllowCustomName;
                lobby.RequiresLogin = announcement.RequiresLogin;
                lobby.RequiresRealName = announcement.RequiresRealName;

                if (announcement.Version > lobby.ProtocolVersion)
                {
                    ModBase.RunInUi(() =>
                    {
                        HintAnnounce.Theme = MyHint.Themes.Red;
                        HintAnnounce.Text = Lang.Text("Tools.GameLink.Error.UpdateRequired");
                        lobby.IsLobbyAvailable = false;
                    });
                    return;
                }

                #endregion

                #region 解析公告列表 (Notices)

                foreach (var notice in announcement.Notices)
                {
                    var content = notice.Content;
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    // 版本过滤
                    var minVer = notice.MinVersion;
                    var maxVer = notice.MaxVersion;
                    if (ModBase.versionCode < minVer || ModBase.versionCode > maxVer) continue;

                    // 类型映射
                    var type = LinkAnnounceType.Notice;
                    var typeStr = notice.Type.ToLower();
                    if (typeStr == "important" || typeStr == "red") type = LinkAnnounceType.Important;
                    else if (typeStr == "warning" || typeStr == "yellow") type = LinkAnnounceType.Warning;

                    // 按行拆分公告
                    foreach (var announce in content.Split('\n'))
                    {
                        if (string.IsNullOrWhiteSpace(announce)) continue;
                        _linkAnnounces.Add(new LinkAnnounceInfo(type, announce));
                    }
                }

                #endregion

                #region 解析中继服务器 (Relays)

                lobby.SetRelayServers(announcement.Relays);

                #endregion

                #region 处理账户登录状态显示

                if (lobby.HasAccountLogin)
                {
                    ModBase.RunInUi(() => LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.Loading"));
                    if (string.IsNullOrEmpty(lobby.AccountDisplayName))
                        ReloadNaidData();
                    else
                        ModBase.RunInUi(() =>
                        {
                            LabNatayarkUserName.Text = lobby.AccountDisplayName;
                            LabNatayarkUserName.Opacity = 1;
                        });
                }
                else
                {
                    ModBase.RunInUi(() => LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.Login"));
                }

                #endregion
            }
            catch (Exception ex)
            {
                if (Lobby is not null) Lobby.IsLobbyAvailable = false;
                ModBase.RunInUi(() =>
                {
                    HintAnnounce.Theme = MyHint.Themes.Red;
                    HintAnnounce.Text = Lang.Text("Tools.GameLink.Error.ConnectFailed");
                });
                LogWrapper.Error(ex, "[Link] Failed to get lobby announcement");
            }
        });
    }

    #endregion

    #region 信息获取与展示

    #region UI 元素

    private object PlayerInfoItem(LobbyPlayerProfile info, MyListItem.ClickEventHandler onClick)
    {
        var details = info.Kind == LobbyPlayerKind.HOST
            ? Lang.Text("Tools.GameLink.Player.Details", Lang.Text("Tools.GameLink.Player.Host"), info.Vendor)
            : info.Vendor;

        var newItem = new MyListItem
        {
            Title = info.Name,
            Info = details,
            Type = MyListItem.CheckType.Clickable,
            Tag = info
        };
        newItem.Click += onClick;

        return newItem;
    }

    private void PlayerInfoClick(object sender, MouseButtonEventArgs e)
    {
        var info = (LobbyPlayerProfile)((MyListItem)sender).Tag;
        ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Player.InfoMessage", info.Name, info.Vendor), Lang.Text("Tools.GameLink.Player.InfoTitle", info.Name));
    }

    #endregion

    #region Natayark 账户相关功能

    private void ReloadNaidData()
    {
        ModBase.RunInNewThread(() =>
        {
            try
            {
                #region 1. 登录令牌有效期检查

                Lobby?.RefreshAccountAsync().GetAwaiter().GetResult();

                // 等待用户名加载，设置 10 秒超时防止线程卡死
                var retryCount = 0;
                while (string.IsNullOrWhiteSpace(Lobby?.AccountDisplayName) && retryCount < 10)
                {
                    Thread.Sleep(1000);
                    retryCount++;
                }

                if (string.IsNullOrWhiteSpace(Lobby?.AccountDisplayName))
                    throw new Exception("Timeout waiting for username");

                #endregion

                #region 3. UI 状态更新

                ModBase.RunInUi(() =>
                {
                    LabNatayarkUserName.Text = Lobby?.AccountDisplayName;
                    LabNatayarkUserName.Opacity = 1.0;
                });

                #endregion
            }
            catch (Exception ex)
            {
                #region 错误处理

                ModBase.Log(ex, "Failed to refresh Natayark ID info, re-login required");

                ModBase.RunInUi(() =>
                {
                    LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.FetchFailed");
                    LabNatayarkUserName.Opacity = 0.6;
                });

                #endregion
            }
        }, "Natayark Profile Refresh");
    }

    private void LabNatayarkUserName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // If Not IsLobbyAvailable Then
        // Hint("大厅功能暂不可用，请稍后再试", HintType.Critical)
        // Exit Sub
        // End If

        if (Lobby is not { HasAccountLogin: true })
        {
            // 当前未登录，显示登录选项
            if (ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Natayark.LoginPrompt"), Lang.Text("Tools.GameLink.Natayark.LoginTitle"), Lang.Text("Tools.GameLink.Natayark.Continue"), Lang.Text("Common.Action.Cancel")) == 1)
            {
                LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.BrowserContinue");
                LabNatayarkUserName.Opacity = 0.6d;
                BtnNatayarkUserName.IsEnabled = false;
                _ = Lobby?.StartAccountLoginAsync(() =>
                {
                    ModBase.RunInUi(() => BtnNatayarkUserName.IsEnabled = true);
                    HintService.Hint(Lang.Text("Tools.GameLink.Natayark.LoginComplete"), HintType.Success);
                    ReloadNaidData();
                });
            }
        }
        // 当前已登录，显示登出选项
        else if (ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Natayark.LogoutConfirm"), Lang.Text("Tools.GameLink.Natayark.LogoutTitle"), Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel")) == 1)
        {
            Lobby?.LogoutAccount();
            LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.Login");
            ModBase.Log("[Link] 已退出登录 Natayark Network");
            HintService.Hint(Lang.Text("Tools.GameLink.Natayark.LogoutComplete"), HintType.Success, false);
        }
    }

    #endregion

    // 网络测试功能
    private async void BtnNetTest_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var networkTest = NetworkTest;
            if (networkTest is null)
            {
                LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Failed");
                HintService.Hint(Lang.Text("Setup.GameLink.NetworkTest.Unavailable"), HintType.Info);
                return;
            }

            BtnNatTest.IsEnabled = false;
            LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Testing");
            var result = await networkTest.TestAsync();
            if (result is null)
            {
                LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Failed");
                HintService.Hint(Lang.Text("Setup.GameLink.NetworkTest.Failed"), HintType.Error);
                return;
            }

            LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Result",
                LobbyNetworkTestUi.GetNatTypeString(result.UdpNatType),
                LobbyNetworkTestUi.GetNatTypeString(result.TcpNatType));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Link] 获取网络测试结果失败", ModBase.LogLevel.Hint);
            BtnNatTest.IsEnabled = true;
            LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Failed");
        }
        finally
        {
            BtnNatTest.IsEnabled = true;
        }
    }

    private void PasteLobbyId(object sender, MouseButtonEventArgs e)
    {
        string lobbyId;
        try
        {
            lobbyId = Clipboard.GetText(TextDataFormat.Text);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "从剪贴板识别大厅编号出错");
            return;
        }

        if (!string.IsNullOrEmpty(lobbyId))
            TextJoinLobbyId.Text = lobbyId;
        else
            HintService.Hint(Lang.Text("Tools.GameLink.Join.InvalidText"));
    }

    private void ClearLobbyId(object sender, MouseButtonEventArgs e)
    {
        TextJoinLobbyId.Text = string.Empty;
    }

    #endregion

    #region PanSelect | 种类选择页面

    // 刷新按钮
    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        var lobby = Lobby?.DiscoverWorldAsync();
    }

    private static bool LobbyPrecheck()
    {
        var lobby = Lobby;
        if (lobby is null)
        {
            HintService.Hint("未启用联机插件，请先在插件管理中启用。", HintType.Error);
            return false;
        }

        return lobby.Precheck(ModProfile.selectedProfile?.Username);
    }

    private async void BtnInputPort_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            BtnInputPort.IsEnabled = false;
            if (!LobbyPrecheck()) return;
            var input = ModMain.MyMsgBoxInput(Lang.Text("Tools.GameLink.Create.EnterPort"),
                validateRules: [new IntValidator(65535,1024)]);
            int port;
            if (int.TryParse(input, out port))
                using (var ping = McPingServiceFactory.CreateService("127.0.0.1", port, 5000))
                {
                    var res = await ping.PingAsync();
                    if (res is not null && res.Version.Protocol != 0)
                        await CreateLobbyAsync(port);
                    else
                        HintService.Hint(Lang.Text("Tools.GameLink.Create.NotMcPort"), HintType.Error);
                }
        }
        finally
        {
            BtnInputPort.IsEnabled = true;
        }
    }

    // 创建大厅
    private async void BtnCreate_Click(object sender, MouseButtonEventArgs e)
    {
        if (ComboWorldList.SelectedItem is null)
        {
            HintService.Hint(Lang.Text("Tools.GameLink.Create.NoWorld"));
            return;
        }

        BtnCreate.IsEnabled = false;

        if (!LobbyPrecheck())
        {
            BtnCreate.IsEnabled = true;
            return;
        }

        var port = (int)((MyComboBoxItem)ComboWorldList.SelectedItem).Tag;
        await CreateLobbyAsync(port);
    }

    private async Task CreateLobbyAsync(int port)
    {
        ModBase.Log("[Link] 创建大厅，端口：" + port);


        var username = Lobby?.GetUsername();

        ModBase.RunInUi(() =>
        {
            BtnFinishPing.Visibility = Visibility.Collapsed;
            LabFinishPing.Text = "-ms";
            BtnConnectType.Visibility = Visibility.Collapsed;
            LabConnectType.Text = Lang.Text("Tools.GameLink.Finish.Connecting");
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
            StackPlayerList.Children.Clear();
            LabConnectUserName.Text = username;
            LabConnectUserType.Text = Lang.Text("Tools.GameLink.Finish.Host");
            LabFinishId.Text = Lobby?.CurrentLobbyCode;
            BtnFinishCopyIp.Visibility = Visibility.Collapsed;
            BtnCreate.IsEnabled = true;
            BtnFinishExit.Text = Lang.Text("Tools.GameLink.Finish.CloseLobby");
            CurrentSubpage = Subpages.PanFinish;
        });

        var res = Lobby is not null && await Lobby.CreateLobbyAsync(port, username).ConfigureAwait(true);

        if (!res)
            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                CurrentSubpage = Subpages.PanSelect;
            });
    }

    // 加入大厅
    private async void BtnJoin_Click(object sender, MouseButtonEventArgs e)
    {
        if (!LobbyPrecheck())
            return;

        ModBase.Log("Start to join lobby.");

        var id = TextJoinLobbyId.Text;
        var username = Lobby?.GetUsername();

        ModBase.RunInUi(() =>
        {
            BtnFinishPing.Visibility = Visibility.Visible;
            LabFinishPing.Text = "-ms";
            BtnConnectType.Visibility = Visibility.Visible;
            LabConnectType.Text = Lang.Text("Tools.GameLink.Finish.Connecting");
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
            StackPlayerList.Children.Clear();
            LabConnectUserName.Text = username;
            LabConnectUserType.Text = Lang.Text("Tools.GameLink.Finish.Guest");
            LabFinishId.Text = id;
            BtnFinishCopyIp.Visibility = Visibility.Visible;
            CurrentSubpage = Subpages.PanFinish;
        });

        var res = Lobby is not null && await Lobby.JoinLobbyAsync(id, username).ConfigureAwait(true);

        if (!res)
            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                CurrentSubpage = Subpages.PanSelect;
            });
    }

    private void TextJoinLobbyId_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnJoin_Click(sender, null);
    }

    #endregion

    #region PanLoad | 加载中页面

    // 承接状态切换的 UI 改变
    private void OnLoadStateChanged(ModLoader.LoaderBase loader, ModBase.LoadState newState, ModBase.LoadState oldState)
    {
    }

    private static string _loadStep = "准备初始化";

    private static void SetLoadDesc(string intro, string step)
    {
        ModBase.Log("连接步骤：" + intro);
        _loadStep = step;
        ModBase.RunInUiWait(() =>
        {
            if (ModMain.frmToolsGameLink is null || !ModMain.frmToolsGameLink.LabLoadDesc.IsLoaded)
                return;
            ModMain.frmToolsGameLink.LabLoadDesc.Text = intro;
            ModMain.frmToolsGameLink.UpdateProgress();
        });
    }

    // 承接重试
    private void CardLoad_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (initLoader.State != ModBase.LoadState.Failed)
            return;
        initLoader.Start(isForceRestart: true);
    }

    // 取消加载
    private void CancelLoad(object sender, EventArgs eventArgs)
    {
        if (initLoader.State == ModBase.LoadState.Loading)
        {
            CurrentSubpage = Subpages.PanSelect;
            initLoader.Abort();
        }
        else
        {
            initLoader.State = ModBase.LoadState.Waiting;
        }
    }

    // 进度改变
    private void UpdateProgress(double value = -1)
    {
        if (value == -1)
            value = initLoader.Progress;
        var displayingProgress = ColumnProgressA.Width.Value;
        if (Math.Round(value - displayingProgress, 3) == 0d)
            return;
        if (displayingProgress > value)
        {
            ColumnProgressA.Width = new GridLength(value, GridUnitType.Star);
            ColumnProgressB.Width = new GridLength(1d - value, GridUnitType.Star);
            ModAnimation.AniStop("LobbyProgress");
        }
        else
        {
            var newProgress = value == 1d ? 1d : (value - displayingProgress) * 0.2d + displayingProgress;
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaGridLengthWidth(ColumnProgressA, newProgress - ColumnProgressA.Width.Value, 300,
                        ease: new ModAnimation.AniEaseOutFluent()),
                    ModAnimation.AaGridLengthWidth(ColumnProgressB, 1d - newProgress - ColumnProgressB.Width.Value, 300,
                        ease: new ModAnimation.AniEaseOutFluent())
                }, "LobbyProgress");
        }
    }

    private void CardResized(object sender, SizeChangedEventArgs sizeChangedEventArgs)
    {
        RectProgressClip.Rect = new Rect(0d, 0d, CardLoad.ActualWidth, 12d);
    }

    #endregion

    #region PanFinish | 加载完成页面

    // 退出
    private async void BtnFinishExit_Click(object sender, ModBase.RouteEventArgs routeEventArgs)
    {
        if (ModMain.MyMsgBox(
                Lang.Text((Lobby?.IsHost ?? false)
                    ? "Tools.GameLink.Exit.ConfirmMessageWithHost"
                    : "Tools.GameLink.Exit.ConfirmMessage"),
                Lang.Text("Tools.GameLink.Exit.ConfirmTitle"),
                Lang.Text("Common.Action.Confirm"),
                Lang.Text("Common.Action.Cancel"),
                isWarn: true
            ) == 1)
        {
            CurrentSubpage = Subpages.PanSelect;
            BtnFinishExit.Text = Lang.Text("Tools.GameLink.Finish.Exit");
            if (Lobby is not null) await Lobby.LeaveLobbyAsync().ConfigureAwait(true);
        }
    }

    // 复制大厅编号
    private void BtnFinishCopy_Click(object sender, ModBase.RouteEventArgs routeEventArgs)
    {
        ModBase.ClipboardSet(LabFinishId.Text);
    }

    // 复制 IP
    private void BtnFinishCopyIp_Click(object sender, ModBase.RouteEventArgs routeEventArgs)
    {
        var localPort = Lobby?.LocalMinecraftPort;
        if (localPort is null) return;
        var ip = $"127.0.0.1:{localPort}";
        ModMain.MyMsgBox(Lang.Text("Tools.GameLink.CopyIp.Message", ip),
            Lang.Text("Tools.GameLink.CopyIp.Title"),
            Lang.Text("Common.Action.Copy"),
            Lang.Text("Tools.GameLink.CopyIp.Back"),
            button1Action: () => ModBase.ClipboardSet(ip));
    }

    #endregion

    #region 子页面管理

    public enum Subpages
    {
        PanEula,
        PanSelect,
        PanFinish
    }

    public Subpages CurrentSubpage
    {
        get => field;
        set
        {
            if (field == value)
                return;
            field = value;
            ModBase.Log("[Link] 子页面更改为 " + ModBase.GetStringFromEnum(value));
            PageOnContentExit();
        }
    } = Lobby is { IsEulaAccepted: true } ? Subpages.PanSelect : Subpages.PanEula;

    private void PageLinkLobby_OnPageEnter()
    {
        RefreshProviderState();
        if (PluginHostBootstrap.Extensions.GetAll(PluginExtensionPoints.LobbyTunnelProvider).Count == 0)
            return;

        ModMain.frmToolsGameLink.PanEula.Visibility =
            CurrentSubpage == Subpages.PanEula ? Visibility.Visible : Visibility.Collapsed;
        ModMain.frmToolsGameLink.PanSelect.Visibility =
            CurrentSubpage == Subpages.PanSelect ? Visibility.Visible : Visibility.Collapsed;
        ModMain.frmToolsGameLink.PanFinish.Visibility =
            CurrentSubpage == Subpages.PanFinish ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
