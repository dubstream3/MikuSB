using MikuSB.GameServer.Game.Player;
using MikuSB.TcpSharp;
using MikuSB.Proto;
using MikuSB.Util;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Server.Packet.Send.Login;

// Reconnect response: same player snapshot as RspLogin, but with the reconnect opcode so the
// client can resume its session after the socket was dropped (e.g. after a server rebuild).
public class PacketRspReconnect : BasePacket
{
    private static readonly Logger Logger = new("RspReconnect");

    public PacketRspReconnect(PlayerInstance player, bool includeSupportCards = true) : base(CmdIds.RspReconnect)
    {
        var proto = new RspReconnect
        {
            Timestamp = (uint)Extensions.GetUnixSec(),
            WorldChannel = 1,
            TimeZone = 8,
            SessionId = "",
            Data = player.ToPlayerProto(includeSupportCards),
            NeedRename = false
        };

        var bytes = Google.Protobuf.MessageExtensions.ToByteArray(proto);
        Logger.Info($"RspReconnect content: attrs={player.Attributes.All.Count}, " +
                    $"protoAttrs={proto.Data.Attrs.Count}, protoItems={proto.Data.Items.Count}");
        SetData(bytes);
    }
}
