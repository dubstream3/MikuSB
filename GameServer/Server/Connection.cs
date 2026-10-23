using MikuSB.Enums.Packet;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet;
using MikuSB.TcpSharp;
using MikuSB.Util;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace MikuSB.GameServer.Server;

public class Connection(Socket socket, IPEndPoint remote) : SocketConnection(socket, remote)
{
    private static readonly Logger Logger = new("GameServer");

    public PlayerInstance? Player { get; set; }

    /// <summary>online/online.json Id the client asked for with ReqOnlineRoom(303), 0 when unknown.</summary>
    public uint OnlineRoomId { get; set; }

    /// <summary>Team id the client asked for with ReqOnlineRoom(303) (10 = Online.TeamId).</summary>
    public uint OnlineRoomTeamId { get; set; }

    private static readonly HashSet<string> DummyPacketNames =
    [
        
    ];

    public override async void Start()
    {
        Logger.Info($"New connection from {RemoteEndPoint}.");
        State = SessionStateEnum.WAITING_FOR_TOKEN;
        await ReceiveLoop();
    }

    public override void Stop(bool isServerStop = false)
    {
        Player?.OnLogoutAsync();
        SocketListener.UnregisterConnection(this);
        base.Stop(isServerStop);
    }

    public static int GetInt32(byte[] buf, int index)
    {
        int networkValue = BitConverter.ToInt32(buf, index);
        return IPAddress.NetworkToHostOrder((int)networkValue);
    }

    protected async Task ReceiveLoop()
    {
        try
        {
            var stream = new NetworkStream(Socket, ownsSocket: false);

            while (SocketConnected())
            {
                var decodedPacket = await new PacketCodec().ReadPacketAsync(stream, CancelToken.Token);

                if (decodedPacket == null)
                {
                    Logger.Info("Client disconnected");
                    break;
                }

                switch (decodedPacket.Framing)
                {
                    case PacketFraming.FourByteLittleEndianLength:
                    case PacketFraming.TwoByteBigEndianLength:
                        Framing = decodedPacket.Framing;
                        LogPacket("Recv", decodedPacket.CmdId, decodedPacket.Body.ToArray(),Framing);
                        await HandlePacket(decodedPacket.CmdId, decodedPacket.Body.ToArray());
                        break;

                    case PacketFraming.Control:
                        Logger.Info("Control packet received");
                        // Handle control packet if needed
                        break;

                    case PacketFraming.Unknown:
                        Logger.Warn("Unknown packet format received");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("ReceiveLoop cancelled");
        }
        catch (Exception ex)
        {
            Logger.Info($"ReceiveLoop error: {ex}");
        }
        finally
        {
            Socket.Close();
        }
        Stop();
    }

    private async Task HandlePacket(ushort opcode, byte[] payload)
    {
        var packetName = LogMap.GetValueOrDefault(opcode);
        if (DummyPacketNames.Contains(packetName!))
        {
            await SendDummy(packetName!);
            Logger.Info($"[Dummy] Send Dummy {packetName}");
            return;
        }

        // Find the Handler for this opcode
        var handler = HandlerManager.GetHandler(opcode);
        if (handler != null)
        {
            // Handle
            // Make sure session is ready for packets
            var state = State;
            try
            {
                await handler.OnHandle(this, payload, (ushort)DownStreamSeqNo);
            }
            catch (Exception e)
            {
                Logger.Error(e.Message, e);
            }
            return;
        }

        if (ConfigManager.Config.ServerOption.EnableDebug &&
                 ConfigManager.Config.ServerOption.DebugNoHandlerPacket && !IgnoreLog.Contains(opcode))
            Logger.Error($"No handler found for {packetName}({opcode})");

        //if (ConfigManager.Config.ServerOption.AutoSendResponseWhenNoHandler)
        //{
        //    await SendDummy(packetName);
        //}

    }

    private async Task SendDummy(string packetName)
    {
        var respName = packetName.Replace("Req", "Rsp"); // Get the response packet name
        if (respName == packetName) return; // do not send rsp when resp name = recv name
        var respOpcode = LogMap.FirstOrDefault(x => x.Value == respName).Key; // Get the response opcode

        // Send Rsp
        await SendPacket(respOpcode);
    }
}