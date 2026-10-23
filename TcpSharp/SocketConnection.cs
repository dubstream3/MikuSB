using Google.Protobuf;
using Google.Protobuf.Reflection;
using MikuSB.Enums.Packet;
using MikuSB.Proto;
using MikuSB.Util;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace MikuSB.TcpSharp;

public class SocketConnection
{
    public static readonly ConcurrentBag<int> BannedPackets = [];
    private static readonly Logger Logger = new("GameServer");
    public static readonly ConcurrentDictionary<int, string> LogMap = [];

    public static readonly ConcurrentBag<int> IgnoreLog =
    [
        
    ];
    protected readonly CancellationTokenSource CancelToken;
    protected readonly Socket Socket;
    public readonly IPEndPoint RemoteEndPoint;

    public string DebugFile = "";
    public bool IsOnline = true;
    public StreamWriter? Writer;

    public int DownStreamSeqNo;
    public int UpStreamSeqNo;

    public PacketFraming Framing;

    public SocketConnection(Socket socket, IPEndPoint remote)
    {
        Socket = socket;
        RemoteEndPoint = remote;
        CancelToken = new CancellationTokenSource();

        Start();
    }
    public SessionStateEnum State { get; set; } = SessionStateEnum.INACTIVE;
    internal long ConnectionId { get; set; }

    public virtual void Start()
    {
        Logger.Info($"New connection from {RemoteEndPoint}.");
        State = SessionStateEnum.WAITING_FOR_TOKEN;
    }

    public virtual void Stop(bool isServerStop = false)
    {
        try
        {
            Socket?.Shutdown(SocketShutdown.Both);
        }
        catch { }
        finally
        {
            Socket?.Close();
            Socket?.Dispose();
        }
        try
        {
            CancelToken.Cancel();
            CancelToken.Dispose();
        }
        catch
        {
        }

        IsOnline = false;
    }

    public bool SocketConnected()
    {
        try
        {
            return !((Socket.Poll(1000, SelectMode.SelectRead) && (Socket.Available == 0)) || !Socket.Connected);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void LogPacket(string sendOrRecv, ushort opcode, byte[] payload, PacketFraming framing)
    {
        if (!ConfigManager.Config.ServerOption.EnableDebug) return;
        try
        {
            //Logger.DebugWriteLine($"{sendOrRecv}: {Enum.GetName(typeof(OpCode), opcode)}({opcode})\r\n{Convert.ToHexString(payload)}");
            if (IgnoreLog.Contains(opcode)) return;
            if (!ConfigManager.Config.ServerOption.DebugDetailMessage) throw new Exception(); // go to catch block
            var typ = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly => assembly.GetName().Name == "MikuProto")!.GetTypes()
                .First(t => t.Name == $"{LogMap[opcode]}"); //get the type using the packet name
            var descriptor =
                typ.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(
                    null, null) as MessageDescriptor; // get the static property Descriptor
            var packet = descriptor?.Parser.ParseFrom(payload);
            var formatter = JsonFormatter.Default;
            var asJson = formatter.Format(packet);
            var output = $"{sendOrRecv}: {LogMap[opcode]}({opcode}) ({framing})\r\n{asJson}";
            if (ConfigManager.Config.ServerOption.DebugMessage)
                Logger.Debug(output);
            InGameConsoleBridge.PublishPacketLog(output);
            if (DebugFile == "" || !ConfigManager.Config.ServerOption.SavePersonalDebugFile) return;
            var sw = GetWriter();
            sw.WriteLine($"[{DateTime.Now:HH:mm:ss}] [GameServer] [DEBUG] " + output);
            sw.Flush();
        }
        catch
        {
            var output = $"{sendOrRecv}: {LogMap.GetValueOrDefault(opcode, "UnknownPacket")}({opcode})";
            if (ConfigManager.Config.ServerOption.DebugMessage)
                Logger.Debug(output);
            InGameConsoleBridge.PublishPacketLog(output);
            if (DebugFile != "" && ConfigManager.Config.ServerOption.SavePersonalDebugFile)
            {
                var sw = GetWriter();
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss}] [GameServer] [DEBUG] " + output);
                sw.Flush();
            }
        }
    }

    private StreamWriter GetWriter()
    {
        // Create the file if it doesn't exist
        var file = new FileInfo(DebugFile);
        if (!file.Exists)
        {
            Directory.CreateDirectory(file.DirectoryName!);
            File.Create(DebugFile).Dispose();
        }

        Writer ??= new StreamWriter(DebugFile, true);
        return Writer;
    }

    public async Task SendPacket(byte[] packet)
    {
        try
        {
            if (Socket.Connected)
            {
                await Socket.SendAsync(
                    new ArraySegment<byte>(packet),
                    SocketFlags.None,
                    CancelToken.Token
                );
            }
        }
        catch
        {
            // ignore
        }
    }

    public async Task SendPacket(BasePacket packet, ushort seqNo = 0)
    {
        // Test
        if (packet.CmdId <= 0)
        {
            Logger.Debug("Tried to send packet with missing cmd id!");
            return;
        }

        // DO NOT REMOVE (unless we find a way to validate code before sending to client which I don't think we can)
        if (BannedPackets.Contains(packet.CmdId)) return;
        LogPacket("Send", packet.CmdId, packet.Body,Framing);
        byte[] packetBytes = new PacketCodec().Encode(packet.CmdId, packet.Body,Framing);
        try
        {
            await SendPacket(packetBytes);
        }
        catch
        {
            // ignore
        }
    }

    public async Task SendPacket(int cmdId)
    {
        await SendPacket(new BasePacket((ushort)cmdId));
    }

    public async Task SendPacket(int cmdId, ushort seqNo)
    {
        var packet = new BasePacket((ushort)cmdId);
        packet.SeqNo = seqNo;
        await SendPacket(packet);
    }

    public async Task SendPacket(int cmdId, IMessage msg, ushort seqNo = 0)
    {
        var packet = new BasePacket((ushort)cmdId);
        packet.SetData(msg);
        packet.SeqNo = seqNo;
        await SendPacket(packet);
    }
}