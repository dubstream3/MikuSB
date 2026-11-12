using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Places the island level cost mission (operating_data TaskID[level-1]) into the reserved
/// slot 1. The client only asks when its own preconditions pass, but the server re-checks the
/// map, the level and the empty slot before writing.
/// </summary>
[CallGSApi("VirCaptureTask_DoCheckCostTask")]
public class VirCaptureTask_DoCheckCostTask : CallGSHandler<VirCaptureTaskDoCheckCostTaskParam>
{
    private static readonly Logger Log = new("VirCapture.Task.CostTask");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTaskDoCheckCostTaskParam req)
    {
        if (req == null || req.MapId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var mapId = (uint)req.MapId;

        if (!GameData.VirCaptureOperatingData.TryGetValue(mapId, out var operate))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var mapLevel = VirCaptureTaskState.GetMapLevel(player, mapId);
        if (mapLevel <= 0 || mapLevel > operate.TaskId.Count)
        {
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        var missionId = operate.TaskId[(int)(mapLevel - 1)];
        var cfg = VirCaptureTaskState.GetTask(missionId);
        if (cfg == null || !VirCaptureTaskState.PreReceived(player, cfg))
        {
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        var sync = new NtfSyncPlayer();
        if (VirCaptureTaskState.GetSlotTask(player, mapId, VirCaptureTaskState.RandomPosCost) == 0)
        {
            VirCaptureTaskState.SetSlotTask(player, mapId, VirCaptureTaskState.RandomPosCost, missionId, sync);
            DatabaseHelper.SaveDatabaseType(player.Data);
            Log.Info($"map {mapId} level {mapLevel}: cost mission {missionId} placed in slot 1");
        }

        var rsp = new JsonObject
        {
            ["nMapId"] = mapId,
            ["nTaskId"] = missionId
        };
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public sealed class VirCaptureTaskDoCheckCostTaskParam
{
    [JsonPropertyName("nMapId")] public int MapId { get; set; }
}
