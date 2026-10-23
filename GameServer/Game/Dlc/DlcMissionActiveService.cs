using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Dlc;

/// <summary>
/// Fills the DLC mission board's day/week activation slots.
///
/// The board list itself is client side: DLC_Logic.lua:122-153 builds each group from the local
/// dlc/dlc_mission rows and only lists a row whose RefreshRule is 0 (one-shot) or whose mission id is
/// present in the per-day / per-week slots it reads through me:GetAttribute(15, DailyMission + i) and
/// me:GetAttribute(15, WeeklyMission + i), 20 slots each (DLC_Logic.lua:8-13 and :155-164). MikuSB
/// never wrote those 40 slots - AttrIds.Dlc.DailyMissionStartSid / WeeklyMissionStartSid had no
/// reference - so GetLimitMission() always came back empty and every refreshing row was filtered out.
/// That is what the 2026-10-14 report saw as "水城观光少了 / 荣誉冲突只有一个任务 / 每日登录的任务不存在".
///
/// The official server rolls a subset in (the table carries Priority/weight for it). This server pins
/// one era (ServerOption.PinDlcActivityId) and has exactly one refresh cycle to show, so it activates
/// every refreshing row of the pinned activity's groups. That is deliberately the generous direction:
/// the client's own accept/claim logic is untouched, and nothing the official server would show is
/// hidden. Slots are re-written on every login, which is idempotent because the activated set is the
/// whole refreshing set for the pinned era.
/// </summary>
public static class DlcMissionActiveService
{
    /// <summary>RefreshRule values in dlc_mission.json: 1 = daily, 2 = weekly.</summary>
    private const uint DailyRefresh = 1;
    private const uint WeeklyRefresh = 2;

    /// <summary>
    /// Slots per cycle. The client reads DailyMissionStartSid + 1..20 and WeeklyMissionStartSid + 1..20,
    /// so the first usable sid is 11 (day) / 101 (week).
    /// </summary>
    private const uint Slots = 20;

    private static readonly Logger Log = new(nameof(DlcMissionActiveService));

    /// <summary>
    /// Called from CompleteLogin before the player is serialized, so the first sync the client sees
    /// already carries the activated board. Also called when the served era is switched at runtime
    /// (<paramref name="sync"/> non-null), where the slots of the previous era have to be overwritten
    /// with the new era's mission ids - and cleared when the new era has no refreshing rows at all.
    /// </summary>
    public static void RollDailyState(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var activity = DlcRuntimeOverrides.GetPinnedActivity();
        if (activity == null)
            return;

        var groups = activity.MissionGroups;

        var daily = new List<uint>();
        var weekly = new List<uint>();

        if (groups.Count > 0)
        {
            foreach (var mission in GameData.DlcMissionData.Values)
            {
                if (!groups.Contains(mission.GroupId))
                    continue;

                switch (mission.RefreshRuleValue)
                {
                    case DailyRefresh:
                        daily.Add(mission.MissionId);
                        break;
                    case WeeklyRefresh:
                        weekly.Add(mission.MissionId);
                        break;
                }
            }
        }

        daily.Sort();
        weekly.Sort();

        if (daily.Count > Slots || weekly.Count > Slots)
        {
            Log.Warn($"DLC mission board for activity {activity.Id} has {daily.Count} daily / {weekly.Count} weekly " +
                     $"refreshing missions, more than the {Slots} slots the client reads; the extra rows stay hidden.");
        }

        var changed = false;
        for (uint slot = 0; slot < Slots; slot++)
        {
            changed |= SetSlot(player, AttrIds.Dlc.DailyMissionStartSid + 1 + slot,
                slot < daily.Count ? daily[(int)slot] : 0, sync);
            changed |= SetSlot(player, AttrIds.Dlc.WeeklyMissionStartSid + 1 + slot,
                slot < weekly.Count ? weekly[(int)slot] : 0, sync);
        }

        if (changed)
        {
            Log.Info($"DLC mission board for activity {activity.Id} (uid={player.Uid}): activated " +
                     $"{daily.Count} daily / {weekly.Count} weekly mission(s).");
        }
    }

    /// <summary>
    /// Ticks the pinned activity's "登录游戏 N 次" rows (dlc_mission Type 11, one row per activity -
    /// 21244 "昨日回首" for activity 35). The client never reports that trigger itself: the Event.Logined
    /// hook (Achievement_Function.lua:609) only fires types 137 and 350, so nothing else ever advanced
    /// the progress slot in attribute group 14 and the row could not be claimed. Idempotent -
    /// AdvanceByType skips a row whose progress already reached its CheckValue. Returns rows advanced.
    /// </summary>
    public static int AdvanceLoginMissions(PlayerInstance player, NtfSyncPlayer sync)
    {
        var activity = DlcRuntimeOverrides.GetPinnedActivity();
        if (activity == null)
            return 0;

        return DlcMissionService.AdvanceByType(
            player,
            DlcMissionService.LoginTriggerType,
            sync,
            activity.MissionGroups);
    }

    /// <summary>Writes one slot; returns true when the stored value actually changed.</summary>
    private static bool SetSlot(PlayerInstance player, uint sid, uint missionId, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(AttrIds.Dlc.Gid, sid);
        if (attr.Val == missionId)
            return false;

        attr.Val = missionId;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);

        return true;
    }
}
