using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/templates/card_skin.json")]
public class CardSkinExcel : ExcelResource
{
    public uint Genre { get; set; }
    public uint Detail { get; set; }
    public uint Particular { get; set; }
    public uint Level { get; set; }
    public uint Icon { get; set; }
    public int AppearID { get; set; }
    [JsonProperty("profile")] public List<List<int>> Profile { get; set; } = [];
    public override uint GetId()
    {
        return Icon;
    }

    public override void Loaded()
    {
        GameData.CardSkinData.Add(Icon, this);
    }
}