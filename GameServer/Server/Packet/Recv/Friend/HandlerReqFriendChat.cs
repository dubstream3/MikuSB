using Google.Protobuf;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Friend;

[Opcode(CmdIds.ReqFriendChat)]
public class HandlerReqFriendChat : Handler
{
    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = ChatMsg.Parser.ParseFrom(data);
        var json = JsonFormatter.Default.Format(req);
        Logger.GetByClassName().Debug($"{json}");

        await connection.Player!.ReceiveMessage((uint)connection.Player!.Uid, (uint)req.Recver, req.Text, (uint)req.Emoji);
        await connection.SendPacket(CmdIds.RspFriendChat);
    }
}
