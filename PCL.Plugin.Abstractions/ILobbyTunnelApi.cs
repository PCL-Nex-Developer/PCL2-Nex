using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 联机扩展点便捷注册方法。
/// </summary>
public static class LobbyTunnelExtensions
{
    /// <summary>
    /// 注册一个联机隧道提供者。
    /// </summary>
    public static IDisposable RegisterLobbyTunnelProvider(
        this IPluginExtensionApi extensions,
        ILobbyTunnelProvider provider,
        string? id = null,
        int order = 100)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(provider);

        return extensions.Register(new PluginExtensionDescriptor<ILobbyTunnelProvider>
        {
            ExtensionPoint = PluginExtensionPoints.LobbyTunnelProvider,
            Id = string.IsNullOrWhiteSpace(id) ? provider.Id : id,
            DisplayName = provider.DisplayName,
            Order = order,
            Contribution = provider
        });
    }

    /// <summary>
    /// 注册一个联机功能服务。
    /// </summary>
    public static IDisposable RegisterLobbyService(
        this IPluginExtensionApi extensions,
        ILobbyService service,
        string id = "default",
        string displayName = "Lobby Service",
        int order = 100)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(service);

        return extensions.Register(new PluginExtensionDescriptor<ILobbyService>
        {
            ExtensionPoint = PluginExtensionPoints.LobbyService,
            Id = string.IsNullOrWhiteSpace(id) ? "default" : id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Lobby Service" : displayName,
            Order = order,
            Contribution = service
        });
    }

    /// <summary>
    /// 注册一个联机网络测试服务。
    /// </summary>
    public static IDisposable RegisterLobbyNetworkTestService(
        this IPluginExtensionApi extensions,
        ILobbyNetworkTestService service,
        string id = "default",
        string displayName = "Lobby Network Test",
        int order = 100)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(service);

        return extensions.Register(new PluginExtensionDescriptor<ILobbyNetworkTestService>
        {
            ExtensionPoint = PluginExtensionPoints.LobbyNetworkTestService,
            Id = string.IsNullOrWhiteSpace(id) ? "default" : id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Lobby Network Test" : displayName,
            Order = order,
            Contribution = service
        });
    }
}

/// <summary>
/// Provides the lobby feature implementation used by the host-provided lobby pages.
/// </summary>
public interface ILobbyService
{
    ObservableCollection<LobbyFoundWorld> DiscoveredWorlds { get; }
    ObservableCollection<LobbyPlayerProfile> Players { get; }
    LobbyServiceState CurrentState { get; }
    bool IsHost { get; }
    bool IsLobbyAvailable { get; set; }
    bool AllowCustomName { get; set; }
    bool RequiresLogin { get; set; }
    bool RequiresRealName { get; set; }
    int ProtocolVersion { get; }
    string? CurrentLobbyCode { get; }
    string? CurrentUserName { get; }
    int? LocalMinecraftPort { get; }
    LobbySettingsSnapshot Settings { get; }
    bool IsEulaAccepted { get; set; }
    bool HasAccountLogin { get; }
    string? AccountDisplayName { get; }
    event Action<LobbyServiceState, LobbyServiceState>? StateChanged;
    event Action? OnUserStopGame;
    event Action<long>? OnClientPing;
    event Action? OnServerShutDown;
    event Action? OnServerStarted;
    event Action<Exception>? OnServerException;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task DiscoverWorldAsync(CancellationToken cancellationToken = default);
    Task<bool> CreateLobbyAsync(int port, string username, CancellationToken cancellationToken = default);
    Task<bool> JoinLobbyAsync(string lobbyCode, string username, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(CancellationToken cancellationToken = default);
    string? GetUsername();
    bool Precheck(string? selectedProfileUsername);
    void UpdateSettings(LobbySettingsSnapshot settings);
    void ResetSettings();
    string GetAnnouncementCache();
    int GetAnnouncementCacheVersion();
    void SetAnnouncementCache(string cache, int version);
    void ResetAnnouncementCache();
    Task RefreshAccountAsync(CancellationToken cancellationToken = default);
    Task StartAccountLoginAsync(Action? completeCallback = null, CancellationToken cancellationToken = default);
    void LogoutAccount();
    Task<LobbyAnnouncementSnapshot> FetchAnnouncementAsync(CancellationToken cancellationToken = default);
    void SetRelayServers(IEnumerable<LobbyRelayDescriptor> relays);
}

public sealed record LobbyAnnouncementSnapshot
{
    public required bool Available { get; init; }
    public required bool AllowCustomName { get; init; }
    public required bool RequiresLogin { get; init; }
    public required bool RequiresRealName { get; init; }
    public required double Version { get; init; }
    public IReadOnlyList<LobbyAnnouncementNotice> Notices { get; init; } = [];
    public IReadOnlyList<LobbyRelayDescriptor> Relays { get; init; } = [];
}

public sealed record LobbyAnnouncementNotice
{
    public required string Content { get; init; }
    public required string Type { get; init; }
    public required double MinVersion { get; init; }
    public required double MaxVersion { get; init; }
}

public sealed record LobbySettingsSnapshot
{
    public string Username { get; init; } = string.Empty;
    public int ServerType { get; init; } = 1;
    public bool UseLatencyFirstMode { get; init; } = true;
    public string CustomRelayServer { get; init; } = string.Empty;
    public LobbyProtocolPreference ProtocolPreference { get; init; } = LobbyProtocolPreference.Tcp;
    public bool TryPunchSymmetricNat { get; init; } = true;
    public bool EnableIPv6 { get; init; } = true;
    public bool EnableDebugOutput { get; init; }
}

public enum LobbyProtocolPreference
{
    Tcp,
    Udp
}

public sealed record LobbyFoundWorld(string Name, int Port);

public sealed record LobbyRelayDescriptor
{
    public required string Url { get; init; }
    public required string Name { get; init; }
    public required LobbyRelayKind Type { get; init; }
}

public enum LobbyRelayKind
{
    Community,
    Selfhosted,
    Custom
}

public sealed record LobbyPlayerProfile
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("machine_id")] public required string MachineId { get; init; }
    [JsonPropertyName("vendor")] public required string Vendor { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LobbyPlayerKind? Kind { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LobbyPlayerKind
{
    HOST,
    GUEST
}

public enum LobbyServiceState
{
    Idle,
    Initializing,
    Initialized,
    Discovering,
    Creating,
    Joining,
    Connected,
    Leaving,
    Error
}

/// <summary>
/// Creates tunnel sessions for PCL lobby hosting and joining.
/// </summary>
public interface ILobbyTunnelProvider
{
    /// <summary>Stable provider identifier.</summary>
    string Id { get; }

    /// <summary>Human readable provider name.</summary>
    string DisplayName { get; }

    /// <summary>Provider version text displayed to peers.</summary>
    string Version { get; }

    /// <summary>Whether the provider is ready to create tunnel sessions.</summary>
    bool IsAvailable { get; }

    /// <summary>Optional message explaining why the provider is unavailable.</summary>
    string? UnavailableReason { get; }

    /// <summary>Creates and starts a tunnel session.</summary>
    Task<ILobbyTunnelSession> CreateSessionAsync(LobbyTunnelSessionOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// A running lobby tunnel session.
/// </summary>
public interface ILobbyTunnelSession : IAsyncDisposable
{
    /// <summary>Lobby code used by this session.</summary>
    string LobbyCode { get; }

    /// <summary>Local Minecraft port guarded by the host game watcher.</summary>
    int MinecraftPort { get; }

    /// <summary>Host peer discovered by the tunnel provider when joining a lobby.</summary>
    LobbyTunnelPeerInfo? HostPeer { get; }

    /// <summary>Adds a local port forward to a remote peer.</summary>
    Task<int> AddPortForwardAsync(string targetIp, int targetPort, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides the network test implementation for host-provided lobby pages.
/// </summary>
public interface ILobbyNetworkTestService
{
    Task<LobbyNetworkTestResult?> TestAsync(CancellationToken cancellationToken = default);
}

public sealed record LobbyNetworkTestResult
{
    public required LobbyNatType UdpNatType { get; init; }
    public required LobbyNatType TcpNatType { get; init; }
    public required bool SupportIPv6 { get; init; }
}

public enum LobbyNatType
{
    Unknown,
    OpenInternet,
    NoPat,
    FullCone,
    Restricted,
    PortRestricted,
    SymmetricEasy,
    Symmetric,
    SymmetricFirewall,
    UdpBlocked
}

/// <summary>
/// Input required to create a lobby tunnel session.
/// </summary>
public sealed record LobbyTunnelSessionOptions
{
    public required string LobbyCode { get; init; }
    public required string NetworkName { get; init; }
    public required string NetworkSecret { get; init; }
    public required string MachineId { get; init; }
    public required string PlayerName { get; init; }
    public required bool IsHost { get; init; }
    public required int MinecraftPort { get; init; }
    public required int ScaffoldingPort { get; init; }
    public IReadOnlyList<string> RelayServers { get; init; } = [];
    public string PreferredProtocol { get; init; } = "tcp";
    public bool TryPunchSymmetricNat { get; init; } = true;
    public bool EnableIPv6 { get; init; } = true;
    public bool UseLatencyFirstMode { get; init; }
    public bool EnableDebugOutput { get; init; }
}

/// <summary>
/// Peer information returned by a lobby tunnel provider.
/// </summary>
public sealed record LobbyTunnelPeerInfo
{
    public required bool IsHost { get; init; }
    public required string HostName { get; init; }
    public required string Ip { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string MinecraftName { get; init; } = string.Empty;
    public LobbyTunnelConnectionWay Way { get; init; } = LobbyTunnelConnectionWay.Unknown;
    public double Ping { get; init; }
    public double Loss { get; init; }
    public string NatType { get; init; } = string.Empty;
    public string ProviderVersion { get; init; } = string.Empty;
}

public enum LobbyTunnelConnectionWay
{
    Local,
    P2P,
    Relay,
    Unknown
}