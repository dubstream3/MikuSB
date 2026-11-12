using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 爬塔关卡结算（VirCaptureTower_LevelSettlement, settle channel）.
///
/// The client assigns the whole response to `Launch.tbAward` (VirCaptureTowerLogic.lua:34-40) and the
/// success page walks it as "array of category arrays of [g,d,p,l,n] rows", so the drops have to be
/// nested the same way the DLC chapter settlement nests them.
///
/// First-clear detection happens *before* the pass counter is bumped: the old behaviour wrote
/// `PassGid` first and then asked "is this the first pass?", which was always false - that is how all
/// 70 levels lost their FirstDropID rewards. Stars keep the historical 3-bit mask the client sends.
/// </summary>
[CallGSApi("VirCaptureTower_LevelSettlement")]
public class VirCaptureTower_LevelSettlement : CallGSHandler
{
    private const uint LaunchLevelStateGroupId = AttrIds.Tower.LevelStateGid;
    private const uint LaunchPassGroupId = AttrIds.Tower.PassGid;
    private const uint PassedFlagBit = 1u << 8;

    private static readonly Logger Logger = new("VirCaptureTower");

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var (response, sync) = await HandleSettlementAsync(context.Connection.Player!, JsonNode.Parse(param));
        return CallGSResult.Ok(response.ToJsonString(), sync);
    }

    public static async Task<(JsonNode Response, NtfSyncPlayer Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<VirCaptureTowerSettlementParam>(CallGSJson.Options);
        if (req == null || req.LevelId == 0)
        {
            Logger.Error($"Invalid vircapture tower settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var sync = new NtfSyncPlayer();

        // The pass counter is read before it is written: the value is the only first-clear witness.
        var passAttr = player.Attributes.GetOrCreate(LaunchPassGroupId, (uint)req.LevelId);
        var isFirstClear = passAttr.Val == 0;

        var levelStateAttr = player.Attributes.GetOrCreate(LaunchLevelStateGroupId, (uint)req.LevelId);
        levelStateAttr.Val |= MergeStarMask(req.StarMask) | PassedFlagBit;
        player.Attributes.SyncTo(sync, levelStateAttr);

        passAttr.Val = Math.Max(1u, passAttr.Val + 1);
        player.Attributes.SyncTo(sync, passAttr);

        JsonArray awards = [];
        if (GameData.VirCaptureTowerData.TryGetValue((uint)req.LevelId, out var levelConfig))
        {
            // Stable per-level seed: the same clear always resolves the same random drop, and a
            // crafted nSeed cannot reroll a reward.
            var seed = (uint)req.LevelId * 2654435761u ^ (isFirstClear ? 0xA5A5_5A5Au : 0u);
            awards = await player.RewardManager.GrantLevelRewardsAsync(levelConfig, isFirstClear, seed, sync);
        }
        else
        {
            Logger.Warn($"tower level {req.LevelId} has no reward config; settlement paid no drops uid={player.Uid}");
        }

        Logger.Info(
            $"VirCaptureTower settlement saved. uid={player.Uid} levelId={req.LevelId} starMask={req.StarMask} " +
            $"firstClear={isFirstClear} levelStateVal={levelStateAttr.Val} passVal={passAttr.Val} " +
            $"awardGroups={awards.Count}");

        // Drops land in InventoryData and the level-up gifts in CharacterData; RewardManager only
        // stages them in the sync packet, so both stores have to be flushed here as well. Saving
        // only player.Data left a reconnect (or a restart) without the items the success page
        // had just shown.
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        return (awards, sync);
    }

    private static uint MergeStarMask(int starMask)
    {
        uint result = 0;
        for (var i = 0; i < 3; i++)
        {
            if (((starMask >> i) & 1) != 0)
                result |= 1u << i;
        }

        return result;
    }
}

internal sealed class VirCaptureTowerSettlementParam
{
    [JsonPropertyName("nID")] public int LevelId { get; set; }

    [JsonPropertyName("nStar")] public int StarMask { get; set; }
}
