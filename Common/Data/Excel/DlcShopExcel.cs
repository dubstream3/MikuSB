using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// DLC shop tabs (dlc/dlc_shop.json). Only the shop id is needed server side: the client owns the
/// goods and asks the server which tabs are currently open (ShopLogic_GetOpenTime), because every
/// shipped tab window has long expired.
/// </summary>
[ResourceEntity("dlc/dlc_shop.json")]
public class DlcShopExcel : ExcelResource
{
    [JsonProperty("ShopId")] public uint ShopId { get; set; }
    [JsonProperty("JumpUI")] public JToken? JumpUI { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }

    public override uint GetId() => ShopId;

    public override void Loaded()
    {
        GameData.DlcShopData[ShopId] = this;
    }
}
