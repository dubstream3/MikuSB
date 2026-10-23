using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// 世界BOSS period rows (challenge/worldboss/worldbosslist.json). The table is keyed by the pair
// (Id, ActID) and ships a single Id (the activity group the DlcFuncWorldboss button points at, see
// DLC/DLCFunc/DlcFuncWorldboss.lua:11) with one row per act. The client looks the row up as
// tbBossListCfg[Id][ActID] (Challenge/WorldBoss/WorldBoss.lua:252-258) after reading the served pair
// from attributes (179,0)/(179,1), so the server has to publish exactly that pair.
//
// Times are compact "YYYYMMDDHHmm" strings wrapped in brackets ("[202512251000]"). Keep them as raw
// strings and parse via DlcRuntimeOverrides.ParseConfigTime - converting them to numbers overflows
// an int (about 2e11) and JToken.Value<int>() would throw.
[ResourceEntity("challenge/worldboss/worldbosslist.json")]
public class WorldBossActExcel : ExcelResource
{
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("ActID")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("StartTime")] public string? StartTime { get; set; }
    [JsonProperty("EndTime")] public string? EndTime { get; set; }
    [JsonProperty("AwardTime")] public string? AwardTime { get; set; }
    [JsonProperty("LevelList")] public JToken? LevelListRaw { get; set; }
    [JsonProperty("ReferRank1")] public JToken? ReferRank1Raw { get; set; }
    [JsonProperty("ReferRank2")] public JToken? ReferRank2Raw { get; set; }
    [JsonProperty("TaskID")] public JToken? TaskIdsRaw { get; set; }
    [JsonProperty("TaskID2")] public JToken? TaskIds2Raw { get; set; }

    [JsonIgnore] public uint ActivityId => ReadUInt(IdRaw);
    [JsonIgnore] public uint ActId => ReadUInt(ActIdRaw);
    [JsonIgnore] public uint ReferRank1 => ReadUInt(ReferRank1Raw);
    [JsonIgnore] public uint ReferRank2 => ReadUInt(ReferRank2Raw);
    [JsonIgnore] public List<uint> LevelList => ReadUIntList(LevelListRaw);
    [JsonIgnore] public List<uint> TaskIds => ReadUIntList(TaskIdsRaw);
    [JsonIgnore] public List<uint> TaskIds2 => ReadUIntList(TaskIds2Raw);

    /// <summary>All quest ids shown by this act's reward panel, in table order.</summary>
    [JsonIgnore]
    public List<uint> AllTaskIds
    {
        get
        {
            var ids = new List<uint>();
            foreach (var id in TaskIds.Concat(TaskIds2))
                if (id != 0 && !ids.Contains(id))
                    ids.Add(id);
            return ids;
        }
    }

    public override uint GetId() => ActivityId;

    public override void Loaded()
    {
        GameData.WorldBossActData.RemoveAll(row => row.ActivityId == ActivityId && row.ActId == ActId);
        GameData.WorldBossActData.Add(this);
    }

    private static uint ReadUInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)token.Value<decimal>(),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }

    private static List<uint> ReadUIntList(JToken? token)
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
}

// 世界BOSS selectable stage ids (challenge/worldboss/worldbossleveldes.json): the button publishes
// the stage id (2501 / 2511) but the battle table is keyed by LevelID (3401 / 3402), so an enter /
// settlement request carries the former and the HP pool has to be resolved through this row.
[ResourceEntity("challenge/worldboss/worldbossleveldes.json")]
public class WorldBossLevelDesExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("LevelID")] public JToken? LevelIdRaw { get; set; }

    [JsonIgnore] public uint DesId => ReadUInt(IdRaw);
    [JsonIgnore] public uint LevelId => ReadUInt(LevelIdRaw);

    public override uint GetId() => DesId;

    public override void Loaded()
    {
        GameData.WorldBossLevelDesData[DesId] = this;
    }

    private static uint ReadUInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)token.Value<decimal>(),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}

// 世界BOSS battle rows (challenge/worldboss/worldbosslevel.json). BossMaxHp drives the percent based
// quest line (Type 380 checks 25/50/75/100 percent of this value).
[ResourceEntity("challenge/worldboss/worldbosslevel.json")]
public class WorldBossLevelExcel : ExcelResource
{
    [JsonProperty("LevelID")] public JToken? LevelIdRaw { get; set; }
    [JsonProperty("BossMaxHp")] public JToken? BossMaxHpRaw { get; set; }
    [JsonProperty("BossMaxDamage")] public JToken? BossMaxDamageRaw { get; set; }

    [JsonIgnore] public uint LevelId => ReadUInt(LevelIdRaw);
    [JsonIgnore] public uint BossMaxHp => ReadUInt(BossMaxHpRaw);
    [JsonIgnore] public uint BossMaxDamage => ReadUInt(BossMaxDamageRaw);

    public override uint GetId() => LevelId;

    public override void Loaded()
    {
        GameData.WorldBossLevelData[LevelId] = this;
    }

    private static uint ReadUInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)token.Value<decimal>(),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}
