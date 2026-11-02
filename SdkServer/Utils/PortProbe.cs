using System.Net;
using System.Net.Sockets;

namespace MikuSB.SdkServer.Utils;

/// <summary>
///     Answers "can this port still be bound?" before Kestrel tries. The SDK ports are hard-coded in the game client,
///     so when one of them is taken the only useful response is to name the port and keep serving the others instead
///     of dying with a stack trace the operator never sees.
/// </summary>
public static class PortProbe
{
    public static IReadOnlyList<int> FindBusy(IPAddress address, IEnumerable<int> ports)
    {
        var busy = new List<int>();
        foreach (var port in ports.Distinct())
        {
            if (IsBusy(address, port))
            {
                busy.Add(port);
                continue;
            }

            // Windows lets a wildcard bind (0.0.0.0) coexist with a listener that only took 127.0.0.1. Binding would
            // succeed, but the client reaches 127.0.0.1 through the hosts redirect, so that socket would answer
            // instead of us. Only relevant for the wildcard address: a specific bind address never serves loopback.
            if (address.Equals(IPAddress.Any) && IsBusy(IPAddress.Loopback, port))
            {
                busy.Add(port);
            }
        }

        return busy;
    }

    /// <summary>True when a listener already owns the port on the address Kestrel would bind to.</summary>
    public static bool IsBusy(IPAddress address, int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(address, port);
            listener.Start();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (Exception)
        {
            // Unknown failure: let Kestrel have the final word rather than skipping a port we might have used.
            return false;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch
            {
                // Nothing useful to do: the socket is gone either way.
            }
        }
    }
}
