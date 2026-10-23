using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.StarWish;

// 星祈心愿 (StarWish) -- the DLC34 "DlcFuncCelebrate" panel. Unlike the combat mini games this one
// has no settlement channel and no leaderboard: every reward is claimed through one of the six
// panel apis, and all of the progress lives in attribute group 180 (DLC/StarWish/StarWishLogic.lua:8).
//
// Attribute layout (docs/contracts/dlc-minigames.md §6, all read points verified line by line):
//     1                  current period id; 0 means "closed" and gates ~10 client branches
//     2                  highest claimed score-award tier id
//     3                  tree part stage (client only reads it, we never advance it)
//     4                  repeat-quest extra award bits, bit nPos (1..3)
//     70 + type (71..77) equipped jigsaw id per part type -- the client writes these itself through
//                        me:SetAttribute (local cache), so the server only seeds a default
//     80 + id / 30       jigsaw ownership bitmap, bit = id % 30  (note: %30, not id-1)
//     90 + page / 30     story page claim bitmap, bit = page % 30
//     100 + 3 * id       story quest progress (compared against CheckValue)
//     101 + 3 * id       story quest reward claimed flag
//     102 + 3 * id       story quest sub step bitmap, bit = nPos
//
// Bitmaps are extracted by the client as 32-bit signed segments (Misc/Lib.lua:1014-1026), so every
// value we write has to stay below 2^31 -- ids are capped at 704, which keeps us far away from that.
public static class StarWishService
{
    public static readonly Logger Log = new(nameof(StarWishService));

    private const uint Gid = AttrIds.StarWish.Gid;

    /// <summary>Bits 1..3 of this sid record the three repeat-quest extra awards.</summary>
    private const uint ExtraAwardBitBase = 0;

    /// <summary>"Dorm gift already handed out" flag. The client does not read it; it only keeps the
    /// auto claim (StarWishLogic.lua:1092-1097) from re-firing the furniture gift every time the last
    /// story quest is re-reported.</summary>
    private const uint AllCompleteGiftSid = 5;

    /// <summary>Jigsaw ids are 1..21 for the current table; the client caps the usable range at 300
    /// (StarWishLogic.lua:457), so bound the bitmap walk by that rather than by table row count.</summary>
    private const uint MaxJigsawId = 300;

    /// <summary>Picks the period to serve. The only row is ID=1 (2025-12-25 .. 2026-01-29), which is
    /// long expired; we still serve it so the panel opens under the era pin. Selection mirrors
    /// <see cref="WorldBossService"/>: window first, then the newest already-started row, then the
    /// highest id.</summary>
    public static StarWishMainExcel? SelectAct(DateTime now)
    {
        if (GameData.StarWishMainData.Count == 0)
            return null;

        var unixNow = (long)new DateTimeOffset(now).ToUnixTimeSeconds();
        StarWishMainExcel? fallback = null;

        foreach (var act in GameData.StarWishMainData.Values.OrderBy(a => a.Id))
        {
            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(act.Start));
            var end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(act.End));

            if (start != 0 && end != 0 && unixNow >= start && unixNow <= end)
                return act;

            if (start != 0 && unixNow >= start)
                fallback = act;
        }

        return fallback ?? GameData.StarWishMainData.OrderByDescending(pair => pair.Key).First().Value;
    }

    public static uint GetActId(PlayerInstance player) =>
        player.Attributes.GetValue(Gid, AttrIds.StarWish.ActivitySid);

    public static bool IsOpen(PlayerInstance player) => GetActId(player) != 0;

    /// <summary>Login / era-switch seeding: pins the period id and (only when the slot is empty)
    /// assigns the type-1 tree part so the tree renders before the player ever opens the panel.</summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var act = SelectAct(DateTime.Now);
        if (act == null)
        {
            Log.Warn("No StarWish period row is loaded; the panel will answer bOpen=false.");
            return;
        }

        if (GetActId(player) != act.Id)
            Set(player, AttrIds.StarWish.ActivitySid, act.Id, sync);

        var equipSid = AttrIds.StarWish.EquipJigsawOffsetSid + 1;
        if (player.Attributes.GetValue(Gid, equipSid) == 0)
        {
            var firstType1 = GameData.StarWishJigsawData.Values
                .Where(j => j.Type == 1)
                .OrderBy(j => j.Id)
                .Select(j => j.Id)
                .FirstOrDefault();
            if (firstType1 != 0)
                Set(player, equipSid, firstType1, sync);
        }
    }

    public static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var current = player.Attributes.GetValue(Gid, sid);
        if (current == value)
        {
            // Still push the value: the client may have lost it across a reconnect, and the slot
            // existed already so there is no "don't create empty rows" concern here.
            if (sync != null)
                player.Attributes.SyncTo(sync, Gid, sid, value);
            return;
        }

        var attr = player.Attributes.Set(Gid, sid, value);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    // ---- jigsaw (tree parts) -------------------------------------------------------------

    public static uint JigsawSid(uint id) => AttrIds.StarWish.JigsawIdOffsetSid + id / 30;
    public static uint JigsawBit(uint id) => id % 30;

    public static bool OwnsJigsaw(PlayerInstance player, uint id) =>
        (player.Attributes.GetValue(Gid, JigsawSid(id)) & (1u << (int)JigsawBit(id))) != 0;

    public static List<uint> OwnedJigsaws(PlayerInstance player)
    {
        var owned = new List<uint>();
        foreach (var id in GameData.StarWishJigsawData.Keys.OrderBy(k => k))
        {
            if (OwnsJigsaw(player, id))
                owned.Add(id);
        }

        return owned;
    }

    public static void GrantJigsaw(PlayerInstance player, uint id, NtfSyncPlayer? sync)
    {
        var sid = JigsawSid(id);
        var value = player.Attributes.GetValue(Gid, sid) | (1u << (int)JigsawBit(id));
        Set(player, sid, value, sync);
    }

    /// <summary>Collection score = sum of the Score column over owned parts (jigsaw.json gives the
    /// first part 0 and every other part 10, so a full collection is exactly 200).</summary>
    public static uint Score(PlayerInstance player)
    {
        uint total = 0;
        foreach (var part in GameData.StarWishJigsawData.Values)
        {
            if (OwnsJigsaw(player, part.Id))
                total += part.Score;
        }

        return total;
    }

    /// <summary>Rolls one unowned part. Returns 0 when everything is already owned.</summary>
    public static uint RollJigsaw(PlayerInstance player)
    {
        var missing = GameData.StarWishJigsawData.Values
            .Where(part => !OwnsJigsaw(player, part.Id))
            .Select(part => part.Id)
            .OrderBy(id => id)
            .ToList();

        if (missing.Count == 0)
            return 0;

        return missing[Random.Shared.Next(missing.Count)];
    }

    // ---- story quests --------------------------------------------------------------------

    public static uint StoryProgressSid(uint id) =>
        AttrIds.StarWish.StoryQuestIdOffsetSid + AttrIds.StarWish.SidStride * id;

    public static uint GetStoryProgress(PlayerInstance player, uint id) =>
        player.Attributes.GetValue(Gid, StoryProgressSid(id));

    public static bool IsStoryQuestCompleted(PlayerInstance player, StarWishStoryQuestExcel quest) =>
        GetStoryProgress(player, quest.Id) >= quest.CheckValue;

    public static bool IsStoryQuestClaimed(PlayerInstance player, uint id) =>
        player.Attributes.GetValue(Gid, StoryProgressSid(id) + 1) != 0;

    public static void SetStoryProgress(PlayerInstance player, uint id, uint value, NtfSyncPlayer? sync)
    {
        var sid = StoryProgressSid(id);
        if (player.Attributes.GetValue(Gid, sid) < value)
            Set(player, sid, value, sync);
    }

    public static uint StoryPageSid(uint page) => AttrIds.StarWish.StoryQuestPageStartSid + page / 30;
    public static uint StoryPageBit(uint page) => page % 30;

    public static bool IsStoryPageClaimed(PlayerInstance player, uint page) =>
        (player.Attributes.GetValue(Gid, StoryPageSid(page)) & (1u << (int)StoryPageBit(page))) != 0;

    public static void MarkStoryPageClaimed(PlayerInstance player, uint page, NtfSyncPlayer? sync)
    {
        var sid = StoryPageSid(page);
        var value = player.Attributes.GetValue(Gid, sid) | (1u << (int)StoryPageBit(page));
        Set(player, sid, value, sync);
    }

    public static bool IsSubStepDone(PlayerInstance player, uint id, uint pos) =>
        (player.Attributes.GetValue(Gid, StoryProgressSid(id) + 2) & (1u << (int)pos)) != 0;

    public static void SetSubStep(PlayerInstance player, uint id, uint pos, bool done, NtfSyncPlayer? sync)
    {
        var sid = StoryProgressSid(id) + 2;
        var current = player.Attributes.GetValue(Gid, sid);
        var bit = 1u << (int)pos;
        var value = done ? current | bit : current & ~bit;
        if (value != current)
            Set(player, sid, value, sync);
    }

    /// <summary>Reads CheckValue for a story quest, falling back to 1 for rows the exporter left
    /// empty (StarWishConfig.lua does the same).</summary>
    public static uint CheckValueOf(uint id) =>
        GameData.StarWishStoryQuestData.TryGetValue(id, out var quest) ? quest.CheckValue : 1u;

    // ---- repeat quests (achievement rows, group 7) ---------------------------------------

    public static bool IsRepeatQuestDone(PlayerInstance player, uint id) =>
        AchievementClaimService.IsFinished(player, AttrIds.Achievement.QuestGroupId, id,
            AchievementCheckValue(id));

    public static bool IsRepeatQuestClaimed(PlayerInstance player, uint id) =>
        AchievementClaimService.IsClaimed(player, AttrIds.Achievement.QuestGroupId, id);

    public static uint CompletedRepeatQuestCount(PlayerInstance player, StarWishMainExcel act)
    {
        uint count = 0;
        foreach (var id in act.RepeatQuests)
        {
            if (IsRepeatQuestDone(player, id))
                count++;
        }

        return count;
    }

    private static uint AchievementCheckValue(uint id)
    {
        if (GameData.ActivityQuestData.TryGetValue(id, out var quest) && quest.CheckValue != 0)
            return quest.CheckValue;

        return 1;
    }

    // ---- extra awards --------------------------------------------------------------------

    /// <summary>How many repeat quests were *received* (not merely finished). The panel gates the
    /// three extra awards on this number (umg_starwish_autotask.lua:95-112, capped at 3).</summary>
    public static uint ReceivedRepeatQuestCount(PlayerInstance player, StarWishMainExcel act)
    {
        uint count = 0;
        foreach (var id in act.RepeatQuests)
        {
            if (IsRepeatQuestClaimed(player, id))
                count++;
        }

        return Math.Min(count, 3u);
    }

    public static uint ExtraAwardBit(uint pos) => 1u << (int)(ExtraAwardBitBase + pos);

    public static bool IsExtraAwardClaimed(PlayerInstance player, uint pos) =>
        (player.Attributes.GetValue(Gid, AttrIds.StarWish.RepeatQuestExtraAwardSid) & ExtraAwardBit(pos)) != 0;

    public static void MarkExtraAwardClaimed(PlayerInstance player, uint pos, NtfSyncPlayer? sync)
    {
        var current = player.Attributes.GetValue(Gid, AttrIds.StarWish.RepeatQuestExtraAwardSid);
        Set(player, AttrIds.StarWish.RepeatQuestExtraAwardSid, current | ExtraAwardBit(pos), sync);
    }

    // ---- all complete gift ----------------------------------------------------------------

    public static bool AllStoryQuestsComplete(PlayerInstance player, StarWishMainExcel act)
    {
        foreach (var id in act.StoryQuests)
        {
            if (!GameData.StarWishStoryQuestData.TryGetValue(id, out var quest))
                continue;
            if (!IsStoryQuestCompleted(player, quest))
                return false;
        }

        return act.StoryQuests.Count > 0;
    }

    public static bool IsAllCompleteGiftGiven(PlayerInstance player) =>
        player.Attributes.GetValue(Gid, AllCompleteGiftSid) != 0;

    public static void MarkAllCompleteGiftGiven(PlayerInstance player, NtfSyncPlayer? sync) =>
        Set(player, AllCompleteGiftSid, 1, sync);
}
