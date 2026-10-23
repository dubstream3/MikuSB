using System.Net.Sockets;
using System.Net;
using MikuSB.Util;
using MikuSB.Internationalization;

namespace MikuSB.TcpSharp;

public class SocketListener
{
    private static IPEndPoint? ListenAddress;
    private static readonly Logger Logger = new("GameServer");

    private static Socket? serverSocket;

    public static readonly SortedList<long, SocketConnection> Connections = [];

    public static Type BaseConnection { get; set; } = typeof(SocketConnection);

    private static int PORT => ConfigManager.Config.GameServer.Port;

    private static long _nextId = 0;

    public static void StartListener()
    {
        if (serverSocket != null)
            throw new InvalidOperationException("SocketListener already started.");

        ListenAddress = new IPEndPoint(IPAddress.Parse(ConfigManager.Config.GameServer.BindAddress), PORT);

        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        serverSocket.Bind(ListenAddress);
        serverSocket.Listen(100);

        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerRunning",
            I18NManager.Translate("Word.Game"),
            ConfigManager.Config.GameServer.GetDisplayAddress()));

        _ = Task.Run(AcceptLoop);
    }

    private static async Task AcceptLoop()
    {
        if (serverSocket == null)
            throw new InvalidOperationException("Server socket not initialized.");

        try
        {
            while (true)
            {
                Socket clientSocket = await serverSocket.AcceptAsync();
                var remote = clientSocket.RemoteEndPoint as IPEndPoint;

                if (remote == null)
                {
                    clientSocket.Close();
                    continue;
                }

                try
                {
                    var connection = (SocketConnection?)Activator.CreateInstance(BaseConnection, clientSocket, remote);

                    if (connection == null)
                    {
                        Logger.Error($"Failed to create connection instance from {BaseConnection.Name}");
                        clientSocket.Close();
                        continue;
                    }

                    var id = Interlocked.Increment(ref _nextId);
                    connection.ConnectionId = id;

                    Connections[id] = connection;
                    Logger.Info($"Accepted connection #{id} from {remote}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error creating connection: {ex}");
                    clientSocket.Close();
                }
            }
        }
        catch (ObjectDisposedException)
        {
            Logger.Info("Server stopped listening.");
        }
    }

    public static SocketConnection? GetConnectionByEndPoint(IPEndPoint ep)
    {
        Connections.TryGetValue(ep.GetHashCode(), out var conn);
        return conn;
    }

    public static void UnregisterConnection(SocketConnection socket)
    {
        if (socket == null) return;

        if (Connections.Remove(socket.ConnectionId))
        {
            Logger.Info($"Connection #{socket.ConnectionId} with {socket.RemoteEndPoint} has been closed");
        }
    }
}
