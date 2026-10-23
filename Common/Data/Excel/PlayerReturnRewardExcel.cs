using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/player_return/return_reward.json — the 回归活动 reward table, one row per ActId, read
/// by the client as activity/player_return/return_reward.txt (Activity/PlayerReturn.lua:37).
///
/// FirstReward is the one-shot "trigger" reward handed out by PlayerReturn.GetFirstReward
/// (nType 1), already in the nested [[G,D,P,L,Count], ...] shape DlcRuntimeOverrides.ReadRewardRows
/// expects. Plot is the story id the first tab plays (990004 for act 560) and Intro the help text.
///
/// AchieveId is the stage list: eight arrays, stage 1 holding the eight aggregate quest ids
/// (4001..4008) and stages 2..8 the leaf quest ids of that stage (4009..4013, 4014..4018, ...).
/// Every id in there is an achievement/activity_quest row the client evaluates against attribute
/// group 7 (Achievement.GroupID_Quest), so the server may only pay rows listed here — that is the
/// allowlist behind PlayerReturn_GetReward nType 3.
/// </summary>
[ResourceEntity("activity/player_return/return_reward.json")]
public class PlayerReturnRewardExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("LevelBuff")] public JToken? LevelBuff { get; set; }
    [JsonProperty("FirstReward")] public JToken? FirstReward { get; set; }
    [JsonProperty("Intro")] public JToken? Intro { get; set; }
    [JsonProperty("Plot")] public JToken? PlotRaw { get; set; }
    [JsonProperty("AchieveId")] public JToken? AchieveId { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);
    [JsonIgnore] public uint Plot => ReadUint(PlotRaw);

    public override uint GetId() => ActId;

    public override void Loaded()
    {
        if (ActId != 0)
            GameData.PlayerReturnRewardData[ActId] = this;
    }

    /// <summary>Number of stages (rows of AchieveId); 8 for act 560.</summary>
    public int StageCount => AchieveId is JArray array ? array.Count : 0;

    /// <summary>Quest ids of one stage, <paramref name="stage"/> being 1-based as the client uses it.</summary>
    public List<uint> StageQuestIds(int stage)
    {
        var ids = new List<uint>();
        if (AchieveId is not JArray stages || stage < 1 || stage > stages.Count)
            return ids;

        if (stages[stage - 1] is not JArray rows)
            return ids;

        foreach (var row in rows)
        {
            var raw = row?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
            if (uint.TryParse(raw, out var id) && id != 0)
                ids.Add(id);
        }

        return ids;
    }

    /// <summary>True when <paramref name="questId"/> sits in any stage, i.e. the act owns the row.</summary>
    public bool OwnsQuest(uint questId)
    {
        if (questId == 0)
            return false;

        for (var stage = 1; stage <= StageCount; stage++)
        {
            if (StageQuestIds(stage).Contains(questId))
                return true;
        }

        return false;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
