using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.SeasonBoss;

/// <summary>
/// 弑神斗场 (season boss) per level run bookkeeping that the client only ever reads back.
///
/// Each level owns a ten slot string block
/// (AttrIds.SeasonBoss.StringGid = 26, sid = LevelSidStride * levelId + LevelStartSid + offset):
///
///   +1..+3  role/weapon/supporter snapshots of the best run (SeasonBoss.lua:584-586 GetLevelRoleData)
///   +4      best score      (SeasonBoss.lua:560-563 GetMaxIntegral)
///   +5      best clear time (SeasonBoss.lua:566-569 GetLevelFinishTime)
///   +6      clear count     (SeasonBoss.lua:572-575 GetLevelPassTimes)
///   +7      challenge count (SeasonBoss.lua:578-581 GetLevelChallengeTimes)
///
/// SeasonBoss_LevelSettlement already maintained +4/+5/+6. This service adds the two families the
/// client can never write itself: the mode has no client -> server attribute write API (outside the two
/// business calls DonoDuel_UpgradeAttr / VirCaptureBuild_UpgradeAttribute), and SeasonBoss.lua contains
/// no writer at all for +1..+3 or +7 -- only readers.
///
///   * +7 is bumped on entry, so it counts attempts rather than clears. The fail path
///     (SeasonBoss_LevelFail, Challenge/SeasonBoss/SeasonBoss.lua:1423) records nothing, and the zone
///     UI renders the sum over a zone's levels as the number of tries
///     (SeasonBoss.lua:760-775 GetTotalChallengeTimesByLevelIndex).
///   * +1..+3 are rewritten whenever a run beats the stored best score, using the lineup the player
///     entered the level with.
///
/// The client tolerates absent or empty snapshots: GetLevelRoleData falls back to InitLevelData()
/// (SeasonBoss.lua:589-603) when json.decode returns nothing, and every reader guards on
/// "roledata.role and roledata.role > 0".
/// </summary>
internal static class SeasonBossRunService
{
    private static readonly Logger Log = new(nameof(SeasonBossRunService));

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Lineup the player last entered a season boss level with, keyed by uid. SeasonBoss_EnterLevel
    /// receives nTeamID = Formation.GetCurLineupIndex() (SeasonBoss.lua:1185) but the settlement and
    /// record payloads carry no lineup at all, so the choice has to be remembered between the two
    /// requests. It is deliberately process local: a restart merely degrades the snapshot to the
    /// fallback lineup, which is still a valid formation.
    /// </summary>
    private static readonly ConcurrentDictionary<int, uint> LastEnteredTeam = new();

    /// <summary>
    /// Counts one attempt at the level. Called from SeasonBoss_EnterLevel so the number also covers runs
    /// that end in SeasonBoss_LevelFail, which is what the zone UI means by challenge times.
    /// </summary>
    public static void RecordEntry(PlayerInstance player, uint levelId, uint teamId, NtfSyncPlayer sync)
    {
        if (levelId == 0)
            return;

        LastEnteredTeam[player.Uid] = teamId;

        var sid = LevelStatSid(levelId, AttrIds.SeasonBoss.ChallengeCountOffset);
        SetStat(player, sid, ReadStat(player, sid) + 1, sync);

        Log.Info($"SeasonBoss level {levelId} attempt #{ReadStat(player, sid)} by uid={player.Uid} " +
                 $"(team={teamId}).");
    }

    /// <summary>
    /// Folds a finished run into the level's best score and, when the score actually improved, replaces
    /// the three role snapshots with the lineup that produced it. Returns true when the score improved.
    ///
    /// Idempotent by construction: the settlement runs first and SeasonBoss_Record (the player's "keep
    /// this run" confirmation) calls it again with the same score, which then takes the early exit.
    /// </summary>
    public static bool RecordBestRun(PlayerInstance player, uint levelId, uint score, NtfSyncPlayer sync)
    {
        if (levelId == 0)
            return false;

        var scoreSid = LevelStatSid(levelId, AttrIds.SeasonBoss.BestScoreOffset);
        if (score <= ReadStat(player, scoreSid))
            return false;

        SetStat(player, scoreSid, score, sync);
        WriteBestRunLineup(player, levelId, sync);
        return true;
    }

    private static void WriteBestRunLineup(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        var snapshots = CaptureLineupSnapshots(player, player.Uid);

        for (var i = 0; i < AttrIds.SeasonBoss.BestRunRoleCount; i++)
        {
            var sid = LevelStatSid(levelId, AttrIds.SeasonBoss.BestRunRoleStartOffset + (uint)i);
            SetStat(player, sid, JsonSerializer.Serialize(snapshots[i], SnapshotJson), sync);
        }

        Log.Info($"SeasonBoss level {levelId} best run lineup recorded for uid={player.Uid} " +
                 $"(roles={string.Join('/', snapshots.Select(x => x.Role))}).");
    }

    private static SeasonBossRoleSnapshot[] CaptureLineupSnapshots(PlayerInstance player, int uid)
    {
        var lineups = player.LineupManager.LineupData.LineupInfo;
        var entered = LastEnteredTeam.TryGetValue(uid, out var teamId) ? teamId : 0;

        var lineup = (entered != 0 ? lineups.GetValueOrDefault((int)entered) : null)
            ?? lineups.GetValueOrDefault(1)
            ?? lineups.Values.FirstOrDefault();

        if (lineup == null)
            return [new(), new(), new()];

        return
        [
            CaptureRoleSnapshot(player, lineup.Member1),
            CaptureRoleSnapshot(player, lineup.Member2),
            CaptureRoleSnapshot(player, lineup.Member3)
        ];
    }

    /// <summary>
    /// Mirrors BossPvpService.CaptureRoleSnapshot: the shape has to match what the client's
    /// SeasonBossLogic.InitLevelData (SeasonBoss.lua:589-603) produces, because GetLevelRoleData feeds
    /// the same table to the lineup renderer and to CheckLineupChange.
    /// </summary>
    private static SeasonBossRoleSnapshot CaptureRoleSnapshot(PlayerInstance player, uint characterGuid)
    {
        if (characterGuid == 0)
            return new SeasonBossRoleSnapshot();

        var character = player.CharacterManager.GetCharacterByGUID(characterGuid);
        if (character == null)
            return new SeasonBossRoleSnapshot();

        var snapshot = new SeasonBossRoleSnapshot
        {
            Role = character.Guid,
            Weapon = character.WeaponUniqueId
        };

        var weapon = player.InventoryManager.GetWeaponItem(character.WeaponUniqueId);
        if (weapon != null)
        {
            snapshot.Wgdpl = BuildWeaponGdpl(weapon);
            snapshot.Wslot = weapon.PartSlots;
        }

        var supports = character.SupportSlots
            .OrderBy(x => x.Key)
            .Select(x => x.Value)
            .Where(x => x != 0)
            .Take(3)
            .ToArray();

        if (supports.Length > 0)
        {
            snapshot.S1 = supports[0];
            snapshot.Sgdpl1 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(supports[0]));
        }

        if (supports.Length > 1)
        {
            snapshot.S2 = supports[1];
            snapshot.Sgdpl2 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(supports[1]));
        }

        if (supports.Length > 2)
        {
            snapshot.S3 = supports[2];
            snapshot.Sgdpl3 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(supports[2]));
        }

        return snapshot;
    }

    private static List<uint> BuildWeaponGdpl(GameWeaponInfo weapon)
    {
        var gdpl = DecodeGdpl(weapon.TemplateId);
        gdpl.Add(weapon.Level);
        gdpl.Add(weapon.Evolue);
        return gdpl;
    }

    private static List<uint> BuildSupportGdpl(GameSupportCardInfo? support)
    {
        if (support == null)
            return [];

        var gdpl = DecodeGdpl(support.TemplateId);
        gdpl.Add(support.Level);
        gdpl.Add(0);
        return gdpl;
    }

    private static List<uint> DecodeGdpl(ulong templateId)
    {
        return
        [
            (uint)(templateId & 0xFFFF),
            (uint)((templateId >> 16) & 0xFFFF),
            (uint)((templateId >> 32) & 0xFFFF),
            (uint)((templateId >> 48) & 0xFFFF)
        ];
    }

    private static uint LevelStatSid(uint levelId, uint offset) =>
        AttrIds.SeasonBoss.LevelSidStride * levelId + AttrIds.SeasonBoss.LevelStartSid + offset;

    public static uint ReadStat(PlayerInstance player, uint sid)
    {
        var raw = player.Attributes.GetStringValue(AttrIds.SeasonBoss.StringGid, sid);
        return uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    public static void SetStat(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync) =>
        SetStat(player, sid, value.ToString(CultureInfo.InvariantCulture), sync);

    public static void SetStat(PlayerInstance player, uint sid, string value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.SetString(AttrIds.SeasonBoss.StringGid, sid, value);
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Convenience wrapper so callers do not have to repeat the stride arithmetic.</summary>
    public static uint StatSid(uint levelId, uint offset) => LevelStatSid(levelId, offset);
}

/// <summary>
/// One party slot as the season boss lineup panel expects it. The JSON names are load bearing: the
/// client decodes this straight into the table InitLevelData() builds (SeasonBoss.lua:589-603).
/// </summary>
public sealed class SeasonBossRoleSnapshot
{
    [JsonPropertyName("role")] public uint Role { get; set; }
    [JsonPropertyName("weapon")] public uint Weapon { get; set; }
    [JsonPropertyName("s1")] public uint S1 { get; set; }
    [JsonPropertyName("s2")] public uint S2 { get; set; }
    [JsonPropertyName("s3")] public uint S3 { get; set; }
    [JsonPropertyName("wgdpl")] public List<uint> Wgdpl { get; set; } = [];
    [JsonPropertyName("wslot")] public Dictionary<uint, ulong> Wslot { get; set; } = [];
    [JsonPropertyName("sgdpl1")] public List<uint> Sgdpl1 { get; set; } = [];
    [JsonPropertyName("sgdpl2")] public List<uint> Sgdpl2 { get; set; } = [];
    [JsonPropertyName("sgdpl3")] public List<uint> Sgdpl3 { get; set; } = [];
}
