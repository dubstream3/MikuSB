using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// house/coinpusher/coinpusher_achv.json — the coin pusher (推币机) achievement list the arcade task
/// panel iterates (`HouseArcadeConfig.lua:113` LoadTargetConfig('coinpusher_achv'),
/// `UMG/House/HouseArcade/umg_arcade_task.lua:41-56`). Each row names an
/// achievement/activity_quest.json row through ID and carries the trigger type that row uses.
///
/// The panel therefore shows quest-group (AttrIds.Achievement.QuestGroupId = 7) rows, not the standard
/// achievement group; claiming goes through the House_Request sub-route ArcadeGameGetAchReward
/// (`HouseArcadeToSeverMsg.lua:278-285` GetAchReward(nId) -> {nAchId}).
/// </summary>
[ResourceEntity("house/coinpusher/coinpusher_achv.json")]
public class CoinPusherAchvExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("AchType")] public JToken? AchTypeRaw { get; set; }

    /// <summary>Presentation type used by the panel (1 = always listed, otherwise shown once finished).</summary>
    [JsonIgnore] public uint Type => ReadUint(TypeRaw);

    /// <summary>Trigger type of the referenced activity_quest row (388 = normal-mode rounds with a girl).</summary>
    [JsonIgnore] public uint AchType => ReadUint(AchTypeRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.CoinPusherAchvData[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
