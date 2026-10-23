using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.BagRogue;

// 背包肉鸽 (BagRogue, DLC28) server state. The client owns the whole route state machine and only ever
// reads it back: every 142/61 slot below is written here and pushed with NtfSyncAttr -- the Lua side has
// no SetAttribute for group 142 or 61 at all.
//
// The two things that must never drift:
//   1. (142,1) has to name a real timelist row before CheckOpenAct answers open, or BackpackMain
//      dereferences a nil config and throws (umg_backpack.lua:104).
//   2. 61/1 is the only source of node type/level for the map widget (BagRogue.lua:386-404), and every
//      node's "D" is an *array*: fight/boss read D[1] as a level/story id, adventure passes D straight to
//      the option list, ending reads S instead (uw_backpack_map_node.lua:141-171).
public static class BagRogueService
{
    private const uint Gid = AttrIds.BagRogue.GroupId;
    private const uint StrGid = AttrIds.BagRogue.StringGroupId;

    public const uint EventFight = 1;
    public const uint EventAdventure = 2;
    public const uint EventStore = 3;
    public const uint EventRandom = 4;
    public const uint EventBoss = 5;
    public const uint EventEnding = 6;

    private static readonly Logger Log = new(nameof(BagRogueService));

    /// <summary>Nodes of one route map, ordered by line so the client paints them left to right.</summary>
    public static List<BagRogueNodeExcel> NodesOf(uint mapId) =>
        GameData.BagRogueNodeData.Values
            .Where(node => node.MapId == mapId)
            .OrderBy(node => node.Line)
            .ThenBy(node => node.PositionId)
            .ToList();

    private static long Unix(string? raw) =>
        DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(raw));

    /// <summary>
    /// Picks the period to run. Every timelist window is historical (last one ended 2025-07-07), so the
    /// fallback chain matters more than the containment test: window first, then the latest period that
    /// has already started, then the highest id.
    /// </summary>
    public static BagRogueScheduleExcel? SelectSchedule(DateTime now)
    {
        var rows = GameData.BagRogueScheduleData.Values.OrderBy(row => row.Id).ToList();
        if (rows.Count == 0)
            return null;

        var current = new DateTimeOffset(now).ToUnixTimeSeconds();
        foreach (var row in rows)
        {
            var start = Unix(row.Start);
            var end = Unix(row.End);
            if (start > 0 && end > 0 && current >= start && current <= end)
                return row;
        }

        var started = rows
            .Where(row => Unix(row.Start) > 0 && Unix(row.Start) <= current)
            .OrderByDescending(row => row.Id)
            .FirstOrDefault();

        return started ?? rows[^1];
    }

    public static BagRogueScheduleExcel? CurrentSchedule => SelectSchedule(DateTime.Now);

    /// <summary>Period id the client should treat as current; falls back to the config when unset.</summary>
    public static uint ScheduleId(PlayerInstance player)
    {
        var stored = player.Attributes.GetValue(Gid, AttrIds.BagRogue.ActivitySid);
        return stored != 0 ? stored : CurrentSchedule?.Id ?? 0;
    }

    public static BagRogueScheduleExcel? ScheduleOf(PlayerInstance player)
    {
        var id = ScheduleId(player);
        if (id != 0 && GameData.BagRogueScheduleData.TryGetValue(id, out var row))
            return row;

        return CurrentSchedule;
    }

    /// <summary>Login seeding: the period must be in place before the panel is ever opened.</summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var schedule = SelectSchedule(DateTime.Now);
        if (schedule == null)
            return;

        if (player.Attributes.GetValue(Gid, AttrIds.BagRogue.ActivitySid) == schedule.Id)
            return;

        var attr = player.Attributes.Set(Gid, AttrIds.BagRogue.ActivitySid, schedule.Id);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Stage id for a battle node: the first stage of the node's level group inside the period band.</summary>
    public static uint BattleLevelFor(BagRogueNodeExcel node, BagRogueScheduleExcel? schedule)
    {
        if (GameData.BagRogueLevelData.Count == 0)
            return 0;

        // Stage ids are banded per period: 8300..8399 for the DLC28 maps 1..4, 8500..8699 for the
        // 211..232 maps introduced by the later periods. Groups overlap between the bands (group 5 exists
        // in both), so filtering by band first keeps a late map from opening an early stage.
        var late = schedule != null && schedule.MapIds.Count > 0 && schedule.MapIds.Max() >= 200;
        var min = late ? 8500u : 8300u;
        var max = late ? 8699u : 8399u;

        var candidates = GameData.BagRogueLevelData.Values
            .Where(level => level.GroupId == node.LevelGroup && level.Id >= min && level.Id <= max)
            .OrderBy(level => level.Id)
            .ToList();

        if (candidates.Count == 0)
            candidates = GameData.BagRogueLevelData.Values
                .Where(level => level.GroupId == node.LevelGroup)
                .OrderBy(level => level.Id)
                .ToList();

        if (candidates.Count == 0)
        {
            Log.Warn($"BagRogue: no stage for level group {node.LevelGroup} (node {node.NodeId}).");
            return 0;
        }

        // Deeper lines use the later stages of the group so the route ramps in difficulty.
        var index = Math.Min(candidates.Count - 1, (int)(node.Line / 4));
        return candidates[index].Id;
    }

    /// <summary>Adventure option ids of one group, in table order (the client shows them as D[1..n]).</summary>
    public static List<uint> OptionsOfGroup(uint groupId) =>
        GameData.BagRogueAdventureData.Values
            .Where(option => option.GroupId == groupId)
            .OrderBy(option => option.Id)
            .Select(option => option.Id)
            .ToList();

    /// <summary>
    /// Story id a boss node plays. mapnode.json leaves StoryBranch empty, so the ending table is keyed by
    /// the route map instead: maps 1..4 map to story 1..4, then 211/212/213/221/222/231/232 to 5..10.
    /// </summary>
    public static uint StoryForMap(uint mapId)
    {
        if (GameData.BagRogueStoryData.Count == 0)
            return 0;

        var index = mapId switch
        {
            211 => 5u,
            212 => 6u,
            213 => 7u,
            221 => 8u,
            222 => 9u,
            231 or 232 => 10u,
            _ => mapId
        };

        return Math.Clamp(index, 1u, GameData.BagRogueStoryData.Values.Max(row => row.Id));
    }

    /// <summary>Ending type stored on an EventType 6 node as "S" (1..10).</summary>
    public static uint EndingTypeFor(BagRogueNodeExcel node)
    {
        if (GameData.BagRogueStoryData.Count == 0)
            return 0;

        var story = StoryForMap(node.MapId);
        return GameData.BagRogueStoryData.TryGetValue(story, out var row) ? row.EndingType : 0;
    }

    /// <summary>Builds the 61/1 node table: {"N&lt;nodeId&gt;": {T,C,D[],S}}.</summary>
    public static JsonObject BuildNodeTable(PlayerInstance player, BagRogueScheduleExcel schedule, uint mapId)
    {
        var table = new JsonObject();
        if (mapId == 0)
            return table;

        foreach (var node in NodesOf(mapId))
        {
            var entry = new JsonObject
            {
                ["T"] = node.EventType,
                ["C"] = 0
            };

            switch (node.EventType)
            {
                case EventFight:
                {
                    var level = BattleLevelFor(node, schedule);
                    if (level != 0)
                        entry["D"] = new JsonArray(level);
                    break;
                }

                case EventRandom:
                {
                    // The client has no widget branch for EVENTTYPE_RANDOM: a random node resolves to a
                    // playable one server side, otherwise clicking it does nothing and the route stalls.
                    var level = BattleLevelFor(node, schedule);
                    if (level != 0)
                    {
                        entry["T"] = EventFight;
                        entry["D"] = new JsonArray(level);
                    }
                    break;
                }

                case EventBoss:
                {
                    var story = StoryForMap(node.MapId);
                    if (story != 0)
                        entry["D"] = new JsonArray(story);
                    break;
                }

                case EventAdventure:
                {
                    var options = OptionsOfGroup(node.AdventureGroupId);
                    if (options.Count > 0)
                    {
                        var array = new JsonArray();
                        foreach (var option in options)
                            array.Add(option);
                        entry["D"] = array;
                    }
                    break;
                }

                case EventEnding:
                {
                    var ending = EndingTypeFor(node);
                    if (ending != 0)
                        entry["S"] = ending;
                    break;
                }
            }

            table[$"N{node.NodeId}"] = entry;
        }

        return table;
    }

    /// <summary>Starts a new route: picks a map of the period and writes (142,4), (142,6), 61/1.</summary>
    public static void StartRoute(PlayerInstance player, BagRogueScheduleExcel schedule, NtfSyncPlayer sync)
    {
        var maps = schedule.MapIds.Count > 0 ? schedule.MapIds : [1u];
        // Rotate on the clear counter so consecutive runs differ but stay replayable.
        var clears = ClearCount(player);
        var mapId = maps[(int)(clears % (uint)maps.Count)];

        var levelAttr = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentLevelNodeSid, mapId);
        player.Attributes.SyncTo(sync, levelAttr);

        var nodeAttr = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentWalkNodeSid, 0);
        player.Attributes.SyncTo(sync, nodeAttr);

        var bagMax = player.Attributes.Set(
            Gid,
            AttrIds.BagRogue.BagMaxIndexSid,
            schedule.BagVolume == 0 ? 20u : schedule.BagVolume);
        player.Attributes.SyncTo(sync, bagMax);

        var stockroom = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentStockroomNumSid, 0);
        player.Attributes.SyncTo(sync, stockroom);

        var nodes = player.Attributes.SetString(
            StrGid, AttrIds.BagRogue.StringLevelNodeSid, BuildNodeTable(player, schedule, mapId).ToJsonString());
        player.Attributes.SyncTo(sync, nodes);

        var equip = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringEquipBagSid, "{}");
        player.Attributes.SyncTo(sync, equip);

        var reward = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringMapNodeRewardSid, "[]");
        player.Attributes.SyncTo(sync, reward);

        RecordOpen(player, sync);

        Log.Info($"BagRogue route: period {schedule.Id}, map {mapId}, " +
                 $"{NodesOf(mapId).Count} node(s) for uid={player.Uid}.");
    }

    /// <summary>
    /// Validates a walk target and marks it visited. A node is reachable when every PreNode is already
    /// visited and the node itself is not; the client only checks the same rules against its local copy
    /// of the table (BagRogue.lua:441-462), so the server has to enforce them on 61/1.
    /// </summary>
    public static bool TryWalk(PlayerInstance player, uint nodeId, NtfSyncPlayer sync, out string error)
    {
        error = string.Empty;

        var mapId = player.Attributes.GetValue(Gid, AttrIds.BagRogue.CurrentLevelNodeSid);
        if (mapId == 0)
        {
            error = "ui.TxtChapterUnfinish";
            return false;
        }

        if (!GameData.BagRogueNodeData.TryGetValue(nodeId, out var node) || node.MapId != mapId)
        {
            error = "error.BadParam";
            return false;
        }

        var table = JsonNode.Parse(ReadNodeTable(player)) as JsonObject ?? new JsonObject();
        if (table[$"N{nodeId}"] is not JsonObject entry)
        {
            error = "bagrogue.massage_NodeLock";
            return false;
        }

        if (ReadUint(entry["C"]) != 0)
        {
            error = "bagrogue.massage_NodeExecuted";
            return false;
        }

        foreach (var previous in node.PreNodes)
        {
            if (table[$"N{previous}"] is not JsonObject parent || ReadUint(parent["C"]) == 0)
            {
                error = "bagrogue.massage_NodeLock";
                return false;
            }
        }

        entry["C"] = 1;

        var nodes = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringLevelNodeSid,
            table.ToJsonString());
        player.Attributes.SyncTo(sync, nodes);

        var walk = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentWalkNodeSid, nodeId);
        player.Attributes.SyncTo(sync, walk);
        return true;
    }

    public static string ReadNodeTable(PlayerInstance player) =>
        player.Attributes.GetStringValue(StrGid, AttrIds.BagRogue.StringLevelNodeSid) ?? "{}";

    public static int ClearCount(PlayerInstance player) =>
        (int)((player.Attributes.GetValue(Gid, AttrIds.BagRogue.LevelOpenNumSid) >> 16) & 0xFFFF);

    /// <summary>Bits 0-15 of (142,2) count started runs and bits 16-31 count finished ones.</summary>
    public static void RecordOpen(PlayerInstance player, NtfSyncPlayer sync) => BumpCounter(player, sync, false);

    /// <summary>Only an ending node finishes a run; settlement of an ordinary node must not count.</summary>
    public static void RecordClear(PlayerInstance player, NtfSyncPlayer sync) => BumpCounter(player, sync, true);

    private static void BumpCounter(PlayerInstance player, NtfSyncPlayer sync, bool clear)
    {
        var attr = player.Attributes.Get(Gid, AttrIds.BagRogue.LevelOpenNumSid);
        var bits = attr?.Val ?? 0;
        var opens = bits & 0xFFFF;
        var clears = (bits >> 16) & 0xFFFF;

        if (clear)
            clears = Math.Min(0xFFFFu, clears + 1);
        else
            opens = Math.Min(0xFFFFu, opens + 1);

        var updated = player.Attributes.Set(Gid, AttrIds.BagRogue.LevelOpenNumSid, (clears << 16) | opens);
        player.Attributes.SyncTo(sync, updated);
    }

    /// <summary>Ends the run: clears the route and hands back the accumulated rewards.</summary>
    public static JsonArray FinishRoute(PlayerInstance player, NtfSyncPlayer sync)
    {
        // 61/3 is a JSON *string*; handing the raw text to ReadRewardRows would make it a JValue and read
        // back as an empty table, silently dropping every reward the run collected.
        var accumulated = player.Attributes.GetStringValue(StrGid, AttrIds.BagRogue.StringMapNodeRewardSid);
        var awards = new JsonArray();
        if (JsonNode.Parse(accumulated ?? "[]") is JsonArray table)
        {
            foreach (var row in table)
            {
                if (row is not JsonArray cells)
                    continue;

                var item = new JsonArray();
                foreach (var cell in cells)
                    item.Add(cell?.DeepClone());
                awards.Add(item);
            }
        }

        var node = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentLevelNodeSid, 0);
        player.Attributes.SyncTo(sync, node);
        var walk = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentWalkNodeSid, 0);
        player.Attributes.SyncTo(sync, walk);
        var stockroom = player.Attributes.Set(Gid, AttrIds.BagRogue.CurrentStockroomNumSid, 0);
        player.Attributes.SyncTo(sync, stockroom);

        var nodes = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringLevelNodeSid, "{}");
        player.Attributes.SyncTo(sync, nodes);
        var equip = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringEquipBagSid, "{}");
        player.Attributes.SyncTo(sync, equip);
        var reward = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringMapNodeRewardSid, "[]");
        player.Attributes.SyncTo(sync, reward);

        return awards;
    }

    /// <summary>Appends the rewards of one cleared node to the run total kept in 61/3.</summary>
    public static void AccumulateRewards(PlayerInstance player, IReadOnlyList<IReadOnlyList<uint>> rows,
        NtfSyncPlayer sync)
    {
        if (rows.Count == 0)
            return;

        var table = JsonNode.Parse(
            player.Attributes.GetStringValue(StrGid, AttrIds.BagRogue.StringMapNodeRewardSid) ?? "[]")
            as JsonArray ?? new JsonArray();

        foreach (var row in rows)
        {
            var item = new JsonArray();
            foreach (var cell in row)
                item.Add(cell);
            table.Add(item);
        }

        var attr = player.Attributes.SetString(StrGid, AttrIds.BagRogue.StringMapNodeRewardSid,
            table.ToJsonString());
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Records the ending bit (142,15) and the best-run record read by the second settlement panel.</summary>
    public static void RecordEnding(PlayerInstance player, uint endingType, uint score, uint time,
        NtfSyncPlayer sync)
    {
        if (endingType is > 0 and < 32)
        {
            var attr = player.Attributes.Get(Gid, AttrIds.BagRogue.UnlockEndingStoryInfoSid);
            var bits = attr?.Val ?? 0;
            var updated = player.Attributes.Set(
                Gid, AttrIds.BagRogue.UnlockEndingStoryInfoSid, bits | (1u << (int)endingType));
            player.Attributes.SyncTo(sync, updated);
        }

        var record = new JsonObject
        {
            ["roleformation"] = new JsonArray(),
            ["TotalBestScore"] = score,
            ["TotalBestTime"] = time,
            ["BestScore"] = score,
            ["BestTime"] = time,
            ["CurScore"] = score,
            ["CurTime"] = time
        };

        var recordAttr = player.Attributes.SetString(
            StrGid, AttrIds.BagRogue.StringBestEndNodeRecordSid, record.ToJsonString());
        player.Attributes.SyncTo(sync, recordAttr);
    }

    public static BagRogueNodeExcel? CurrentNode(PlayerInstance player)
    {
        var nodeId = player.Attributes.GetValue(Gid, AttrIds.BagRogue.CurrentWalkNodeSid);
        return nodeId != 0 && GameData.BagRogueNodeData.TryGetValue(nodeId, out var node) ? node : null;
    }

    /// <summary>Reward rows paid out when the node is finished (mapnode.json's Reward column).</summary>
    public static List<List<uint>> NodeRewardRows(BagRogueNodeExcel node) => node.Rewards;

    public static uint ReadUint(JsonNode? node, uint fallback = 0)
    {
        if (node is JsonValue value && value.TryGetValue<uint>(out var parsed))
            return parsed;

        return uint.TryParse(node?.ToString(), out var text) ? text : fallback;
    }
}
