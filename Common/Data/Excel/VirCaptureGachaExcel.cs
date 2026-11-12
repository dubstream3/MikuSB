using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/gachapool.json - the monetised capture gacha banner.
/// Condition gates the banner by the same condition vocabulary as capture regions (20 = capture
/// level), Cost is a [genre, detail, particular, level, count] row charged per pull (the ten pull
/// charges ten times, umg_vircapture_gacha.lua:120-130) and PoolFile names the pool/xxx.json table
/// with the candidates. UnlockWeight / LockWeight split the roll between monsters the player
/// already owns a Riki entry for and ones they do not.
/// </summary>
[ResourceEntity("dlc/vircapture/gachapool.json")]
public class VirCaptureGachaExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("StartTime")] public string StartTime { get; set; } = string.Empty;
    [JsonProperty("EndTime")] public string EndTime { get; set; } = string.Empty;
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("PoolFile")] public string PoolFile { get; set; } = string.Empty;
    [JsonProperty("UnlockWeight")] public uint UnlockWeight { get; set; }
    [JsonProperty("LockWeight")] public uint LockWeight { get; set; }
    [JsonProperty("Cost")] public List<uint> Cost { get; set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureGachaData[Id] = this;
    }
}

/// <summary>One row of pool/pool01.json: a card template and its roll weight.</summary>
[ResourceEntity("dlc/vircapture/pool/pool01.json")]
public class VirCaptureGachaPoolExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("GDPL")] public List<uint> Gdpl { get; set; } = [];
    [JsonProperty("Weight")] public uint Weight { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureGachaPoolData.Add(this);
    }
}

/// <summary>
/// dlc/vircapture/rikiawardlist.json - the capture-book milestones.
/// Point is the number of distinct monsters that must be unlocked; the claim flag lives in gid 128
/// sid 121 + Id/30, bit Id%30 (VirCapture.lua:289-297).
/// </summary>
[ResourceEntity("dlc/vircapture/rikiawardlist.json")]
public class VirCaptureRikiAwardExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("Point")] public uint Point { get; set; }
    [JsonProperty("Rewards")] public JToken? Rewards { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureRikiAwardData[Id] = this;
    }
}
