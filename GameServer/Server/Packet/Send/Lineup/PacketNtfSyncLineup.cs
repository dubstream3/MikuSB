using MikuSB.Database.Lineup;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Send.Lineup;

public class PacketNtfSyncLineup : BasePacket
{

    public PacketNtfSyncLineup(LineupDataInfo lineup) : base(CmdIds.NtfSyncLineup)
    {
        var proto = new NtfSyncLineup
        {
            Lineup = lineup.ToProto()
        };

        SetData(proto);
    }
}
