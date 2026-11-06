using MikuSB.Configuration;
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
///
/// The room then waits for other players: the client only asks for a start (305) once its own
/// matchmaking thinks the room is full, so a lone player stays on the spinner (the 2026-11-05
/// "点匹配卡住"). <see cref="AutoStartSoloRoomAsync"/> covers that case by sending the very same
/// NtfOnlineStart a 305 would have produced, but only while the room really holds just one member.
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

        var autoStartSeconds = ConfigManager.Config.ServerOption.OnlineRoomAutoStartSeconds;
        if (autoStartSeconds > 0) _ = AutoStartSoloRoomAsync(connection, autoStartSeconds);
    }

    /// <summary>
    /// Starts a one-player party room after <paramref name="seconds"/>, mirroring
    /// <see cref="HandlerReqOnlineRoomStart"/>: the client never sends 305 on its own when it is waiting
    /// for teammates, so without this the matchmaking spinner never ends. Anything that is not a plain
    /// solo room (a LAN guest, a second member, an already started room) is left untouched.
    /// </summary>
    private static async Task AutoStartSoloRoomAsync(Connection connection, int seconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));

            if (connection.OnlineRoomStarted || connection.Player is null) return;
            if (OnlineRoomService.RoomMembers(connection).Count != 1) return;

            var decision = OnlineRoomService.DecideRoomAddress(connection, out var roomAddr);
            if (decision != OnlineRoomService.RoomAddressDecision.Send)
            {
                Logger.Info($"Solo auto-start skipped: room address decision is {decision}.");
                return;
            }

            connection.OnlineRoomStarted = true;
            Logger.Info($"Solo auto-start: room {connection.OnlineRoomId} still has one member after " +
                        $"{seconds}s, starting the level instead of waiting for teammates.");

            await OnlineRoomService.PushRoomRoster(connection);
            await OnlineRoomService.ApplyOnlineLevel(connection);
            await connection.SendPacket(CmdIds.NtfOnlineStart,
                new NtfOnlineStart { RoomId = 1, RoomAddr = roomAddr });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Solo auto-start failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
