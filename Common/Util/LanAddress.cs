using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MikuSB.Util;

/// <summary>
/// Tells the machine that runs the server apart from the machines that reach it over the LAN.
///
/// The host client's SDK traffic arrives over loopback (it goes through the local proxy on
/// 127.0.0.1:18888), but its game-server socket is opened from the host's own LAN address, so a
/// plain loopback test is not enough: every address that belongs to this host has to count as
/// "the host". Everything else is a guest, which is what the LAN guest account mapping keys on.
/// </summary>
public static class LanAddress
{
    private static readonly Logger Logger = new("LanAddress");
    private static readonly Lazy<HashSet<IPAddress>> LocalAddresses = new(Collect);

    public static bool IsLocal(IPEndPoint? endPoint) => endPoint is null || IsLocal(endPoint.Address);

    public static bool IsLocal(IPAddress? address)
    {
        if (address is null) return true;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) return IsLocal(address.MapToIPv4());
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal) return true;
        return LocalAddresses.Value.Contains(address);
    }

    private static HashSet<IPAddress> Collect()
    {
        var addresses = new HashSet<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    addresses.Add(unicast.Address);
            }
        }
        catch (Exception e)
        {
            Logger.Warn("Could not enumerate the host's own addresses; guest detection falls back to loopback only.", e);
        }

        return addresses;
    }
}
