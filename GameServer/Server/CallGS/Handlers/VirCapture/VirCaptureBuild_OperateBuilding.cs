using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Runs one operating round of the blueprint island: pays the summed building revenue into the
/// island coin and bumps the total / daily / level operate counters (gid 161 map block).
/// The revenue is recomputed on the server from building.json; buffs and staff attribute
/// bonuses are not part of the figure yet (see docs/progress/072 P1 notes).
/// </summary>
[CallGSApi("VirCaptureBuild_OperateBuilding")]
public class VirCaptureBuild_OperateBuilding : CallGSHandler<VirCaptureBuildOperateBuildingParam>
{
    private static readonly Logger Log = new("VirCapture.Build.OperateBuilding");
    private const uint DefaultDailyLimit = 3000;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureBuildOperateBuildingParam req)
    {
        if (req == null || req.MapId <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;
        var mapId = (uint)req.MapId;
        if (!GameData.VirCaptureOperatingData.TryGetValue(mapId, out var operate) ||
            !VirCaptureBuildState.HostsBuildings(mapId))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        if (VirCaptureBuildState.GetMapLevel(player, mapId) <= 0)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var sync = new NtfSyncPlayer();
        VirCaptureBuildState.EnsureDailyCounter(player, mapId, sync);
        var daily = VirCaptureBuildState.GetMapValue(player, mapId, VirCaptureBuildState.OffDailyOperate);
        if (daily >= DefaultDailyLimit)
            return Task.FromResult(CallGSResult.Error("error.condition_limit", sync));

        var revenue = VirCaptureBuildState.MapRevenue(player, mapId, out var cost);
        VirCaptureBuildState.AddCash(player, operate.CashId, revenue, sync);

        VirCaptureBuildState.SetMapValue(player, mapId, VirCaptureBuildState.OffTotalOperate,
            VirCaptureBuildState.GetMapValue(player, mapId, VirCaptureBuildState.OffTotalOperate) + 1, sync);
        VirCaptureBuildState.SetMapValue(player, mapId, VirCaptureBuildState.OffDailyOperate, daily + 1, sync);
        VirCaptureBuildState.SetMapValue(player, mapId, VirCaptureBuildState.OffLevelOperate,
            VirCaptureBuildState.GetMapValue(player, mapId, VirCaptureBuildState.OffLevelOperate) + 1, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var rsp = new JsonObject
        {
            ["nMapId"] = (int)mapId,
            ["nRevenue"] = revenue,
            ["nCost"] = cost
        };
        Log.Info($"map {mapId} operate: revenue {revenue}, cost {cost}, daily {daily + 1}");
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public class VirCaptureBuildOperateBuildingParam
{
    [JsonPropertyName("nMapId")] public int MapId { get; set; }
}
