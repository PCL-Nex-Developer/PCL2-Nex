using PCL.Core.App.Localization;
using PCL.Plugin.Abstractions;

namespace PCL;

internal static class LobbyNetworkTestUi
{
    public static string GetNatTypeString(LobbyNatType type)
    {
        return Lang.Text(type switch
        {
            LobbyNatType.OpenInternet or LobbyNatType.NoPat => "Link.Nat.Type.Open",
            LobbyNatType.FullCone => "Link.Nat.Type.FullCone",
            LobbyNatType.PortRestricted => "Link.Nat.Type.PortRestricted",
            LobbyNatType.Restricted => "Link.Nat.Type.Restricted",
            LobbyNatType.SymmetricEasy => "Link.Nat.Type.SymmetricEasy",
            LobbyNatType.Symmetric => "Link.Nat.Type.Symmetric",
            LobbyNatType.SymmetricFirewall => "Link.Nat.Type.SymmetricFirewall",
            LobbyNatType.UdpBlocked => "Link.Nat.Type.UdpBlocked",
            _ => "Link.Nat.Type.Unknown"
        });
    }

    public static string GetIpv6StatusString(bool supported)
    {
        return Lang.Text(supported
            ? "Setup.GameLink.NetworkTest.Supported"
            : "Setup.GameLink.NetworkTest.Unsupported");
    }
}