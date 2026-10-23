using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// Shared helpers for the Donoduel mission attributes (group 186). The layout mirrors the client
/// reads in DLC/DonoDuel/DonoDuelTask.lua:182-222 - bit0 accepted (GetAccept), bit1 reward already
/// taken (GetReceive), bits2..31 progress (GetProgres). A mission is finished when its progress
/// reaches the CheckValue column of dlc/DonoDuel/donoduel_story_task.json.
/// </summary>
internal static class DonoDuelTaskState
{
    private const uint GroupId = AttrIds.Dlc.DonoDuelTaskGroupId;
    private const int ProgressShift = 2;
    private const uint ProgressMask = 0x3FFFFFFFu;

    /// <summary>Reads the "nId" field of an Accept/GetReward request.</summary>
    public static uint ReadMissionId(string param)
    {
        try
        {
            return JsonNode.Parse(param) is JsonObject request &&
                   uint.TryParse(request["nId"]?.ToString(), out var id)
                ? id
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Reads the "nCheckTaskId" field of a DoSomething request.</summary>
    public static uint ReadCheckTaskId(string param)
    {
        try
        {
            return JsonNode.Parse(param) is JsonObject request &&
                   uint.TryParse(request["nCheckTaskId"]?.ToString(), out var id)
                ? id
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Reads the "tbIdList" array of a QuickGetReward request.</summary>
    public static List<uint> ReadIdList(string param)
    {
        var result = new List<uint>();

        try
        {
            if (JsonNode.Parse(param) is not JsonObject request || request["tbIdList"] is not JsonArray list)
                return result;

            foreach (var entry in list)
            {
                if (uint.TryParse(entry?.ToString(), out var id) && id != 0)
                    result.Add(id);
            }
        }
        catch
        {
            // Malformed requests simply yield an empty list.
        }

        return result;
    }

    public static uint CheckValue(uint id) =>
        GameData.DonoDuelStoryTaskData.TryGetValue(id, out var row) ? ReadUint(row.CheckValue, 1) : 1u;

    /// <summary>
    /// Objective id 1 ("体验多诺对决", donoduel_story_task.json) is the arena-clear objective: its
    /// Param column carries the arena level id the player has to finish (missions 102, 106 and
    /// 109-114 point at 5505-5512). The client cannot track it - DonoDuelTask.tbFnCompare
    /// (DLC/DonoDuel/DonoDuelTask.lua:258-264) only implements objective type 1 for the base
    /// accumulation counter and CheckClientFinish (:515-520) only reports type 10 - so the server
    /// has to advance it from its own settlement, exactly like the official one does.
    /// </summary>
    public const uint ArenaClearObjectiveId = 1;

    public static uint GetProgress(PlayerInstance player, uint id) =>
        (player.Attributes.GetValue(GroupId, id) >> ProgressShift) & ProgressMask;

    /// <summary>
    /// Advances every mission whose objective is "clear arena level <paramref name="levelId"/>".
    /// Missions already rewarded are left alone, and progress only ever moves forward.
    /// </summary>
    public static int AdvanceArenaClearMissions(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        if (levelId == 0)
            return 0;

        var advanced = 0;

        foreach (var (id, row) in GameData.DonoDuelStoryTaskData)
        {
            if (ReadUint(row.Objectives) != ArenaClearObjectiveId || !ParamsContain(row.Param, levelId))
                continue;

            if (IsReceived(player, id))
                continue;

            if (!IsAccepted(player, id))
                MarkAccepted(player, id, sync);

            var target = CheckValue(id);
            if (GetProgress(player, id) < target)
                MarkProgress(player, id, target, sync);

            advanced++;
        }

        return advanced;
    }

    /// <summary>
    /// Objective id 2 ("前往获取多诺卡" / "完成1次多诺卡探索" / "在探索多诺卡中到达第N阶第M区",
    /// donoduel_story_task.json) is the rogue-mode objective (missions 103, 108 and 201-203). Its Param
    /// column is [floor, area]: a positive floor means "reach that exploration stage", -1 means "finish
    /// a whole exploration run". The client never reports it (DonoDuelTask.tbFnCompare has no entry for
    /// type 2 and DonoDuelRogue only reports rooms and mini games), so the server advances it from the
    /// rogue settlement.
    /// </summary>
    public const uint RogueCardObjectiveId = 2;

    /// <summary>Objective id 4 ("前往进行潜能提升", "累计为多诺卡提升N次星级").</summary>
    public const uint PotentialObjectiveId = 4;

    /// <summary>Objective id 5 ("和安卡希雅聊聊", side missions 301-308).</summary>
    public const uint StoryTalkObjectiveId = 5;

    /// <summary>Objective id 8 ("激活1个探索加速包").</summary>
    public const uint BoostPackObjectiveId = 8;

    /// <summary>Type column value of the main story list; 2 and 3 are the side and atlas lists.</summary>
    private const uint StoryTaskType = 1;

    /// <summary>
    /// Advances every rogue-mode mission whose Param threshold the settlement satisfies. Missions
    /// already rewarded are left alone and progress only moves forward.
    /// </summary>
    public static int AdvanceRogueMissions(
        PlayerInstance player, uint floor, uint area, bool runFinished, NtfSyncPlayer sync)
    {
        var advanced = 0;

        foreach (var (id, row) in GameData.DonoDuelStoryTaskData)
        {
            if (ReadUint(row.Objectives) != RogueCardObjectiveId || IsReceived(player, id))
                continue;

            var wantedFloor = ReadParamAt(row.Param, 0);
            var wantedArea = ReadParamAt(row.Param, 1);

            var satisfied = wantedFloor < 0
                ? runFinished
                : wantedFloor == 0 ||
                  (floor >= (uint)wantedFloor && (wantedArea <= 0 || area >= (uint)wantedArea));

            if (!satisfied)
                continue;

            Complete(player, id, sync);
            advanced++;
        }

        return advanced;
    }

    /// <summary>
    /// Completes the story missions whose objective is an action this server cannot observe ("go spot
    /// the potential upgrade", "chat with Anka", "activate a boost pack"). They only fire once the
    /// previous story mission has been rewarded, so the chapter order is preserved - without this the
    /// story stops dead at mission 104, because none of those actions has a request of its own
    /// (DonoDuel_UpgradeStar/UpgradeQuality/UpgradeAttr, DonoDuel_StudySkillTree ... are all missing).
    /// </summary>
    public static int AdvanceStoryActions(PlayerInstance player, NtfSyncPlayer sync)
    {
        var advanced = 0;

        foreach (var (id, row) in GameData.DonoDuelStoryTaskData)
        {
            if (ReadUint(row.Type) != StoryTaskType || IsReceived(player, id))
                continue;

            var objective = ReadUint(row.Objectives);
            if (objective != PotentialObjectiveId && objective != StoryTalkObjectiveId &&
                objective != BoostPackObjectiveId)
                continue;

            var preId = ReadUint(row.PreId);
            if (preId == 0 || !IsReceived(player, preId))
                continue;

            Complete(player, id, sync);
            advanced++;
        }

        return advanced;
    }

    /// <summary>Marks a mission accepted and fills its progress bar in one go.</summary>
    private static void Complete(PlayerInstance player, uint id, NtfSyncPlayer sync)
    {
        if (!IsAccepted(player, id))
            MarkAccepted(player, id, sync);

        var target = CheckValue(id);
        if (GetProgress(player, id) < target)
            MarkProgress(player, id, target, sync);
    }

    /// <summary>Tolerant signed read: the Param column carries -1 as well as positive stages.</summary>
    private static int ReadParamAt(JToken? param, int index)
    {
        if (param is not JArray array || index >= array.Count)
            return 0;

        return int.TryParse(array[index]?.ToString().Trim().Trim('[', ']', '"'), out var value) ? value : 0;
    }

    /// <summary>True when the first column of an objective Param array equals <paramref name="value"/>.</summary>
    private static bool ParamsContain(JToken? param, uint value)
    {
        if (param is not JArray array || array.Count == 0)
            return false;

        return ReadUint(array[0]) == value;
    }

    public static bool IsAccepted(PlayerInstance player, uint id) =>
        (player.Attributes.GetValue(GroupId, id) & 1u) != 0;

    public static bool IsReceived(PlayerInstance player, uint id) =>
        (player.Attributes.GetValue(GroupId, id) & 2u) != 0;

    public static void MarkAccepted(PlayerInstance player, uint id, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, id);
        attr.Val |= 1u;
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Sets the progress field to <paramref name="progress"/>, leaving the flag bits alone.</summary>
    public static void MarkProgress(PlayerInstance player, uint id, uint progress, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, id);
        attr.Val = (attr.Val & ~(ProgressMask << ProgressShift)) | ((progress & ProgressMask) << ProgressShift);
        player.Attributes.SyncTo(sync, attr);
    }

    public static void MarkReceived(PlayerInstance player, uint id, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, id);
        attr.Val |= 2u;
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>
    /// Reads a reward column that is either a single flat row or a list of rows, e.g. Reward1 =
    /// [[4,1,6,18,2000]] or [4,1,6,18,2000]. Rows shorter than five columns are ignored.
    /// </summary>
    public static List<IReadOnlyList<uint>> ReadRewardRows(JToken? node)
    {
        var result = new List<IReadOnlyList<uint>>();

        if (node is not JArray array || array.Count == 0)
            return result;

        if (array[0] is JArray)
        {
            foreach (var entry in array)
            {
                if (entry is JArray row && TryReadRow(row, out var values))
                    result.Add(values);
            }

            return result;
        }

        if (TryReadRow(array, out var single))
            result.Add(single);

        return result;
    }

    private static bool TryReadRow(JArray row, out uint[] values)
    {
        values = new uint[row.Count];

        for (var i = 0; i < row.Count; i++)
        {
            if (!uint.TryParse(row[i]?.ToString(), out var value))
                return false;

            values[i] = value;
        }

        return values.Length >= 5 && values[4] != 0;
    }

    public static uint ReadUint(JToken? node, uint fallback = 0)
    {
        var raw = node?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : fallback;
    }

    public static void Save(PlayerInstance player)
    {
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
    }
}
