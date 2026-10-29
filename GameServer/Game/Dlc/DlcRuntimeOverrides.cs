using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MikuSB.GameServer.Game.Dlc;

/// <summary>
/// Server side window overrides for the pinned DLC. Every time window the client ships in its own
/// tables (dlc_activities, dlc1/chapter, challenge/defend/defend_act, dlc_shop, online) expired long
/// ago, and the DLC UI hides or marks "expired" any entry whose local window is not active. All of
/// the overrides ride on s2c callbacks the client already registers, so no client file has to be
/// modified.
/// </summary>
public static class DlcRuntimeOverrides
{
    public const long WindowBeforeSeconds = 3600;
    public const long WindowAfterSeconds = 30 * 24 * 3600;

    /// <summary>Start/end (unix seconds) of the synthetic window pushed to the client.</summary>
    public static (long Start, long End) Window(long? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return (current - WindowBeforeSeconds, current + WindowAfterSeconds);
    }

    /// <summary>
    /// Runtime pin installed by the <c>dlc</c> command. While non-null it wins over
    /// <c>ServerOption.PinDlcActivityId</c>, so an operator can serve another era without editing
    /// Config.json and restarting the server. <c>Id == 0</c> is a deliberate "pinning off" (the client
    /// then falls back to its own time windows). The reference is swapped in one write, so a reader
    /// always sees either the whole old pin or the whole new one.
    /// </summary>
    private sealed class RuntimePin
    {
        public uint Id;
    }

    private static volatile RuntimePin? runtimePin;

    /// <summary>True when a runtime pin was installed (even the deliberate "off", i.e. id 0).</summary>
    public static bool HasRuntimePin => runtimePin is not null;

    /// <summary>
    /// The DLC era the server currently serves: the runtime pin when one is installed, the value in
    /// Config.json otherwise. Every pin consumer must read this instead of
    /// <c>ConfigManager.Config.ServerOption.PinDlcActivityId</c>.
    /// </summary>
    /// <summary>
    /// Retired eras (see <see cref="DlcEraPolicy"/>) collapse to 0: the client keeps no assets for them any
    /// more, so pinning one would seed attributes and windows the UI cannot draw. A Config.json written
    /// before they were withdrawn therefore degrades to "no pin" instead of a broken DLC page.
    /// </summary>
    public static uint EffectiveDlcId
    {
        get
        {
            var id = runtimePin?.Id ?? ConfigManager.Config.ServerOption.PinDlcActivityId;
            return DlcEraPolicy.IsRetired(id) ? 0u : id;
        }
    }

    /// <summary>
    /// True while the server should steer the DLC era at all. False keeps the legacy behaviour of
    /// "no pin, let the client resolve by its own time windows": nothing is seeded or cleared then.
    /// </summary>
    public static bool PinActive => EffectiveDlcId != 0;

    /// <summary>Installs (or replaces) the runtime pin. Does not touch Config.json.</summary>
    public static void SetRuntimeDlcId(uint id) => runtimePin = new RuntimePin { Id = id };

    /// <summary>Drops the runtime pin and falls back to Config.json again.</summary>
    public static void ClearRuntimeDlcId() => runtimePin = null;

    /// <summary>
    /// Payload of the ShopLogic_GetOpenTime callback: every known shop tab with a fresh window
    /// (unix seconds). The client copies nBegin/nEnd into ShopLogic.tbShopTabConf[shopId]
    /// (Shop.lua:649), which is what makes the 崇福集市 tabs usable again.
    /// </summary>
    public static JsonObject BuildShopOpenTimePayload()
    {
        var (start, end) = Window();
        var payload = new JsonObject();

        foreach (var shopId in GameData.DlcShopData.Keys)
        {
            payload[shopId.ToString()] = new JsonObject
            {
                ["nBegin"] = start,
                ["nEnd"] = end
            };
        }

        return payload;
    }

    /// <summary>
    /// Reads one entry out of a dlc_activities <c>DlcFuncConfId</c> string, e.g.
    /// <c>"{Refresh=35,Defense=35,Rush=105}"</c> -&gt; 35 for "Refresh" (0 when absent).
    /// </summary>
    public static uint ResolveFuncConfigId(string? dlcFuncConfId, string funcName)
    {
        if (string.IsNullOrWhiteSpace(dlcFuncConfId))
            return 0;

        var match = Regex.Match(dlcFuncConfId, $@"(?:^|[{{,\s]){Regex.Escape(funcName)}\s*=\s*(\d+)");
        return match.Success && uint.TryParse(match.Groups[1].Value, out var id) ? id : 0;
    }

    /// <summary>The pinned dlc/dlc_activities row, or null when pinning is off or the row is missing.</summary>
    public static DlcActivityExcel? GetPinnedActivity()
    {
        var pinnedId = EffectiveDlcId;
        return pinnedId != 0 && GameData.DlcActivityData.TryGetValue(pinnedId, out var act) ? act : null;
    }

    /// <summary>
    /// Unix second floor of the era the pinned DLC activated: its own EnterStartTime (DLC35 -&gt;
    /// 2026-01-29 04:00 local). Activating DLCxx activates that DLC's content, so a window that already
    /// ended before this floor belongs to an earlier version, where live-server semantics stay in force
    /// (expired = closed) instead of the blanket "every expired window opens" relaxation. 0 disables
    /// the gate (no pinned row, or an unparseable window).
    /// </summary>
    public static long ActivationFloor()
    {
        var pinned = GetPinnedActivity();
        return pinned == null ? 0 : ToUnixSeconds(ParseConfigTime(pinned.EnterStartTime));
    }

    /// <summary>
    /// True when the window shipped in the client's own tables ended before the pinned DLC's era began,
    /// i.e. the row belongs to an earlier version. Rows whose end cannot be parsed (permanent
    /// activities leave the column empty or use -1) are never historical.
    /// </summary>
    public static bool IsHistoricalWindow(string? windowEndRaw)
    {
        var floor = ActivationFloor();
        if (floor <= 0)
            return false;

        var end = ToUnixSeconds(ParseConfigTime(windowEndRaw));
        return end > 0 && end < floor;
    }

    /// <summary>
    /// Parses a client table timestamp ("[202601290400]") as local time. Every window in the shipped
    /// tables uses that shape and the client turns it into a unix second with os.time on the local wall
    /// clock, so the server has to read it the same way.
    /// </summary>
    public static DateTime? ParseConfigTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Trim('[', ']');
        if (normalized.Length != 12)
            return null;

        return DateTime.TryParseExact(
            normalized,
            "yyyyMMddHHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

    /// <summary>Unix seconds of a parsed client timestamp (0 when it could not be parsed).</summary>
    public static long ToUnixSeconds(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0L;

    /// <summary>
    /// The dlc1/chapter row behind the 主线 entry (DLC35 -&gt; 28, matched by UnlockChapter), or 0 when
    /// the row is unknown. DlcFuncConfId only carries the sub game modes (Refresh/Defense/...), so the
    /// story chapter has to come from UnlockChapter instead.
    /// </summary>
    public static uint ResolveStoryChapterId(DlcActivityExcel activity)
    {
        // UnlockChapter shares the mixed number/string typing of the other dlc_activities columns,
        // so go through the tolerant reader instead of comparing the JToken directly.
        var chapterId = ToUint(activity.UnlockChapter);
        if (chapterId != 0)
            return chapterId;

        // Fall back to parsing it out of DlcFuncConfId when it happens to be listed there too.
        return ResolveFuncConfigId(activity.DlcFuncConfId, "Story");
    }

    /// <summary>Defend act rows belonging to <paramref name="dlcId"/> (DLC35 -&gt; ids 49 and 50).</summary>
    public static List<DefendActExcel> SelectDefendActs(uint dlcId)
    {
        var result = new List<DefendActExcel>();
        if (dlcId == 0)
            return result;

        foreach (var act in GameData.DefendActData.Values)
        {
            if (ToUint(act.DlcId) == dlcId)
                result.Add(act);
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>
    /// Defend act id the client should treat as the current 荣誉冲突 period. The client only ever reads
    /// the act id from attribute (gid 10, sid 0) and never writes it, so without this the battle pass
    /// and target reward lists stay empty because tbLevelAwardList[0] does not exist.
    /// </summary>
    public static uint SelectDefendActId()
    {
        var pinned = GetPinnedActivity();
        if (pinned == null)
            return 0;

        var acts = SelectDefendActs(pinned.Id);
        return acts.Count > 0 ? acts[0].Id : 0;
    }

    /// <summary>
    /// 海姆优化作战 (Rush): the client's Online_GMChangeTime callback (Launch/Online/Online.lua:4449)
    /// copies nStartTime/nEndTime/tbCondition/tbOpenHour/tbDate into Online.GetConfig(nId), which is
    /// the row DlcFuncRush.OnGetTimeInfo() reads through Online.GetGroupConfig(group). Both rows of
    /// the group (105 and 115) share Group=105 but each needs its own callback, because the client
    /// looks the row up by nId.
    /// </summary>
    public static JsonObject BuildRushTimePayload(uint onlineId, long start, long end)
    {
        // Condition "{1=20}" is a player-level gate the client checks itself; an empty table means
        // "no extra condition" and keeps the entry from being filtered out.
        return new JsonObject
        {
            ["tbConfig"] = new JsonObject
            {
                ["nId"] = onlineId,
                ["nStartTime"] = start,
                ["nEndTime"] = end,
                ["tbCondition"] = new JsonObject(),
                ["tbOpenHour"] = new JsonArray(),
                ["tbDate"] = new JsonArray()
            }
        };
    }

    /// <summary>
    /// 心愿寄语 (Dorm=3): the client's GMRoleFestivalSetId callback (Activity/RoleFestivalMessage.lua:336)
    /// stretches the matching tbActConf row to now-1d..now+1d and marks every other row as ended.
    /// </summary>
    public static JsonObject BuildFestivalMessagePayload(uint actId)
    {
        return new JsonObject { ["nActId"] = actId };
    }

    /// <summary>
    /// 异星守护 (Mow=4): the client's AlienDefense_CheckOpenAct callback
    /// (DLC/AlienDefense/AlienDefense.lua:797) writes nStartTime/nEndTime into
    /// AlienDefenseConfig.tbTimeList[nId] - and then opens MowMain, which is why this payload is only
    /// ever sent in answer to the player's own click (AlienDefense_CheckOpenAct handler); the DLC
    /// button gets its window from the AlienDefenseLevelWindows client patch instead.
    /// </summary>
    public static JsonObject BuildAlienDefenseTimePayload(uint actId, long start, long end)
    {
        return new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = actId,
            ["nStartTime"] = start,
            ["nEndTime"] = end
        };
    }

    /// <summary>
    /// 弑神斗场 (Teamboss=10): the client's SeasonBoss_GMChangeTime callback
    /// (Challenge/SeasonBoss/SeasonBoss.lua:1115) patches SeasonBossLogic.tbTimeIndexCfg by nID.
    /// SeasonBoss rows are per (ActID, TermID, ZoneID) and carry their own AwardTime, so every row
    /// belonging to the act has to be sent, keyed by the row's primary Id.
    /// </summary>
    public static JsonObject BuildSeasonBossTimePayload(
        IEnumerable<(uint Id, long Start, long End, long Award)> rows)
    {
        var tbTerm = new JsonArray();
        foreach (var row in rows)
            tbTerm.Add(new JsonObject
            {
                ["nID"] = row.Id,
                ["nStartTime"] = row.Start,
                ["nEndTime"] = row.End,
                ["nRewardTime"] = row.Award
            });

        return new JsonObject { ["tbTerm"] = tbTerm };
    }

    /// <summary>
    /// Online rows belonging to an online group (DLC Rush 海姆优化作战 -&gt; group 105 -&gt; rows 105, 115).
    /// </summary>
    public static List<uint> SelectOnlineGroupRows(uint groupId)
    {
        var result = new List<uint>();
        if (groupId == 0)
            return result;

        foreach (var row in GameData.OnlineData.Values)
        {
            if (ReadUint(row.Group) == groupId)
                result.Add(row.Id);
        }

        result.Sort();
        return result;
    }

    /// <summary>Season boss rows belonging to <paramref name="actId"/> (DLC Teamboss 弑神斗场 -&gt; 10).</summary>
    public static List<SeasonBossExcel> SelectSeasonBossRows(uint actId)
    {
        var result = new List<SeasonBossExcel>();
        if (actId == 0)
            return result;

        foreach (var row in GameData.SeasonBossData.Values)
        {
            if (ReadUint(row.ActID) == actId)
                result.Add(row);
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>Tolerant numeric read for config values. The shipped tables mix JSON numbers and strings
    /// ("35", "", "[35]"), and JToken.ToObject&lt;uint?&gt;() throws a FormatException on the empty
    /// string, which would take the whole CallGS handler down with it.
    /// </summary>
    private static uint ToUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        var raw = token.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    /// <summary>Public wrapper so callers outside this class can read a mixed-typed config id.</summary>
    public static uint ReadUint(JToken? token) => ToUint(token);

    /// <summary>
    /// Flattens a nested reward table ([ [G,D,P,L,Count], ... ]) into the row shape
    /// RewardManager.GrantConfiguredRewardsAsync expects. Rows shorter than five values or with a
    /// zero count are dropped instead of throwing, because the shipped tables mix shapes freely.
    /// </summary>
    public static List<uint[]> ReadRewardRows(JToken? token)
    {
        var rows = new List<uint[]>();
        if (token is not JArray outer)
            return rows;

        foreach (var entry in outer)
        {
            if (entry is not JArray row || row.Count < 5)
                continue;

            var values = new uint[5];
            for (var i = 0; i < 5; i++)
                values[i] = ToUint(row[i]);

            if (values[0] != 0 && values[4] != 0)
                rows.Add(values);
        }

        return rows;
    }

    /// <summary>
    /// 心愿寄语 (Dorm) act id. RoleFestivalMessage.GetIsInActTime() is literally
    /// "GetActId() ~= 0" and the act id is read from attribute (gid 72, sid 1), so the pinned DLC's
    /// Dorm entry (DlcFuncConfId Dorm=3) doubles as the act id to seed at login. It nonetheless has
    /// to exist in house/MessageActivity/ActivityTime so the client has a window row for it.
    /// </summary>
    public static uint SelectRoleFestivalActId()
    {
        var act = GetPinnedActivity();
        var actId = ResolveFuncConfigId(act?.DlcFuncConfId, "Dorm");
        if (actId == 0)
            return 0;

        foreach (var row in GameData.RoleFestivalMessageData.Values)
        {
            if (ReadUint(row.ActivityID) == actId)
                return actId;
        }

        Logger.GetByClassName().Warn(
            $"Role festival act {actId} has no house/MessageActivity/ActivityTime row; skipping Dorm attribute.");
        return 0;
    }

    /// <summary>
    /// 多诺-巅峰对决 (Donoduel) season id. DonoDuel:GetActId() reads attribute (gid 185, sid 1) and
    /// DlcFuncDonoduel.OnGetTimeInfo -> DonoDuelConfig:GetSeasonConfig(cfg) returns nil for an unknown
    /// season, so the pinned DLC's Donoduel entry (DlcFuncConfId Donoduel=1) is used as the season id.
    /// It must match a dlc/DonoDuel/donoduel_season row for the client to have a window to patch.
    /// </summary>
    public static uint SelectDonoDuelSeasonId()
    {
        var act = GetPinnedActivity();
        var actId = ResolveFuncConfigId(act?.DlcFuncConfId, "Donoduel");
        if (actId == 0)
            return 0;

        foreach (var row in GameData.DonoDuelSeasonData.Values)
        {
            if (ReadUint(row.SeasonID) == actId)
                return actId;
        }

        Logger.GetByClassName().Warn(
            $"Donoduel season {actId} has no dlc/DonoDuel/donoduel_season row; skipping Donoduel attribute.");
        return 0;
    }

    /// <summary>
    /// 异星守护 (Mow) act id. AlienDefense.GetActId() reads attribute (gid 143, sid 1) and
    /// GetCurTimeConfig() returns nil while it is zero, so the whole mode (entry, level type
    /// lookup, score awards) is dead until the act id is seeded at login. The pinned DLC's Mow
    /// entry (DlcFuncConfId Mow=4) doubles as the act id. It must match a dlc/AlienDefense/alienlist
    /// row for the client to have a level table.
    /// </summary>
    public static uint SelectAlienDefenseActId()
    {
        var act = GetPinnedActivity();
        var actId = ResolveFuncConfigId(act?.DlcFuncConfId, "Mow");
        if (actId == 0)
            return 0;

        if (GameData.AlienDefenseData.ContainsKey(actId))
            return actId;

        Logger.GetByClassName().Warn(
            $"Alien defense act {actId} has no dlc/AlienDefense/alienlist row; skipping AlienDefense attribute.");
        return 0;
    }

    /// <summary>
    /// 弑神斗场 (Teamboss) act id. SeasonBossLogic.GetCurTimeCfg() returns nil while attribute
    /// (gid 18, sid 1) is zero, and CheckFirstEnter() then indexes that nil
    /// (Challenge/SeasonBoss/SeasonBoss.lua:1062-1064), so the entry raises a Lua error before the
    /// SeasonBoss_GMChangeTime push can have any effect. The act id (and the current term) are seeded
    /// at login instead.
    /// </summary>
    public static uint SelectSeasonBossActId()
    {
        var act = GetPinnedActivity();
        var actId = ResolveFuncConfigId(act?.DlcFuncConfId, "Teamboss");
        if (actId == 0)
            return 0;

        foreach (var row in GameData.SeasonBossData.Values)
        {
            if (ReadUint(row.ActID) == actId)
                return actId;
        }

        Logger.GetByClassName().Warn(
            $"Teamboss act {actId} has no challenge/seasonboss/seasonlist row; skipping SeasonBoss attribute.");
        return 0;
    }
}
