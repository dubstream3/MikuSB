using System.Collections.Generic;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Dlc;

/// <summary>
/// DLC mission board (水城观光) shared logic. Mission state lives in attribute (gid 14, sid missionId)
/// packed as bits: bit 0 is "reward claimed", bits 1..31 are the progress counter, mirroring
/// Achievement.lua:743 GetProgres (GetBits(attr, 1, 31)) and :783 IsReceive (GetBits(attr, 0, 0)).
/// </summary>
public static class DlcMissionService
{
    private const int ProgressShift = 1;
    private const uint ProgressMask = 0x7FFFFFFFu;
    private static readonly Logger Log = new(nameof(DlcMissionService));

    /// <summary>
    /// Trigger type of the "登录游戏 N 次" rows (Type 11 in dlc_mission.json, one row per DLC activity -
    /// 21244 "昨日回首" for activity 35). The client never reports it on its own: the Event.Logined hook
    /// (Achievement_Function.lua:609) only fires types 137 and 350, so the server has to tick it.
    /// </summary>
    public const uint LoginTriggerType = 11;

    public static uint GetProgress(uint raw) => (raw >> ProgressShift) & ProgressMask;

    public static bool IsReceived(uint raw) => (raw & 1u) != 0;

    public static uint Pack(uint progress, bool received) =>
        (Math.Min(progress, ProgressMask) << ProgressShift) | (received ? 1u : 0u);

    /// <summary>
    /// Grants one mission's rewards and flips its claimed bit. Returns null when the mission is
    /// already claimed or its progress has not reached CheckValue.
    /// </summary>
    public static async Task<JsonArray?> ClaimAsync(
        PlayerInstance player,
        DlcMissionExcel mission,
        NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.DlcGroupId, mission.MissionId);

        if (IsReceived(attr.Val))
            return null;

        if (GetProgress(attr.Val) < mission.CheckValue)
            return null;

        var rows = DlcRuntimeOverrides.ReadRewardRows(mission.Rewards);
        var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        attr.Val = Pack(GetProgress(attr.Val), true);
        player.Attributes.SyncTo(sync, attr);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        Log.Info($"DLC mission {mission.MissionId} (group {mission.GroupId}) claimed by uid={player.Uid}, " +
                 $"{rewards.Count} reward row(s).");

        return rewards;
    }

    /// <summary>
    /// Advances every still-open mission of the given trigger type by one step. The shipped tables
    /// gate most rows through "Param" (an item/source filter) that the client never sends, so this
    /// intentionally ignores it and treats the notification as a plain counter tick. When
    /// <paramref name="groups"/> is non-empty only missions of those dlc_mission GroupIDs are touched -
    /// the login tick uses that to stay inside the pinned DLC activity. Returns the rows advanced.
    /// </summary>
    public static int AdvanceByType(
        PlayerInstance player,
        uint type,
        NtfSyncPlayer sync,
        IReadOnlyCollection<uint>? groups = null)
    {
        if (type == 0 || !GameData.DlcMissionsByType.TryGetValue(type, out var missions))
            return 0;

        var advanced = 0;

        foreach (var mission in missions)
        {
            if (mission.CheckValue == 0)
                continue;

            if (groups is { Count: > 0 } && !groups.Contains(mission.GroupId))
                continue;

            var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.DlcGroupId, mission.MissionId);
            if (IsReceived(attr.Val))
                continue;

            var progress = GetProgress(attr.Val);
            if (progress >= mission.CheckValue)
                continue;

            attr.Val = Pack(progress + 1, false);
            player.Attributes.SyncTo(sync, attr);
            advanced++;
        }

        return advanced;
    }
}
