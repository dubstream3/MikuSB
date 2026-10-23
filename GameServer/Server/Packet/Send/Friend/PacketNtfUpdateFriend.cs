using MikuSB.GameServer.Game.Player;
using MikuSB.TcpSharp;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Send.Friend;

public class PacketNtfUpdateFriend : BasePacket
{
    public PacketNtfUpdateFriend(PlayerInstance player) : base(CmdIds.NtfUpdateFriend)
    {
        var proto = new PlayerProfileArray
        {
            List = { player.ToServerFriendProto() }
        };

        SetData(proto);
    }
}
