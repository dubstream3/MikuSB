using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DonoDuelTask:ReqAccept (DLC/DonoDuel/DonoDuelTask.lua:943-976) sends {nId} and
// the callback (DonoDuelTask.lua:978-997) only needs {nId} back without an sError to refresh the npc
// and play the accept story. Accept is recorded as bit0 of attribute (186, nId), which is exactly
// what DonoDuelTask:GetAccept (DonoDuelTask.lua:182-196) reads back.
// Request:  { "nId": <missionId> }
// Response: { "nId": <missionId> }
[CallGSApi("DonoDuelTask_Accept")]
public class DonoDuelTask_Accept : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var id = DonoDuelTaskState.ReadMissionId(param);
        if (id == 0 || !GameData.DonoDuelStoryTaskData.ContainsKey(id))
            return Task.FromResult(CallGSResult.Ok("{}"));

        var sync = new NtfSyncPlayer();
        DonoDuelTaskState.MarkAccepted(player, id, sync);
        DonoDuelTaskState.Save(player);

        Logger.GetByClassName().Info($"Donoduel mission {id} accepted for uid={player.Uid}.");
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = id }, sync));
    }
}
