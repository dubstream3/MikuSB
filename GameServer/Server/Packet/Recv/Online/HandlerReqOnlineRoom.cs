using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Online;

/// <summary>
/// 海姆优化作战 (Rush party mode) opens its room through the native binding
/// me:OnlineCreateRoom / me:OnlineRoomStart, which sends ReqOnlineRoom(303) and then blocks on
/// RspOnlineRoom(304). MikuSB has no room subsystem, so without an answer the client sits on the
/// "connecting" spinner forever (observed 2026-10-05 18:09). Answer with an empty Rsp so the
/// client leaves the spinner and remember which activity was asked for, because the later
/// ReqOnlineRoomStart(305) has to know the level to send in NtfOnlineStart.
///
/// Payload (probed 2026-10-06): protobuf varints, field 1 = online id, field 2 = team id, e.g.
/// [0870100A] = online id 112 + team 10 and [0873100A] = online id 115 + team 10.
/// </summary>
[Opcode(CmdIds.ReqOnlineRoom)]
public class HandlerReqOnlineRoom : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoom");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var values = OnlineRoomService.Varints(data);
        if (values.Count > 0 && values[0] != 0) connection.OnlineRoomId = values[0];
        if (values.Count > 1) connection.OnlineRoomTeamId = values[1];

        Logger.Info($"Online room requested: {data.Length} byte(s) [{Convert.ToHexString(data)}] -> " +
                    $"onlineId={connection.OnlineRoomId} teamId={connection.OnlineRoomTeamId}.");

        await connection.SendPacket(CmdIds.RspOnlineRoom);

        // The native room layer keeps a roster and the online GameMode spawns every player's character
        // from it, so describe the room right away (an empty room aborts the later travel with
        // "InitNewPlayer :Team is Empty").
        await OnlineRoomService.PushRoomRoster(connection);

        // The room is created while the player is still browsing the activity UI, so hand the level over
        // now: the GameMode reads the online/level attributes when it builds its game state, and doing it
        // here means they are already in place by the time the player presses 出击 (ReqOnlineRoomStart).
        await OnlineRoomService.ApplyOnlineLevel(connection);
    }
}
