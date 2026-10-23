using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(CmdIds.NtfSetStrAttr)]
public class HandlerNtfSetStrAttr : Handler
{
    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = NtfSetStrAttr.Parser.ParseFrom(data);
        var player = connection.Player!;

        player.SetStrAttr(req.Gid, req.Sid, req.Val);
        DatabaseHelper.SaveDatabaseType(player.Data);

        await player.OnHeartBeat();
    }
}
