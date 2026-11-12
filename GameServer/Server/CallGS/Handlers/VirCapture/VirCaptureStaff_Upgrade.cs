using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Raises one island staff member by a level and charges <c>Consume[level - 1]</c> in the island
/// coin. The level lives in gid 162 <c>100 + id*20 + 1</c>.
/// </summary>
[CallGSApi("VirCaptureStaff_Upgrade")]
public class VirCaptureStaff_Upgrade : CallGSHandler<VirCaptureStaffUpgradeParam>
{
    private static readonly Logger Log = new("VirCapture.Staff.Upgrade");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureStaffUpgradeParam req)
    {
        if (req == null || req.MapId <= 0 || req.StaffId <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;
        var mapId = (uint)req.MapId;
        var staffId = (uint)req.StaffId;
        var cfg = VirCaptureStaffState.Config(staffId);
        if (cfg == null || !GameData.VirCaptureOperatingData.TryGetValue(mapId, out var operate))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var level = VirCaptureStaffState.Level(player, staffId);
        if (level == 0 || level >= cfg.MaxLevel)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var sync = new NtfSyncPlayer();
        var cost = cfg.UpgradeCost(level);
        if (!VirCaptureBuildState.TryPay(player, operate.CashId, cost, sync, out var error))
        {
            Log.Info($"staff {staffId} level {level} denied: {error} (need {cost})");
            return Task.FromResult(CallGSResult.Error(error, sync));
        }

        VirCaptureStaffState.Set(player, staffId, VirCaptureStaffState.OffLevel, level + 1, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);
        Log.Info($"staff {staffId} level {level} -> {level + 1} cost {cost}");
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nStaffId"] = (int)staffId }.ToJsonString(), sync));
    }
}

public class VirCaptureStaffUpgradeParam
{
    [JsonPropertyName("nMapId")] public int MapId { get; set; }
    [JsonPropertyName("nStaffId")] public int StaffId { get; set; }
}
