using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 弑神斗场 (season boss) per-level rows from challenge/seasonboss/seasonleveldes.json.
///
/// The client builds SeasonBossLogic.tbLevelCfg keyed by this file's ID column
/// (Challenge/SeasonBoss/SeasonBoss.lua:109 `local nLevelID = tonumber(tbLine.ID)` and :134
/// `SeasonBossLogic.tbLevelCfg[nLevelID] = tb`), and the season list's LevelList holds exactly
/// those IDs (3401..3405 for ActID 10 zone 1). Every request of the mode therefore carries this
/// ID as nID, which is why it is the lookup key here as well.
///
/// ReferAward is the activity_quest id whose completion row the level feeds
/// (3401 -> 19001, 3411 -> 19016, ...). SeasonBoss_LevelSettlement turns that into progress on
/// attribute (AttrIds.Achievement.QuestGroupId, questId), and SeasonBoss_GetReward pays it out.
/// </summary>
[ResourceEntity("challenge/seasonboss/seasonleveldes.json")]
public class SeasonBossLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("LevelID")] public JToken? LevelIdRaw { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }
    [JsonProperty("ReferAward")] public JToken? ReferAwardRaw { get; set; }
    [JsonProperty("SceneData")] public JToken? SceneDataRaw { get; set; }
    [JsonProperty("PictureBoss")] public JToken? PictureBossRaw { get; set; }
    [JsonProperty("PictureLevel")] public JToken? PictureLevelRaw { get; set; }
    [JsonProperty("LevelTitle")] public JToken? LevelTitle { get; set; }
    [JsonProperty("LevelDes")] public JToken? LevelDes { get; set; }
    [JsonProperty("BossID")] public JToken? BossIdRaw { get; set; }
    [JsonProperty("AreaBuff")] public JToken? AreaBuff { get; set; }
    [JsonProperty("EntriesPool")] public JToken? EntriesPool { get; set; }

    /// <summary>Inner level row (seasonlevel.json) this display row points at.</summary>
    [JsonIgnore] public uint LevelId => ReadUint(LevelIdRaw);

    /// <summary>activity_quest id completed by clearing this level; 0 when the row has none.</summary>
    [JsonIgnore] public uint ReferAward => ReadUint(ReferAwardRaw);

    [JsonIgnore] public uint SceneData => ReadUint(SceneDataRaw);
    [JsonIgnore] public uint BossId => ReadUint(BossIdRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.SeasonBossLevelData[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
