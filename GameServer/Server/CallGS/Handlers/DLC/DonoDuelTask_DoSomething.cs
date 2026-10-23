using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DonoDuelTask:CheckClientFinish (DLC/DonoDuel/DonoDuelTask.lua:513-520) detects
// objectives the client can verify on its own (objectives type 10, "visit a place N times") and
// reports them through ReqDoSomething -> {nCheckTaskId}; the callback
// (DonoDuelTask.lua:932-940) just refreshes the npc and the in-progress list. Accepting the report
// means pushing the progress field of attribute (186, nId) up to the mission's CheckValue so the
// mission turns claimable.
// Request:  { "nCheckTaskId": <missionId> }
// Response: {}
[CallGSApi("DonoDuelTask_DoSomething")]
public class DonoDuelTask_DoSomething : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var id = DonoDuelTaskState.ReadCheckTaskId(param);
        if (id == 0)
            return Task.FromResult(CallGSResult.Ok("{}"));

        var sync = new NtfSyncPlayer();

        // A client reported objective only makes sense on a mission the player already holds.
        if (!DonoDuelTaskState.IsAccepted(player, id))
            DonoDuelTaskState.MarkAccepted(player, id, sync);

        var checkValue = DonoDuelTaskState.CheckValue(id);
        DonoDuelTaskState.MarkProgress(player, id, checkValue, sync);
        DonoDuelTaskState.Save(player);

        Logger.GetByClassName().Info(
            $"Donoduel mission {id} reported complete by the client (progress -> {checkValue}) for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}
