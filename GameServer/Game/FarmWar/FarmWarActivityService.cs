using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.FarmWar;

// 蔬菜大战 (FarmWar) shared state.
//
// Client sources of truth (FarmWarActivity/*.lua, A level):
//   (164, 1)            current period id            FarmWarActivity.lua:189  GID / SID_ACT
//   (165, nodeId) == 1  grow node unlocked/activated  FarmWarActivityCfg.lua:157
//   (169, buffId)       buff owned (value = tier)    FarmWarActivity.lua:276
//   (104, levelId)      formation JSON string        FarmWarLevel.lua:59 (str attr)
//   (21,  levelId)      bits 0..11 best record, bits 12..20 best wave   FarmWarLevel.lua:13/18
//   (22,  levelId)      clear count                  FarmWarLevel.lua:29
//
// The grow-tree / buff slots are written by the FarmWarLogic_ActiveGrowNode family and read back by
// the client every time the tree opens, so the server only has to keep the bit alive across logins.
public static class FarmWarActivityService
{
    private static readonly Logger Log = new(nameof(FarmWarActivityService));

    private const uint RecordMask = 0xFFFu;   // 12 bits
    private const uint WaveMask = 0x1FFu;     // 9 bits
    private const int WaveShift = 12;

    /// <summary>Period the pinned era should run; falls back to the newest row whose window opened.</summary>
    public static FarmWarActExcel? SelectAct(DateTime now)
    {
        FarmWarActExcel? latestStarted = null;
        FarmWarActExcel? latest = null;

        foreach (var act in GameData.FarmWarActData.Values.OrderBy(a => a.Id))
        {
            latest = act;

            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(act.Start));
            var end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(act.End));
            if (start == 0)
                continue;

            var stamp = new DateTimeOffset(now).ToUnixTimeSeconds();
            if (stamp < start || (end != 0 && stamp > end))
                continue;

            if (latestStarted == null || act.Id > latestStarted.Id)
                latestStarted = act;
        }

        return latestStarted ?? latest;
    }

    public static uint CurrentActId(PlayerInstance player) =>
        player.Attributes.GetValue(AttrIds.FarmWar.Gid, AttrIds.FarmWar.ActivitySid);

    public static FarmWarActExcel? CurrentAct(PlayerInstance player)
    {
        var id = CurrentActId(player);
        if (id != 0 && GameData.FarmWarActData.TryGetValue(id, out var act))
            return act;

        return SelectAct(DateTime.UtcNow);
    }

    /// <summary>Login seeding: pin the period id into (164,1) so the module UI can resolve its row.</summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var act = SelectAct(DateTime.UtcNow);
        if (act == null)
            return;

        var existing = player.Attributes.Get(AttrIds.FarmWar.Gid, AttrIds.FarmWar.ActivitySid);
        if (existing != null && existing.Val == act.Id)
        {
            if (sync != null)
                player.Attributes.SyncTo(sync, existing);
            return;
        }

        var attr = player.Attributes.Set(AttrIds.FarmWar.Gid, AttrIds.FarmWar.ActivitySid, act.Id);
        if (sync != null && attr != null)
            player.Attributes.SyncTo(sync, attr);
    }

    public static bool IsSkillActive(PlayerInstance player, uint nodeId) =>
        player.Attributes.GetValue(AttrIds.FarmWar.SkillGid, nodeId) != 0;

    public static void SetSkill(PlayerInstance player, uint nodeId, bool active, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.Set(AttrIds.FarmWar.SkillGid, nodeId, active ? 1u : 0u);
        player.Attributes.SyncTo(sync, attr);

        DatabaseHelper.SaveDatabaseType(player.Data);
    }

    public static void ResetAllSkills(PlayerInstance player, NtfSyncPlayer sync)
    {
        player.Attributes.RemoveWhere(attr => attr.Gid == AttrIds.FarmWar.SkillGid);
        DatabaseHelper.SaveDatabaseType(player.Data);
    }

    public static string? GetFormation(PlayerInstance player, uint levelId) =>
        player.Attributes.GetStringValue(AttrIds.FarmWar.StringGid, levelId);

    public static void SetFormation(PlayerInstance player, uint levelId, string json, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.SetString(AttrIds.FarmWar.StringGid, levelId, json);
        player.Attributes.SyncTo(sync, attr);

        DatabaseHelper.SaveDatabaseType(player.Data);
    }

    /// <summary>
    /// Persists a cleared stage: bumps the clear counter and keeps the max of (health percent, wave)
    /// in the packed record slot. Returns true on the first clear so the caller can grant the table
    /// rewards exactly once.
    /// </summary>
    public static bool RecordClear(
        PlayerInstance player,
        uint levelId,
        uint healthPercent,
        uint wave,
        NtfSyncPlayer sync)
    {
        var passAttr = player.Attributes.Get(AttrIds.Launch.LevelPassGid, levelId);
        var first = passAttr == null || passAttr.Val == 0;

        passAttr = player.Attributes.Set(
            AttrIds.Launch.LevelPassGid,
            levelId,
            (passAttr?.Val ?? 0) + 1);
        player.Attributes.SyncTo(sync, passAttr);

        var recordAttr = player.Attributes.Get(AttrIds.Launch.LevelStateGid, levelId);
        var old = recordAttr?.Val ?? 0;
        var oldRecord = old & RecordMask;
        var oldWave = (old >> WaveShift) & WaveMask;

        var record = Math.Min(healthPercent, RecordMask);
        var bestWave = Math.Min(wave, WaveMask);

        var packed = Math.Max(oldRecord, record) | (Math.Max(oldWave, bestWave) << WaveShift);
        recordAttr ??= player.Attributes.GetOrCreate(AttrIds.Launch.LevelStateGid, levelId);

        recordAttr.Val = packed;
        player.Attributes.SyncTo(sync, recordAttr);

        DatabaseHelper.SaveDatabaseType(player.Data);
        return first;
    }

    /// <summary>
    /// Marks the period task board. The live table has no threshold column on these rows
    /// (achievement/activity_quest.json Check is empty for 20400..20434), and the client only reads the
    /// achievement bits, so a clear flips the matching rows to "finished" and lets the claim button run.
    /// </summary>
    public static void MarkQuestProgress(
        PlayerInstance player,
        FarmWarActExcel act,
        uint levelId,
        NtfSyncPlayer sync)
    {
        foreach (var taskId in act.TaskIds)
        {
            if (!GameData.ActivityQuestData.TryGetValue(taskId, out var quest))
                continue;

            // Type 2 = "clear stage <Param0>": only the matching stage counts.
            if (quest.Type == 2)
            {
                var stage = ReadFirstUInt(quest.Param);
                if (stage != 0 && stage != levelId)
                    continue;
            }

            AchievementClaimService.Complete(
                player, AttrIds.Achievement.QuestGroupId, taskId, 1, sync);
        }
    }

    public static void LogUnknownStage(string api, uint levelId) =>
        Log.Warn($"{api}: unknown farmwar stage {levelId}.");

    private static uint ReadFirstUInt(Newtonsoft.Json.Linq.JToken? token)
    {
        if (token is Newtonsoft.Json.Linq.JArray array && array.Count > 0)
            token = array[0];

        return uint.TryParse(token?.ToString().Trim(), out var parsed) ? parsed : 0;
    }
}
