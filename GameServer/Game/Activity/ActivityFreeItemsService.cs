using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
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
    /// 切时代时把「当期赠送角色」直接发到号上。每个 DLC 期都带一条 Class=free_items 的特别邀请活动
    /// （DLC32 -&gt; 356 = 瑟瑞斯·悠游, DLC35 -&gt; 557 = 茉莉安·霄鹰），它的 CustomData 就是
    /// [[卡片],[后勤/货币]...]：卡片是 [1, detail, particular, level, count]，其余是同批赠送品。
    ///
    /// 这里复用玩家自己点「领取」时走的 ClaimAsync，所以后勤、货币、卡片的发放路径和客户端
    /// 领取完全一致，唯一的区别是时机：切时代即发、并且把 (50, id*10+3) 置 1，于是特别邀请的
    /// 打脸图不会再弹（奖励已经在包里），切走再切回也不会重复发（同一活动只发一次）。
    ///
    /// 只认「本期自己的」那条：开始时间必须落在本期 era 的 EnterStartTime 之后，否则 283 那种
    /// 2099 年才结束的常驻自选活动会在任何一期里被选中。
    /// </summary>
    public static async ValueTask<int> GrantEraGiftAsync(PlayerInstance player, NtfSyncPlayer? sync)
    {
        if (!DlcRuntimeOverrides.PinActive)
            return 0;

        var pinned = DlcRuntimeOverrides.GetPinnedActivity();
        if (pinned is null || string.IsNullOrWhiteSpace(pinned.EnterStartTime))
            return 0;

        // Bound the search by BOTH ends of the era: without the upper bound "latest start after the era
        // began" picked the next era's giveaway (pinning 32 handed out 557, the DLC35 gift, because its
        // 2026-02-11 start is also later than 32's floor), and since that row had already been received
        // the call returned silently - the 2026-11-05 "切期赠送角色不生效". The half-open check lives in
        // IsInsideServedEra so this selection and ClaimAsync can never drift apart again.
        ActivityExcel? gift = null;
        var giftStart = DateTime.MinValue;

        foreach (var row in GameData.ActivityData.Values)
        {
            if (!IsFreeItems(row))
                continue;

            // One membership definition for both the era grant and the request-driven claim: the row
            // must start inside [floor, stop) and must still be open when the era begins.
            if (!DlcRuntimeOverrides.IsInsideServedEra(row.StartTime, row.EndTime))
                continue;

            // Already received rows do not shadow a later, still-unreceived giveaway in the same era.
            if (player.Attributes.GetValue(GroupId, ReceiveSid(row.Id)) != 0)
                continue;

            var start = DlcRuntimeOverrides.ParseConfigTime(row.StartTime);
            if (start is null)
                continue;

            if (gift is null || start.Value > giftStart)
            {
                gift = row;
                giftStart = start.Value;
            }
        }

        if (gift is null)
        {
            Log.Info($"No giveaway activity starts inside era {pinned.Id}; nothing to grant.");
            return 0;
        }

        if (player.Attributes.GetValue(GroupId, ReceiveSid(gift.Id)) != 0)
        {
            Log.Info($"Era {pinned.Id} giveaway {gift.Id} ({gift.StartTime}) was already received by " +
                     $"uid={player.Uid}; nothing to grant.");
            return 0;
        }

        var before = player.CharacterManager.CharacterData.Characters.Select(c => c.TemplateId).ToHashSet();

        var granted = await ClaimAsync(player, gift, sync ?? new NtfSyncPlayer());
        Log.Info(
            $"Era {pinned.Id} giveaway {gift.Id} granted to uid={player.Uid}: {granted.Count} reward row(s).");

        // NtfSyncAttr alone does not put a brand-new character on the client's roster - the request
        // driven claim gets away with it because Activity_UpdateActiveWidget re-asks for the player,
        // but a switch has nobody asking. Push the added cards the same way GM /girl does.
        var added = player.CharacterManager.CharacterData.Characters
            .Where(c => !before.Contains(c.TemplateId))
            .ToList();
        if (added.Count > 0)
        {
            Log.Info($"Era {pinned.Id} giveaway {gift.Id} pushed {added.Count} new character(s) to uid={player.Uid}.");
            await player.SendPacket(new PacketNtfCallScript(added));
        }

        return granted.Count;
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
        // Era gate: only a giveaway that actually ran inside the served era is claimable. The old check
        // only rejected rows that ENDED before the era began, so every later version's giveaway (and
        // every undated permanent row) stayed claimable - the client then let the player collect the whole
        // history in one visit. Answering without a reward is what CloseSelf expects for a dead row.
        if (!DlcRuntimeOverrides.IsInsideServedEra(row.StartTime, row.EndTime))
        {
            Log.Info(
                $"Refusing free items activity {row.Id} for uid={player.Uid}: window {row.StartTime}..{row.EndTime} " +
                $"is outside the pinned DLC era (floor={DlcRuntimeOverrides.ActivationFloor()}, " +
                $"stop={DlcRuntimeOverrides.ActivationEraEnd()}).");
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

        // The receiver flag is the ONLY thing that stops the client from re-opening the splash, so it
        // must not be written until every configured row was actually granted. A missing card/weapon
        // template used to return true from RewardManager and this flag was set unconditionally, which
        // silently ate the giveaway (docs/contracts/activity-era-architecture.md 5.x).
        var expected = ReadRewardRows(row.CustomData)
            .Where(MikuSB.GameServer.Game.Reward.RewardManager.IsGrantableRow)
            .ToList();
        if (expected.Count == 0)
        {
            Log.Warn(
                $"Free items activity {row.Id} has no readable reward rows; refusing the claim and leaving " +
                $"the receive flag at 0 for uid={player.Uid}.");
            return [];
        }

        var rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(expected, sync);
        if (rewarded.Count < expected.Count)
        {
            Log.Warn(
                $"Free items activity {row.Id} only granted {rewarded.Count}/{expected.Count} reward row(s) " +
                $"for uid={player.Uid}; leaving the receive flag at 0 so the claim can be retried.");
            return rewarded;
        }

        ActivitySignService.SetAttr(player, sid, 1, sync);

        Log.Info(
            $"Free items activity {row.Id} claimed by uid={player.Uid}: {rewarded.Count} reward row(s) granted.");

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        return rewarded;
    }
}
