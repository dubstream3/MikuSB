using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Misc;

// Sent by the client when the player switches the chat channel. MikuSB only exposes world
// channel 1, so nothing changes server side, but without a handler the packet router logs
// "No handler found for ReqChangeWorldChannel(321)" on every connect.
[Opcode(CmdIds.ReqChangeWorldChannel)]
public class HandlerReqChangeWorldChannel : Handler
{
    private static readonly Logger Logger = new("ReqChangeWorldChannel");

    public override Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"World channel change requested ({data.Length} bytes), staying on channel 1.");
        return Task.CompletedTask;
    }
}
