using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.ClimbTower;

/// <summary>
/// DLC 3.3 爬塔 (DlcFuncClimbTower -> UI.Open("ClimbTower"), DLC/ClimbTower/ClimbTower.lua).
/// The client keeps the whole ladder in its own config and derives progress from three attribute slots:
///   (174, 1)                 active season id - CheckInSeason/GetTotalStar/CheckRed all gate on it and
///                            return "tip.not_open" when it is 0, so the season has to be seeded at login;
///   (21,  battle.Level)      star bits 0..7 per *level* id (written by ClimbTowerSettlement);
///   (174, 10 + awardId - 1)  star milestone claim flags (ClimbTower.GetAwardFlag, :280-282).
/// Total stars are summed over the season's Challenge battles only (:329-342, BattleType.Challenge == 2),
/// which is also what the milestone table climbs against, so no level table is needed here.
/// </summary>
public static class ClimbTowerService
{
    private const uint StarBitMask = 0xFFu;

    public static DlcClimbTowerSeasonExcel? SelectSeason(DateTime now)
    {
        DlcClimbTowerSeasonExcel? latestStarted = null;
        var latestStart = long.MinValue;
        DlcClimbTowerSeasonExcel? last = null;

        foreach (var season in GameData.ClimbTowerSeasonData.Values)
        {
            last ??= season;
            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(season.OpenTimeRaw));
            var end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(season.CloseTimeRaw));
            var stamp = DlcRuntimeOverrides.ToUnixSeconds(now);

            if (start > 0 && end > 0 && stamp >= start && stamp <= end)
                return season;

            if (start > 0 && start <= stamp && start > latestStart)
            {
                latestStart = start;
                latestStarted = season;
            }

            if (last.ID < season.ID)
                last = season;
        }

        return latestStarted ?? last;
    }

    /// <summary>Seeds (174,1) so CheckInSeason stops short-circuiting every panel to "not open".</summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var season = SelectSeason(DateTime.Now);
        if (season == null)
            return;

        const uint gid = AttrIds.ClimbTowerDlc.GroupId;
        const uint sid = AttrIds.ClimbTowerDlc.ActivitySid;
        if (player.Attributes.GetValue(gid, sid) == season.ID)
        {
            if (sync != null && player.Attributes.Get(gid, sid) is { } existing)
                player.Attributes.SyncTo(sync, existing);

            return;
        }

        var attr = player.Attributes.Set(gid, sid, season.ID);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    public static uint TotalStars(PlayerInstance player, DlcClimbTowerSeasonExcel season)
    {
        uint total = 0;
        foreach (var battleId in season.Battles)
        {
            if (!GameData.ClimbTowerBattleData.TryGetValue(battleId, out var battle) || !battle.IsChallenge ||
                battle.Level == 0)
                continue;

            var bits = player.Attributes.GetValue(AttrIds.Launch.LevelStateGid, battle.Level) & StarBitMask;
            for (var i = 0; i < 8; i++)
            {
                if ((bits & (1u << i)) != 0)
                    total++;
            }
        }

        return total;
    }

    public static bool IsAwarded(PlayerInstance player, uint awardId)
        => player.Attributes.GetValue(AttrIds.ClimbTowerDlc.GroupId, AwardSid(awardId)) > 0;

    public static void MarkAwarded(PlayerInstance player, uint awardId, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Set(AttrIds.ClimbTowerDlc.GroupId, AwardSid(awardId), 1);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>(174, 10 + awardId - 1); ClimbTower.nAwardStart is 10 while the reward table is 1-based.</summary>
    private static uint AwardSid(uint awardId) => AttrIds.ClimbTowerDlc.AwardStartSid + awardId - 1;
}
