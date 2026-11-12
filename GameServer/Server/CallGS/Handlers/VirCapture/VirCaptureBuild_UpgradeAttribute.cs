using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Upgrades one of the three building attributes (gid 161 <c>10000 + id*30 + 6 + index - 1</c>).
/// The price comes from the <c>AttrN</c> dictionary of building.json, keyed by attribute value.
/// </summary>
[CallGSApi("VirCaptureBuild_UpgradeAttribute")]
public class VirCaptureBuild_UpgradeAttribute : CallGSHandler<VirCaptureBuildUpgradeAttributeParam>
{
    private static readonly Logger Log = new("VirCapture.Build.UpgradeAttribute");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureBuildUpgradeAttributeParam req)
    {
        if (req == null || req.Id <= 0 || req.AttrIndex is <= 0 or > 3)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;
        var buildId = (uint)req.Id;
        var attrIndex = (uint)req.AttrIndex;
        var cfg = VirCaptureBuildState.GetBuilding(buildId);
        if (cfg == null || !GameData.VirCaptureOperatingData.TryGetValue(cfg.MapId, out var operate))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var level = VirCaptureBuildState.GetBuildValue(player, buildId, VirCaptureBuildState.OffLevel);
        if (level <= 0)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var attrLevel = VirCaptureBuildState.AttributeLevel(player, buildId, attrIndex);
        if (attrLevel >= cfg.MaxAttributeLevel(attrIndex))
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var sync = new NtfSyncPlayer();
        var cost = cfg.AttributeUpgradeCost(attrIndex, attrLevel);
        if (!VirCaptureBuildState.TryPay(player, operate.CashId, cost, sync, out var error))
        {
            Log.Info($"build {buildId} attr {attrIndex} level {attrLevel} denied: {error} (need {cost})");
            return Task.FromResult(CallGSResult.Error(error, sync));
        }

        VirCaptureBuildState.SetBuildValue(player, buildId,
            VirCaptureBuildState.OffAttr1 + attrIndex - 1, attrLevel + 1, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var rsp = new JsonObject
        {
            ["nId"] = (int)buildId,
            ["nAttrIndx"] = (int)attrIndex,
            ["nAttrLevel"] = (int)(attrLevel + 1)
        };
        Log.Info($"build {buildId} attr {attrIndex} level {attrLevel} -> {attrLevel + 1} cost {cost}");
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public class VirCaptureBuildUpgradeAttributeParam
{
    [JsonPropertyName("nId")] public int Id { get; set; }
    [JsonPropertyName("nAttrIndx")] public int AttrIndex { get; set; }
}
