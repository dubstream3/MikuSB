using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 先行试用 / 角色试玩 (gacha try) state. The client module is Activity/GachaTry.lua and the act rows
/// are the activities.txt rows with Class "gacha_try" that the DLC page points at through
/// dlc_activities.txt TryID (DLC 35 -> groups 561/576, 38 rows, all of them the era's own roster).
///
/// Unlike the return activity there is no player-level act slot: the client always names the act it
/// is looking at (nActId in every request), so the server validates the id and falls back to the era's
/// own act when it is missing.
///
/// Progress lives in one attribute word per act, in the group the client reaches through
/// Activity.GetDiyTaskId(nActId, nPos) = nActId * 10 + nPos + 2 with nPos = GachaTry.RewardGet_Task
/// (= 1), i.e. (50, actId * 10 + 3). Bit nGirlIdx is the "cleared, reward taken" flag the client
/// reads back through GachaTry.IsLevelPassed (GetBits(data, girlIdx, girlIdx) == 1), and it is also
/// what paints the reward row of the try UI.
/// </summary>
public static class GachaTryService
{
    private const string TryClass = "gacha_try";

    /// <summary>GachaTry.RewardGet_Task (Activity/GachaTry.lua:8).</summary>
    private const uint RewardGetTask = 1;

    /// <summary>Highest girl index that still fits the 32 bit progress word.</summary>
    private const uint MaxGirlIdx = 31;

    private static readonly Logger Log = new(nameof(GachaTryService));

    /// <summary>Activity.GetDiyTaskId(nActId, RewardGetTask) = nActId * 10 + nPos + 2.</summary>
    public static uint RewardSid(uint actId) => (actId * 10) + RewardGetTask + 2;

    /// <summary>
    /// Group ids of the pinned DLC's own 先行试用 roster, read from dlc_activities.TryID
    /// ([561, 576] for DLC 35; DLC_Logic.lua:72 reads the same field into tbDlcFuncConfId.Try). Empty
    /// when pinning is off or the row carries no TryID. The field ships as a JSON array, so the array
    /// shape is handled first and a whitespace/comma separated string is accepted as a fallback.
    /// </summary>
    public static List<uint> SelectTryGroupIds()
    {
        var result = new List<uint>();
        var token = DlcRuntimeOverrides.GetPinnedActivity()?.TryID;
        if (token == null || token.Type == JTokenType.Null)
            return result;

        if (token is JArray array)
        {
            foreach (var item in array)
            {
                var id = DlcRuntimeOverrides.ReadUint(item);
                if (id != 0 && !result.Contains(id))
                    result.Add(id);
            }
        }
        else
        {
            foreach (Match match in Regex.Matches(token.ToString(), @"\d+"))
            {
                if (uint.TryParse(match.Value, out var id) && id != 0 && !result.Contains(id))
                    result.Add(id);
            }
        }

        result.Sort();
        return result;
    }

    /// <summary>
    /// Every try act of the activated era: the activities.txt rows of class "gacha_try" that sit in one
    /// of the pinned DLC's TryID groups and whose window belongs to that era (start at or after the
    /// activation floor, window not historical). The try page shows one entry per live row of those
    /// groups (UMG/Activitytry/umg_activitytrydlc.lua:150-169 keeps every Activity.IsOpen row of
    /// Activity.GetCaseByGroup(group)), so the whole 38 row roster of DLC 35 has to be live, not just
    /// the single act <see cref="SelectActId"/> reports. Later eras' try acts are skipped on purpose, so
    /// a pinned DLC only ever opens its own roster.
    /// </summary>
    public static List<uint> SelectEraActIds()
    {
        var result = new List<uint>();
        var floor = DlcRuntimeOverrides.ActivationFloor();
        var groups = SelectTryGroupIds();

        foreach (var act in GameData.GachaTryData.Values)
        {
            if (!GameData.ActivityData.TryGetValue(act.ActId, out var conf))
                continue;

            if (!string.Equals(conf.Class, TryClass, StringComparison.OrdinalIgnoreCase))
                continue;

            if (groups.Count > 0 && !groups.Contains(DlcRuntimeOverrides.ReadUint(conf.GroupId)))
                continue;

            if (DlcRuntimeOverrides.IsHistoricalWindow(conf.EndTime))
                continue;

            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(conf.StartTime));
            if (start == 0 || (floor != 0 && start < floor))
                continue;

            result.Add(act.ActId);
        }

        result.Sort();
        return result;
    }

    /// <summary>
    /// The primary try act of the activated era: the roster row with the earliest window start (580 for
    /// the pinned DLC 35, the only row of its groups that starts inside the era itself). Used as the
    /// fallback when a request names an act the tables do not know.
    /// </summary>
    public static uint SelectActId()
    {
        var best = 0u;
        var bestStart = long.MaxValue;

        foreach (var actId in SelectEraActIds())
        {
            if (!GameData.ActivityData.TryGetValue(actId, out var conf))
                continue;

            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(conf.StartTime));
            if (start >= bestStart)
                continue;

            bestStart = start;
            best = actId;
        }

        return best;
    }

    /// <summary>
    /// Accepts the act id the client sent when it is a live try act of this era, otherwise falls back
    /// to <see cref="SelectActId"/>. The client reads nActId out of its own DLC configuration, so the
    /// fallback only matters for hand-made requests (GM commands, replays).
    /// </summary>
    public static uint ResolveActId(uint requested)
    {
        if (requested != 0 && GameData.GachaTryData.ContainsKey(requested))
            return requested;

        return SelectActId();
    }

    /// <summary>Mirrors GachaTry.IsLevelPassed: bit nGirlIdx of the act's progress word.</summary>
    public static bool IsGirlPassed(PlayerInstance player, uint actId, uint girlIdx)
    {
        if (actId == 0 || girlIdx == 0 || girlIdx > MaxGirlIdx)
            return false;

        var raw = player.Attributes.GetValue(AttrIds.Activity.GroupId, RewardSid(actId));
        return ((raw >> (int)girlIdx) & 1u) == 1u;
    }

    /// <summary>
    /// Settles one try girl: the first clear pays try_girl.Reward and sets the progress bit, later
    /// clears only re-assert the bit. Returns the granted rows as one reward "group", which is the
    /// shape both the settlement channel (Launch.tbAward is a list of groups, each group a list of
    /// [G,D,P,L,Count] rows - UMG/Success/umg_success.lua:158-175) and the already-passed list use.
    /// </summary>
    public static async Task<JsonArray> GrantGirlAsync(
        PlayerInstance player,
        uint actId,
        uint girlIdx,
        NtfSyncPlayer sync)
    {
        var group = new JsonArray();

        if (actId == 0 || girlIdx == 0 || girlIdx > MaxGirlIdx)
        {
            Log.Warn($"Gacha try settlement with bad id (act={actId}, girl={girlIdx}) for uid={player.Uid}.");
            return group;
        }

        if (!GameData.GachaTryData.TryGetValue(actId, out var act))
        {
            Log.Warn($"Gacha try settlement for unknown act {actId} (uid={player.Uid}).");
            return group;
        }

        var tryGirlId = act.TryGirlAt(girlIdx);
        var first = !IsGirlPassed(player, actId, girlIdx);

        if (first && GameData.TryGirlData.TryGetValue(tryGirlId, out var girl))
        {
            var rows = DlcRuntimeOverrides.ReadRewardRows(girl.Reward);
            var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var row in granted)
                group.Add(row?.DeepClone());
        }
        else if (first)
        {
            Log.Warn($"Gacha try act {actId} girl {girlIdx} has no try_girl {tryGirlId} row (uid={player.Uid}).");
        }

        var packed = player.Attributes.GetValue(AttrIds.Activity.GroupId, RewardSid(actId)) | (1u << (int)girlIdx);
        SetAttr(player, AttrIds.Activity.GroupId, RewardSid(actId), packed, sync);
        Save(player);

        Log.Info($"Gacha try act {actId} girl {girlIdx} (try_girl {tryGirlId}) settled by uid={player.Uid}: " +
                 $"firstClear={first}, {group.Count} row(s) granted.");

        return group;
    }

    private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(gid, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    private static void Save(PlayerInstance player)
    {
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
    }
}
