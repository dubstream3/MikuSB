using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/recycle.json")]
public class RecycleExcel : ExcelResource
{
    public int ID { get; set; }
    public JToken? RecycleReward { get; set; }
    public JToken? RecycleBase { get; set; }
    public JToken? RecycleRatio { get; set; }

    public override uint GetId()
    {
        return (uint)ID;
    }

    public override void Loaded()
    {
        GameData.RecycleData[ID] = this;
    }
}
