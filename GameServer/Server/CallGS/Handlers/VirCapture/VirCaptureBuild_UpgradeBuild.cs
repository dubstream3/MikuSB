using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Upgrades one blueprint island building by one level and charges the island coin
/// (<c>operating_data.Cashid</c>). Level 0 means "build it", so the same handler covers the
/// first construction step; the level is written to gid 161 <c>10000 + id*30 + 1</c>.
/// </summary>
[CallGSApi("VirCaptureBuild_UpgradeBuild")]
public class VirCaptureBuild_UpgradeBuild : CallGSHandler<VirCaptureBuildUpgradeBuildParam>
{
    private static readonly Logger Log = new("VirCapture.Build.UpgradeBuild");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureBuildUpgradeBuildParam req)
    {
        if (req == null || req.Id <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;
        var buildId = (uint)req.Id;
        var cfg = VirCaptureBuildState.GetBuilding(buildId);
        if (cfg == null || !GameData.VirCaptureOperatingData.TryGetValue(cfg.MapId, out var operate))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (VirCaptureBuildState.GetMapLevel(player, cfg.MapId) <= 0)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var level = VirCaptureBuildState.GetBuildValue(player, buildId, VirCaptureBuildState.OffLevel);
        if (level >= cfg.MaxLevel)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var cost = cfg.UpgradeCost(level);
        var sync = new NtfSyncPlayer();
        if (!VirCaptureBuildState.TryPay(player, operate.CashId, cost, sync, out var error))
        {
            Log.Info($"build {buildId} level {level} -> {level + 1} denied: {error} (need {cost})");
            return Task.FromResult(CallGSResult.Error(error, sync));
        }

        VirCaptureBuildState.SetBuildValue(player, buildId, VirCaptureBuildState.OffLevel, level + 1, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var rsp = new JsonObject { ["nId"] = (int)buildId, ["nLevel"] = (int)(level + 1) };
        Log.Info($"build {buildId} level {level} -> {level + 1} cost {cost}");
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public class VirCaptureBuildUpgradeBuildParam
{
    [JsonPropertyName("nId")] public int Id { get; set; }
}
