using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(CmdIds.NtfSetAttr)]
public class HandlerNtfSetAttr : Handler
{
    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = NtfSetAttr.Parser.ParseFrom(data);
        var player = connection.Player;
        if (player == null)
        {
            // Client writes attributes during login; a session that has not finished logging in yet
            // (for example a reconnect that arrived while the server was still loading) must not crash.
            return;
        }

        player.Attributes.Set(req.Gid, req.Sid, req.Val);
        DatabaseHelper.SaveDatabaseType(player.Data);
        await player.OnHeartBeat();
    }
}
