using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// dlc/bag_rogue/timelist.json -- the three 背包肉鸽 periods. All three windows are in the past
// (2024-11 .. 2025-07); BagRogue_CheckOpenAct overrides the in-memory window of the row it names,
// which is the only lever the server has (BagRogueConfig.lua:1212-1215) -- the client compares
// GetTime() against its own row, so a handler that only answers "open" would close the panel again
// one second later (umg_backpack.lua:49-77).
[ResourceEntity("dlc/bag_rogue/timelist.json")]
public class BagRogueScheduleExcel : ExcelResource
{
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("StartTime")] public JToken? StartRaw { get; set; }
    [JsonProperty("EndTime")] public JToken? EndRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdsRaw { get; set; }
    [JsonProperty("Daily")] public JToken? DailyRaw { get; set; }
    [JsonProperty("Weekly")] public JToken? WeeklyRaw { get; set; }
    [JsonProperty("Normal")] public JToken? NormalRaw { get; set; }
    [JsonProperty("TrialID")] public JToken? TrialIdsRaw { get; set; }
    [JsonProperty("InitialCoin")] public JToken? InitialCoinRaw { get; set; }
    [JsonProperty("VolumeTransform")] public JToken? VolumeTransformRaw { get; set; }
    [JsonProperty("Story")] public JToken? StoryRaw { get; set; }
    [JsonProperty("BagVolume")] public JToken? BagVolumeRaw { get; set; }
    [JsonProperty("StockroomVolume")] public JToken? StockroomVolumeRaw { get; set; }
    [JsonProperty("SaveVolume")] public JToken? SaveVolumeRaw { get; set; }
    [JsonProperty("MaxInitialJigsawCount")] public JToken? MaxInitialJigsawCountRaw { get; set; }
    [JsonProperty("RefreshBattleReward")] public JToken? RefreshBattleRewardRaw { get; set; }

    [JsonIgnore] public uint Id => BagRogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public string? Start => StartRaw?.ToString();
    [JsonIgnore] public string? End => EndRaw?.ToString();
    [JsonIgnore] public List<uint> MapIds => BagRogueJson.ReadUIntList(MapIdsRaw);
    [JsonIgnore] public List<uint> TrialIds => BagRogueJson.ReadUIntList(TrialIdsRaw);
    [JsonIgnore] public List<uint> TaskIds =>
        BagRogueJson.ReadUIntList(DailyRaw)
            .Concat(BagRogueJson.ReadUIntList(WeeklyRaw))
            .Concat(BagRogueJson.ReadUIntList(NormalRaw))
            .Distinct()
            .ToList();
    [JsonIgnore] public uint StoryId => BagRogueJson.ReadUInt(StoryRaw);
    [JsonIgnore] public uint BagVolume => BagRogueJson.ReadUInt(BagVolumeRaw);
    [JsonIgnore] public uint StockroomVolume => BagRogueJson.ReadUInt(StockroomVolumeRaw);
    [JsonIgnore] public uint SaveVolume => BagRogueJson.ReadUInt(SaveVolumeRaw);
    [JsonIgnore] public uint MaxInitialJigsawCount => BagRogueJson.ReadUInt(MaxInitialJigsawCountRaw);
    [JsonIgnore] public uint RefreshBattleReward => BagRogueJson.ReadUInt(RefreshBattleRewardRaw);
    [JsonIgnore] public uint CoinType => BagRogueJson.ReadUIntAt(InitialCoinRaw, 0);
    [JsonIgnore] public uint CoinAmount => BagRogueJson.ReadUIntAt(InitialCoinRaw, 1);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.BagRogueScheduleData[Id] = this;
}

// dlc/bag_rogue/mapnode.json -- 298 route nodes over 11 maps. "Id" is the map id (1..4, 211..213,
// 221/222, 231/232) and NodeId is the id the client walks to. The client never derives the node type
// from this table at runtime: it reads T/D/A back out of the 61/1 JSON the server writes
// (BagRogue.lua:386-404), so the row is only used to build that JSON.
[ResourceEntity("dlc/bag_rogue/mapnode.json")]
public class BagRogueNodeExcel : ExcelResource
{
    [JsonProperty("Id")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("NodeId")] public JToken? NodeIdRaw { get; set; }
    [JsonProperty("PositionId")] public JToken? PositionIdRaw { get; set; }
    [JsonProperty("Line")] public JToken? LineRaw { get; set; }
    [JsonProperty("PreNode")] public JToken? PreNodeRaw { get; set; }
    [JsonProperty("EventType")] public JToken? EventTypeRaw { get; set; }
    [JsonProperty("LevelGroup")] public JToken? LevelGroupRaw { get; set; }
    [JsonProperty("AdventureGroupId")] public JToken? AdventureGroupIdRaw { get; set; }
    [JsonProperty("StoreId")] public JToken? StoreIdRaw { get; set; }
    [JsonProperty("StoryBranch")] public JToken? StoryBranchRaw { get; set; }
    [JsonProperty("BattleCoin")] public JToken? BattleCoinRaw { get; set; }
    [JsonProperty("Reward")] public JToken? RewardRaw { get; set; }

    [JsonIgnore] public uint MapId => BagRogueJson.ReadUInt(MapIdRaw);
    [JsonIgnore] public uint NodeId => BagRogueJson.ReadUInt(NodeIdRaw);
    [JsonIgnore] public uint PositionId => BagRogueJson.ReadUInt(PositionIdRaw);
    [JsonIgnore] public uint Line => BagRogueJson.ReadUInt(LineRaw);
    [JsonIgnore] public uint EventType => BagRogueJson.ReadUInt(EventTypeRaw);
    [JsonIgnore] public uint LevelGroup => BagRogueJson.ReadUInt(LevelGroupRaw);
    [JsonIgnore] public uint AdventureGroupId => BagRogueJson.ReadUInt(AdventureGroupIdRaw);
    [JsonIgnore] public uint StoreId => BagRogueJson.ReadUInt(StoreIdRaw);
    [JsonIgnore] public List<uint> PreNodes => BagRogueJson.ReadUIntList(PreNodeRaw);
    [JsonIgnore] public List<List<uint>> Rewards => BagRogueJson.ReadRows(RewardRaw);
    [JsonIgnore] public bool IsBattle => EventType is 1 or 5;
    [JsonIgnore] public bool IsEnding => EventType == 6;

    public override uint GetId() => NodeId;

    public override void Loaded() => GameData.BagRogueNodeData[NodeId] = this;
}

// dlc/bag_rogue/level.json -- 132 stages. The server only needs Id (written into the node JSON as D,
// the client turns it into Map.Open(level.txt[Id].MapID), BagRogueLevel.lua:173) plus GroupId, which
// matches the node's LevelGroup.
[ResourceEntity("dlc/bag_rogue/level.json")]
public class BagRogueLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("GroupId")] public JToken? GroupIdRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }

    [JsonIgnore] public uint Id => BagRogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Type => BagRogueJson.ReadUInt(TypeRaw);
    [JsonIgnore] public uint GroupId => BagRogueJson.ReadUInt(GroupIdRaw);
    [JsonIgnore] public uint MapId => BagRogueJson.ReadUInt(MapIdRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.BagRogueLevelData[Id] = this;
}

// dlc/bag_rogue/story.json -- the ten endings. BossLevel is the stage the client plays for the
// ending node, EndingType is the bit written into (142,15).
[ResourceEntity("dlc/bag_rogue/story.json")]
public class BagRogueStoryExcel : ExcelResource
{
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("EndingType")] public JToken? EndingTypeRaw { get; set; }
    [JsonProperty("BossLevel")] public JToken? BossLevelRaw { get; set; }

    [JsonIgnore] public uint Id => BagRogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint EndingType => BagRogueJson.ReadUInt(EndingTypeRaw);
    [JsonIgnore] public uint BossLevel => BagRogueJson.ReadUInt(BossLevelRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.BagRogueStoryData[Id] = this;
}

// dlc/bag_rogue/adventure.json -- 239 adventure options grouped by GroupId. The node carries only the
// group id; the client is handed the option ids as the node's "D" array and echoes an index back, so the
// server has to know the option list to translate that index.
[ResourceEntity("dlc/bag_rogue/adventure.json")]
public class BagRogueAdventureExcel : ExcelResource
{
    [JsonProperty("GroupId")] public JToken? GroupIdRaw { get; set; }
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("OptionType")] public JToken? OptionTypeRaw { get; set; }
    [JsonProperty("Option")] public JToken? OptionRaw { get; set; }

    [JsonIgnore] public uint GroupId => BagRogueJson.ReadUInt(GroupIdRaw);
    [JsonIgnore] public uint Id => BagRogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint OptionType => BagRogueJson.ReadUInt(OptionTypeRaw);
    [JsonIgnore] public List<uint> Options => BagRogueJson.ReadUIntList(OptionRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.BagRogueAdventureData[Id] = this;
}

internal static class BagRogueJson
{
    // Several single-value columns of dlc/bag_rogue are one-element arrays (AdventureGroupId is even a
    // three-element array repeating the same group), so an array collapses to its first non-zero entry
    // instead of reading as 0 -- a 0 there silently empties the adventure options of the node.
    public static uint ReadUInt(JToken? token)
    {
        switch (token)
        {
            case null:
                return 0;
            case JArray array:
                foreach (var item in array)
                {
                    var value = ReadUInt(item);
                    if (value != 0)
                        return value;
                }

                return 0;
            case JValue value when value.Value is long l:
                return (uint)Math.Max(0, l);
            case JValue value when value.Value is int i:
                return (uint)Math.Max(0, i);
            case JValue value when value.Value is double d:
                return (uint)Math.Max(0, d);
            default:
                return uint.TryParse(token.ToString().Trim(), out var parsed) ? parsed : 0;
        }
    }

    public static uint ReadUIntAt(JToken? token, int index)
    {
        if (token is not JArray array || index >= array.Count)
            return 0;

        return ReadUInt(array[index]);
    }

    public static List<uint> ReadUIntList(JToken? token)
    {
        var list = new List<uint>();
        if (token is not JArray array)
            return list;

        foreach (var item in array)
        {
            var value = ReadUInt(item);
            if (value != 0)
                list.Add(value);
        }

        return list;
    }

    // [[G,D,P,L,N], ...]
    public static List<List<uint>> ReadRows(JToken? token)
    {
        var rows = new List<List<uint>>();
        if (token is not JArray array)
            return rows;

        foreach (var item in array)
        {
            if (item is not JArray row)
                continue;

            var values = new List<uint>();
            foreach (var cell in row)
                values.Add(ReadUInt(cell));

            if (values.Count >= 5 && values[4] > 0)
                rows.Add(values);
        }

        return rows;
    }
}
