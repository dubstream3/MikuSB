using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 特别邀请 / 自选 SSR - the "free_items" activity class (activity/activities.json, Class=free_items,
/// ModeId 1028). Two widgets drive it and both follow the same contract:
///
///   UMG/ActivityInvited/umg_Invited01.lua (the login splash, opened by Activity/Activity.lua:801
///   CheckSpInvite)
///     - only opens while Activity.GetDiyData(nId, 1) == 0, i.e. while attribute (50, nId*10 + 3) is 0;
///     - OnClose (:44-50) calls Activity.OnOpen() again, so while that flag is still 0 the splash
///       re-opens on the spot - the "特别邀请 keeps popping and cannot be closed" loop;
///     - GetReward (:178-209) registers Event.ActivityReward and calls Activity.Quest_GetAward({nId}),
///       i.e. CallGS Activity_GetAward with nId only (no nType, no nQuestId);
///     - CloseSelf (:59-105) plays the gacha splash for the first reward matching tbCustomData and then
///       hands tbRewards to Item.Gain.
///   UMG/Activity/Widgets/uw_activity_template31.lua (自选 SSR) uses the same flag (ReceiveID = 1) and
///   sends the same request.
///
/// Event.ActivityReward is fired by exactly one callback, s2c Activity_UpdateActiveWidget
/// (Activity/Activity.lua:1671-1680), which also closes the connection spinner that Quest_GetAward
/// opened - so a claim must be answered under THAT name with tbRewards in the payload. Answering the
/// generic ActivityQuest_GetReward (what Activity_GetAward used to do for nType 0) delivers nothing and
/// leaves the flag at 0, which is why the splash could not be dismissed: on 2026-10-06 the client
/// re-opened it 89 times in 27 minutes, one Activity_GetAward{nId=97} per round trip.
///
/// The reward is the activity row's CustomData: [[G,D,P,L,Count,Splash], ...] (row 97) or the flat
/// single-row form [G,D,P,L,Count,Splash] (row 283). The sixth value is the splash duration the client
/// reads as tbItem[6] and is not part of the item, so only the first five are granted.
/// </summary>
public static class ActivityFreeItemsService
{
    public const uint GroupId = AttrIds.Activity.GroupId; // Activity.nGroupId
    public const uint IdPosStep = 10;                     // Activity.IdPosStep

    /// <summary>Activity/Activity.lua:159 GetDiyTaskId(nId, nPos) = nId * 10 + nPos + 2.</summary>
    private const uint DiySidOffset = 2;

    /// <summary>umg_Invited01.ReceiveID / uw_activity_template31.ReceiveID - both use DIY slot 1.</summary>
    public const uint ReceivePos = 1;

    public const string FreeItemsClass = "free_items";

    private static readonly Logger Log = new("ActivityFreeItems");

    /// <summary>Attribute slot the client reads as "this giveaway has already been received".</summary>
    public static uint ReceiveSid(uint activityId) => activityId * IdPosStep + DiySidOffset + ReceivePos;

    public static bool IsFreeItems(ActivityExcel row) =>
        string.Equals(row.Class, FreeItemsClass, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flattens CustomData into the row shape RewardManager.GrantConfiguredRewardsAsync expects. The
    /// shipped table mixes two shapes: a list of reward rows, and a single flat row (row 283). Rows
    /// shorter than five values or with a zero count are dropped rather than throwing.
    /// </summary>
    public static List<uint[]> ReadRewardRows(JToken? customData)
    {
        if (customData is not JArray data || data.Count == 0)
            return [];

        if (data[0] is JArray)
            return DlcRuntimeOverrides.ReadRewardRows(data);

        if (data.Count < 5)
            return [];

        var row = new uint[5];
        for (var i = 0; i < 5; i++)
            row[i] = DlcRuntimeOverrides.ReadUint(data[i]);

        return row[0] != 0 && row[4] != 0 ? [row] : [];
    }

    /// <summary>
    /// Grants the row's CustomData once and flips the receive flag the client gates on. Called with the
    /// login sync object so the flag reaches the client in the same packet as the claim answer - without
    /// it the client's cached (50, nId*10 + 3) stays 0 and the splash re-opens immediately.
    /// </summary>
    public static async ValueTask<JsonArray> ClaimAsync(
        PlayerInstance player,
        ActivityExcel row,
        NtfSyncPlayer sync)
    {
        // Era gate (mirrors Activity_Refresh): a giveaway whose window ended before the pinned DLC
        // began belongs to an earlier version and is not claimable at all, exactly like on the live
        // server. Answering with no reward is what the client's CloseSelf expects for a dead row.
        if (DlcRuntimeOverrides.IsHistoricalWindow(row.EndTime))
        {
            Log.Info(
                $"Refusing free items activity {row.Id} for uid={player.Uid}: window {row.StartTime}..{row.EndTime} " +
                $"ended before the pinned DLC era (floor={DlcRuntimeOverrides.ActivationFloor()}).");
            return [];
        }

        var sid = ReceiveSid(row.Id);
        if (player.Attributes.GetValue(GroupId, sid) != 0)
        {
            // Already received (a reconnect can re-send the request): report no reward, the client
            // closes the splash either way.
            Log.Info($"Free items activity {row.Id} already received by uid={player.Uid}; answering without a reward.");
            return [];
        }

        var rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(ReadRewardRows(row.CustomData), sync);
        ActivitySignService.SetAttr(player, sid, 1, sync);

        Log.Info(
            $"Free items activity {row.Id} claimed by uid={player.Uid}: {rewarded.Count} reward row(s) granted.");

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        return rewarded;
    }
}
