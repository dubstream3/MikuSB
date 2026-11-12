using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

/// <summary>
/// Random mission pool (dlc/vircapture/build/questpool.json).
/// <c>Level1..Level10</c> hold the draw weight of the mission at island level 1..10.
/// </summary>
[ResourceEntity("dlc/vircapture/build/questpool.json")]
public class VirCaptureQuestPoolExcel : ExcelResource
{
    [JsonProperty("RandomQuest")] public uint MissionId { get; set; }
    [JsonProperty("PoolID")] public uint PoolId { get; set; }
    [JsonProperty("MapID")] public uint MapId { get; set; }
    [JsonProperty("Level1")] public uint Level1 { get; set; }
    [JsonProperty("Level2")] public uint Level2 { get; set; }
    [JsonProperty("Level3")] public uint Level3 { get; set; }
    [JsonProperty("Level4")] public uint Level4 { get; set; }
    [JsonProperty("Level5")] public uint Level5 { get; set; }
    [JsonProperty("Level6")] public uint Level6 { get; set; }
    [JsonProperty("Level7")] public uint Level7 { get; set; }
    [JsonProperty("Level8")] public uint Level8 { get; set; }
    [JsonProperty("Level9")] public uint Level9 { get; set; }
    [JsonProperty("Level10")] public uint Level10 { get; set; }

    public override uint GetId() => MissionId;

    public uint GetWeight(uint mapLevel) => mapLevel switch
    {
        1 => Level1,
        2 => Level2,
        3 => Level3,
        4 => Level4,
        5 => Level5,
        6 => Level6,
        7 => Level7,
        8 => Level8,
        9 => Level9,
        10 => Level10,
        _ => 0
    };

    public override void Loaded()
    {
        GameData.VirCaptureQuestPoolData.Add(this);
    }
}
