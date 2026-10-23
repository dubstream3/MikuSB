using Google.Protobuf;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(CmdIds.ReqRename)]
public class HandlerReqRename : Handler
{
    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var player = connection.Player;
        if (player != null)
        {
            var requestedName = ParseDisplayName(data);
            player.SetDisplayName(requestedName);
            DatabaseHelper.UpdateInstance(player.Data);
            await player.OnHeartBeat();
        }

        await connection.SendPacket(CmdIds.RspRename);
    }

    private static string? ParseDisplayName(byte[] data)
    {
        if (data.Length == 0)
            return null;

        try
        {
            var input = new CodedInputStream(data);
            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0)
                    break;

                if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
                {
                    var value = input.ReadString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
                else
                {
                    input.SkipLastField();
                }
            }
        }
        catch
        {
            // Fall back to raw UTF-8 payload handling below.
        }

        try
        {
            var rawText = System.Text.Encoding.UTF8.GetString(data).Trim('\0', ' ', '\r', '\n', '\t');
            return string.IsNullOrWhiteSpace(rawText) ? null : rawText;
        }
        catch
        {
            return null;
        }
    }
}
