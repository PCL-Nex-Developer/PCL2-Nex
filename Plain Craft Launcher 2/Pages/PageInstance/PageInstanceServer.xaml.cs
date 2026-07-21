using System.IO;
using System.Windows;
using System.Windows.Input;
using fNbt;
using PCL.Core.Minecraft;
using PCL.Core.Utils.Validate;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageInstanceServer : MyPageRight
{
    private const int debounceInterval = 2000;

    public static readonly List<MinecraftServerInfo> serverList = new();

    private DateTime _lastRefresh = DateTime.MinValue;

    public PageInstanceServer()
    {
        InitializeComponent();
        Loaded += PageLoaded;
    }

    private async void PageLoaded(object e, RoutedEventArgs sender)
    {
        serverList.Clear();
        PanServers.Children.Clear();

        await LoadServersFromFileAsync();
        RefreshTip();

        foreach (var server in serverList)
        {
            var serverCard = new ServerCard();
            serverCard.RemoveServer += RemoveServerEvent;
            serverCard.EditServer += (a, b) => this.EditServer(a, (ServerCard.ResultEventArgs)b);
            serverCard.UpdateServerInfo(server);
            PanServers.Children.Add(serverCard);
        }
    }

    private async void RemoveServerEvent(object sender, EventArgs e)
    {
        // Get server index
        var index = PanServers.Children.IndexOf((UIElement)sender);
        if (index < 0)
        {
            HintService.Hint(Lang.Text("Instance.Server.IndexNotFound"), HintType.Error);
            return;
        }

        // Read NBT file
        var nbtData =
            await NbtFileHandler.ReadTagInNbtFileAsync<NbtList>(
                Path.Combine(PageInstanceLeft.McInstance.PathIndie, "servers.dat"), "servers");
        if (nbtData is null)
        {
            HintService.Hint(Lang.Text("Instance.Server.ReadDataFailed"), HintType.Error);
            return;
        }

        // Remove server from NBT data
        nbtData.RemoveAt(index);
        var clonedNbtData = (NbtList)nbtData.Clone();

        // Write back to NBT file
        if (!await NbtFileHandler.WriteTagInNbtFileAsync(clonedNbtData,
                Path.Combine(PageInstanceLeft.McInstance.PathIndie, "servers.dat")))
        {
            HintService.Hint(Lang.Text("Instance.Server.WriteDataFailed"), HintType.Error);
            return;
        }

        // Remove server from list and UI
        serverList.RemoveAt(index);
        if (serverList.Count == 0) RefreshTip();

        // Remove UI element
        PanServers.Children.Remove((UIElement)sender);

        // Success message
        HintService.Hint(Lang.Text("Instance.Server.Removed"), HintType.Success);
    }

    private async void EditServer(object sender, ServerCard.ResultEventArgs e)
    {
        // Read NBT file
        var nbtData =
            await NbtFileHandler.ReadTagInNbtFileAsync<NbtList>(Path.Combine(PageInstanceLeft.McInstance.PathIndie, "servers.dat"),
                "servers");
        if (nbtData is null)
        {
            HintService.Hint(Lang.Text("Instance.Server.ReadDataFailed"), HintType.Error);
            return;
        }

        // Get server index
        var index = PanServers.Children.IndexOf((UIElement)sender);
        if (index < 0 || index >= nbtData.Count)
        {
            HintService.Hint(Lang.Text("Instance.Server.IndexNotFound"), HintType.Error);
            return;
        }

        // Verify server data
        var server = nbtData[index] as NbtCompound;

        // Update server data
        server["name"] = new NbtString("name", e.Param1);
        server["ip"] = new NbtString("ip", e.Param2);

        // Write updated NBT data
        var clonedNbtData = (NbtList)nbtData.Clone();
        if (!await NbtFileHandler.WriteTagInNbtFileAsync(clonedNbtData,
                Path.Combine(PageInstanceLeft.McInstance.PathIndie, "servers.dat")))
        {
            HintService.Hint(Lang.Text("Instance.Server.WriteDataFailed"), HintType.Error);
            return;
        }

        var serverCard = (ServerCard)sender;

        serverCard.server.Name = e.Param1;
        serverCard.server.Address = e.Param2;
        serverCard.UpdateServerInfo(serverCard.server);

        // Success message
        HintService.Hint(Lang.Text("Instance.Server.Updated"), HintType.Success);
    }

    /// <summary>
    ///     刷新服务器列表
    /// </summary>
    public async void RefreshServers()
    {
        ModBase.Log("刷新服务器列表");
        try
        {
            // 读取服务器信息
            await LoadServersFromFileAsync();

            // 在UI线程中更新界面
            ModBase.RunInUi(() => UpdateServerUi());

        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Server.RefreshFailed"), ModBase.LogLevel.Feedback);
            ModBase.RunInUi(() => HintService.Hint(Lang.Text("Instance.Server.RefreshFailed") + ": " + ex.Message, HintType.Error));
        }
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        if ((DateTime.Now - _lastRefresh).TotalMilliseconds < debounceInterval)
        {
            HintService.Hint(Lang.Text("Instance.Server.NoFrequentRefresh"));
            return;
        }

        _lastRefresh = DateTime.Now;
        HintService.Hint(Lang.Text("Instance.Server.RefreshingList"));
        try
        {
            RefreshServers();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Server.RefreshFailed"), ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Server.RefreshFailed") + ": " + ex.Message, HintType.Error);
        }
    }

    private async void BtnAddServer_Click(object sender, MouseButtonEventArgs e)
    {
        var result = GetServerInfo(new MinecraftServerInfo { Name = Lang.Text("Instance.Server.DefaultName"), Address = "" });
        if (result.Success)
        {
            var newServer = new MinecraftServerInfo
            {
                Name = result.Name,
                Address = result.Address
            };
            serverList.Add(newServer);

            RefreshTip();

            var serverCard = new ServerCard();
            serverCard.RemoveServer += RemoveServerEvent;
            serverCard.EditServer += (a, b) => this.EditServer(a, (ServerCard.ResultEventArgs)b);
            serverCard.UpdateServerInfo(newServer);
            PanServers.Children.Add(serverCard);

            var serversDatPath = Path.Combine(PageInstanceLeft.McInstance.PathIndie, "servers.dat");

            NbtList nbtData;
            if (!File.Exists(serversDatPath))
            {
                nbtData = new NbtList("servers", NbtTagType.Compound);
                RefreshTip();
            }
            else
            {
                nbtData = await NbtFileHandler.ReadTagInNbtFileAsync<NbtList>(serversDatPath, "servers");
            }

            if (nbtData is not null)
            {
                var server = new NbtCompound();
                server["name"] = new NbtString("name", result.Name);
                server["ip"] = new NbtString("ip", result.Address);
                if (nbtData.Count == 0) nbtData.ListType = NbtTagType.Compound;
                nbtData.Add(server);
                var clonedNbtData = (NbtList)nbtData.Clone();
                await NbtFileHandler.WriteTagInNbtFileAsync(clonedNbtData, serversDatPath);
            }
        }
    }

    public static (string Name, string Address, bool Success) GetServerInfo(MinecraftServerInfo server)
    {
        var newName = ModMain.MyMsgBoxInput(Lang.Text("Instance.Server.EditTitle"), Lang.Text("Instance.Server.NamePrompt"), server.Name,
            [new NullOrWhiteSpaceValidator()]);

        if (string.IsNullOrEmpty(newName)) return (string.Empty, string.Empty, false);

        var newAddress = ModMain.MyMsgBoxInput(Lang.Text("Instance.Server.EditTitle"), Lang.Text("Instance.Server.AddressPrompt"), server.Address,
            [new NullOrWhiteSpaceValidator()]);
        if (string.IsNullOrEmpty(newAddress)) return (string.Empty, string.Empty, false);
        return (newName, newAddress, true);
    }

    /// <summary>
    ///     从servers.dat文件读取服务器信息
    /// </summary>
    private async Task LoadServersFromFileAsync()
    {
        serverList.Clear();

        var serversFile = Path.Combine(PageInstanceLeft.McInstance.PathIndie, "servers.dat");
        if (!File.Exists(serversFile))
            return;

        try
        {
            // 读取NBT格式的servers.dat文件
            var nbtData = await NbtFileHandler.ReadTagInNbtFileAsync<NbtList>(serversFile, "servers");
            ParseServersFromNBT(nbtData);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Server.ReadFileFailed"));
        }
    }

    /// <summary>
    ///     解析NBT格式的服务器数据
    /// </summary>
    private void ParseServersFromNBT(NbtList serversList)
    {
        if (serversList is not null)
        {
            ModBase.Log($"Found {serversList.Count} servers:");

            // 遍历 servers 列表中的每个服务器
            for (int i = 0, loopTo = serversList.Count - 1; i <= loopTo; i++)
            {
                var server = serversList[i] as NbtCompound;
                if (server is not null)
                {
                    // 提取服务器信息
                    // Dim hidden As Byte = If(server.Get(Of NbtByte)("hidden")?.Value, 0)
                    var ip = server.Get<NbtString>("ip")?.Value ?? "Unknown";
                    var name = server.Get<NbtString>("name")?.Value ?? "Unknown";
                    var iconBase64 = server.Get<NbtString>("icon")?.Value;

                    ModBase.Log($"服务器 {i + 1}:");
                    ModBase.Log($"  名字: {name}");
                    ModBase.Log($"  IP: {ip}");
                    // Log($"  Hidden: {If(hidden = 1, "Yes", "No")}")
                    serverList.Add(new MinecraftServerInfo
                    {
                        Name = name,
                        Address = ip,
                        Icon = iconBase64
                    });
                }
            }
        }
        else
        {
            ModBase.Log("No 'servers' list found in servers.dat.");
        }
    }

    /// <summary>
    ///     更新服务器UI显示
    /// </summary>
    private void UpdateServerUi()
    {
        PanServers.Children.Clear();

        RefreshTip();

        foreach (var server in serverList)
        {
            var serverCard = new ServerCard();
            serverCard.RemoveServer += RemoveServerEvent;
            serverCard.EditServer += (a, b) => this.EditServer(a, (ServerCard.ResultEventArgs)b);
            serverCard.UpdateServerInfo(server);
            PanServers.Children.Add(serverCard);
        }
    }

    private void RefreshTip()
    {
        if (serverList.Count == 0)
        {
            ModBase.Log(Lang.Text("Instance.Server.NoServersFound"));
            PanNoServer.Visibility = Visibility.Visible;
            PanContent.Visibility = Visibility.Collapsed;
            PanServers.Visibility = Visibility.Collapsed;
            return;
        }

        ModBase.Log(Lang.Text("Instance.Server.FoundServers"));
        PanNoServer.Visibility = Visibility.Collapsed;
        PanContent.Visibility = Visibility.Visible;
        PanServers.Visibility = Visibility.Visible;
    }

}

/// <summary>
///     Minecraft服务器信息类
/// </summary>
public class MinecraftServerInfo
{
    public string Name { get; set; }
    public string Address { get; set; }
    public string Icon { get; set; } = "";
}
