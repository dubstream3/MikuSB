using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.ClimbTower;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

/// <summary>
/// Star milestone payout of DLC 3.3 爬塔 (ClimbTower.GetAward, DLC/ClimbTower/ClimbTower.lua:672-684), the
/// single-claim path as well as the 一键领取 button (uw_climbtower_reward_level.lua:37-47 builds tbList from
/// every milestone whose flag is still 0).
///
/// Reply contract (uw_climbtower_reward_level.OnGetAward, :49-66) reads exactly:
///   tbAwardList - list of [g,d,p,l,count] rows, merged by Launch.Merg and fed to Item.Gain;
///   tbList      - the claimed ids, echoed back so the widget can refresh one row (length 1) or rebuild
///                 the whole list (length > 1).
/// An empty/absent tbList would silently drop the rewards, so ids are always echoed.
/// </summary>
[CallGSApi("ClimbTower_Award")]
public class ClimbTower_Award : CallGSHandler<ClimbTowerAwardParam>
{
    private static readonly Logger Log = new(nameof(ClimbTower_Award));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerAwardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var season = ClimbTowerService.SelectSeason(DateTime.Now);
        if (season == null)
        {
            Log.Warn("ClimbTower_Award: no season row loaded from dlc/climb_tower/climbtower_season.json.");
            return CallGSResult.Error("ui.TxtNotOpen");
        }

        var sync = new NtfSyncPlayer();
        var claimed = new JsonArray();
        var awards = new JsonArray();
        var stars = ClimbTowerService.TotalStars(player, season);

        foreach (var id in request.Ids)
        {
            if (!GameData.ClimbTowerRewardData.TryGetValue(id, out var cfg))
            {
                Log.Warn($"ClimbTower_Award: milestone {id} is not in climbtower_reward.json.");
                return CallGSResult.Error("error.BadParam");
            }

            if (stars < cfg.TargetNum)
                return CallGSResult.Error("ui.TxtNotOpen", sync);

            if (ClimbTowerService.IsAwarded(player, id))
                return CallGSResult.Error("achievement.RewardGeted", sync);

            var rows = cfg.Rows;
            if (rows.Count > 0)
            {
                var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
                foreach (var row in granted)
                    awards.Add(row?.DeepClone());
            }

            ClimbTowerService.MarkAwarded(player, id, sync);
            claimed.Add(id);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        if (claimed.Count > 0)
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        Log.Info($"ClimbTower_Award: uid={player.Uid} claimed [{string.Join(",", claimed)}] at " +
                 $"{stars} stars ({awards.Count} reward rows).");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbAwardList"] = awards,
            ["tbList"] = claimed
        }, sync);
    }
}

public sealed class ClimbTowerAwardParam
{
    [JsonPropertyName("tbList")] public List<uint> Ids { get; set; } = [];
}
