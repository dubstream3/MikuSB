using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.GirlRush;

/// <summary>
/// 异绪消解 (GirlRush) - the DLC27 arena mode behind DlcFuncGirlrush.
///
/// The client owns the whole config (GirlRush/GirlRushConfig.lua, GirlRushLevelCfg.lua) and treats two
/// attribute groups as its save file:
///   * <c>GirlRush.GID = 158</c>, one packed value per stage id (GirlRush/GirlRush.lua:12-35):
///     bits 0..13 role id, 14..17 weapon index, 18..21 skill index, 22..25 fire mode, 26..30 best rank.
///   * <c>GirlRush.GID_AWARD = 160</c>, one flag per board mission index (1 = claimed).
/// Stage clear counts live in <c>Launch.GPASSID = 22</c> (GirlRushConfig.lua:34-36), the same slot the
/// rest of the server writes level pass counters to.
///
/// Nothing in the client ever calls SetAttribute for those groups - every write has to come from here,
/// which is also why an empty (158,*) otherwise means "nothing selected, nothing cleared" and the board
/// missions can never unlock.
/// </summary>
public static class GirlRushService
{
    public const uint Gid = 158;
    public const uint AwardGid = 160;

    private const int RoleStart = 0;
    private const int RoleEnd = 13;
    private const int WeaponStart = 14;
    private const int WeaponEnd = 17;
    private const int SkillStart = 18;
    private const int SkillEnd = 21;
    private const int FireStart = 22;
    private const int FireEnd = 25;
    private const int RankStart = 26;
    private const int RankEnd = 30;

    /// <summary>Launch.GPASSID - stage clear counter, the same slot 抢滩登陆/爬塔 use for their levels.</summary>
    private const uint PassGid = AttrIds.Launch.LevelPassGid;

    /// <summary>Newest season row (the only row today), or null when the table is missing.</summary>
    public static GirlRushSeasonExcel? CurrentSeason =>
        GameData.GirlRushSeasonData.Count == 0
            ? null
            : GameData.GirlRushSeasonData.Values.OrderByDescending(row => row.Id).First();

    public static uint SeasonId => CurrentSeason?.Id ?? 0;

    public static GirlRushLevelExcel? Level(uint levelId) =>
        GameData.GirlRushLevelData.GetValueOrDefault(levelId);

    /// <summary>Board missions of a season, in index order.</summary>
    public static IEnumerable<GirlRushAwardExcel> Awards(uint seasonId) =>
        GameData.GirlRushAwardData.Values
            .Where(row => row.SeasonId == seasonId)
            .OrderBy(row => row.Index);

    /// <summary>A stage is considered part of the mode when the season lists it.</summary>
    public static bool IsSeasonLevel(uint levelId, GirlRushSeasonExcel? season = null)
    {
        season ??= CurrentSeason;
        return season != null && season.LevelIds.Contains(levelId) && GameData.GirlRushLevelData.ContainsKey(levelId);
    }

    public static uint PassCount(PlayerInstance player, uint levelId) =>
        player.Attributes.GetValue(PassGid, levelId);

    public static uint BestRank(PlayerInstance player, uint levelId) =>
        ReadBits(player.Attributes.GetValue(Gid, levelId), RankStart, RankEnd);

    /// <summary>Stage can be entered when its prerequisite stage has been cleared at least once.</summary>
    public static bool IsLevelOpen(PlayerInstance player, GirlRushLevelExcel level)
    {
        var required = level.RequiredLevelId;
        return required == 0 || PassCount(player, required) > 0;
    }

    /// <summary>Error key to hand back when the stage cannot be entered; null when it can.</summary>
    public static string? EntryError(PlayerInstance player, uint levelId, GirlRushSeasonExcel? season = null)
    {
        if (!IsSeasonLevel(levelId, season))
            return "error.BadParam";

        var level = Level(levelId);
        if (level == null)
            return "error.BadParam";

        return IsLevelOpen(player, level) ? null : "ui.TxtNotOpen";
    }

    /// <summary>Persists the role/weapon/skill/fire selection of one stage into the packed (158, id).</summary>
    public static void SaveSelection(
        PlayerInstance player,
        uint levelId,
        uint roleId,
        uint weaponIndex,
        uint skillIndex,
        uint fireModel,
        NtfSyncPlayer? sync)
    {
        var current = player.Attributes.GetValue(Gid, levelId);
        current = WriteBits(current, RoleStart, RoleEnd, roleId);
        current = WriteBits(current, WeaponStart, WeaponEnd, weaponIndex);
        current = WriteBits(current, SkillStart, SkillEnd, skillIndex);
        current = WriteBits(current, FireStart, FireEnd, fireModel);

        var attr = player.Attributes.Set(Gid, levelId, current);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Bumps the stage clear counter and returns the new value.</summary>
    public static uint AddPass(PlayerInstance player, uint levelId, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Add(PassGid, levelId, 1);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
        return attr.Val;
    }

    /// <summary>
    /// Keeps the best (lowest) rank of a stage. Returns true when the stored rank changed so callers can
    /// log the improvement; rank 0 / out of range input is ignored.
    /// </summary>
    public static bool RecordRank(PlayerInstance player, uint levelId, uint rank, NtfSyncPlayer? sync)
    {
        if (rank is < 1 or > 4)
            return false;

        var current = player.Attributes.GetValue(Gid, levelId);
        var best = ReadBits(current, RankStart, RankEnd);
        if (best != 0 && best <= rank)
            return false;

        var attr = player.Attributes.Set(Gid, levelId, WriteBits(current, RankStart, RankEnd, rank));
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
        return true;
    }

    public static bool IsAwardClaimed(PlayerInstance player, uint index) =>
        player.Attributes.GetValue(AwardGid, index) == 1;

    public static void MarkAwardClaimed(PlayerInstance player, uint index, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Set(AwardGid, index, 1);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>
    /// Mirrors GirlRush.CheckLockConditionFunc (GirlRush/GirlRush.lua:319-368):
    /// [1, levelId, count] = the stage must be cleared that many times,
    /// [2, levelId, rank]  = the stage must be cleared with a rank at least that good (1 is best).
    /// </summary>
    public static bool Satisfies(PlayerInstance player, GirlRushAwardExcel award)
    {
        foreach (var condition in award.Conditions)
        {
            var type = condition[0];
            var levelId = condition[1];
            var param = condition[2];

            switch (type)
            {
                case 1:
                    if (PassCount(player, levelId) < Math.Max(param, 1u))
                        return false;
                    break;

                case 2:
                {
                    var rank = BestRank(player, levelId);
                    if (PassCount(player, levelId) == 0 || rank == 0 || rank > Math.Max(param, 1u))
                        return false;
                    break;
                }

                default:
                    // Unknown condition types cannot be verified; fail closed so a malformed row cannot
                    // be claimed for free.
                    return false;
            }
        }

        return true;
    }

    /// <summary>First mission of the season that is claimable right now, or null.</summary>
    public static GirlRushAwardExcel? FirstClaimable(PlayerInstance player, uint seasonId)
    {
        foreach (var award in Awards(seasonId))
        {
            if (!IsAwardClaimed(player, award.Index) && Satisfies(player, award))
                return award;
        }

        return null;
    }

    public static uint NewSeed() => (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

    public static void Persist(PlayerInstance player) => DatabaseHelper.SaveDatabaseType(player.Data);

    /// <summary>Reads an inclusive 0-based bit range, matching Misc/Lib.lua GetBits.</summary>
    public static uint ReadBits(uint value, int begin, int end)
    {
        if (begin > end)
        {
            var swap = begin;
            begin = end;
            end = swap;
        }

        if (begin < 0 || end >= 32)
            return 0;

        var width = end - begin + 1;
        var mask = width >= 32 ? uint.MaxValue : (1u << width) - 1;
        return (value >> begin) & mask;
    }

    /// <summary>Writes an inclusive 0-based bit range, matching Misc/Lib.lua SetBits.</summary>
    public static uint WriteBits(uint value, int begin, int end, uint bits)
    {
        if (begin > end)
        {
            var swap = begin;
            begin = end;
            end = swap;
        }

        if (begin < 0 || end >= 32)
            return value;

        var width = end - begin + 1;
        var mask = (width >= 32 ? uint.MaxValue : (1u << width) - 1) << begin;
        return (value & ~mask) | ((bits << begin) & mask);
    }

    /// <summary>Absolute window of the pinned DLC era, used by the client-side window patch.</summary>
    public static (long Start, long End) Window(long now)
    {
        var floor = DlcRuntimeOverrides.ActivationFloor();
        var start = Math.Min(now - 3600, floor == 0 ? long.MaxValue : floor - 3600);
        return (start, now + 30L * 24 * 60 * 60);
    }
}
