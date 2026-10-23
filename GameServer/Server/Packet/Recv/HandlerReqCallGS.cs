using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv;

[Opcode(CmdIds.ReqCallGS)]
public class HandlerReqCallGS : Handler
{
    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = ReqCallGS.Parser.ParseFrom(data);
        await CallGSRouter.Route(connection, req, seqNo);
    }
}
