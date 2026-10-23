using Google.Protobuf;
using MikuSB.Database.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(CmdIds.NtfReadItem)]
public class HandlerNtfReadItem : Handler
{
    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = IDArray.Parser.ParseFrom(data);
        var json = JsonFormatter.Default.Format(req);
        Logger.GetByClassName().Debug($"{json}");
    }
}
