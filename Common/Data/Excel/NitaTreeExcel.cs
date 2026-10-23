using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// 妮塔大富翁 tree house rows (activity/nita_monopoly/tree.json). The table is a plain JSON array
// WITHOUT an id column; the client addresses rows by their ipairs index
// (Activity/NitaMonolopy.lua:363 `for _, tbConfig in ipairs(NitaMonolopy.TreeConfigs)`), and the
// index doubles as the attribute sid offset: TreeRewardGot (10) + index. Row 1 has EX = 0 and no
// rewards, so the first claimable index is 2 (NitaMonolopy.lua:377 `tbConfig.EX > 0`).
//
// GameData.NitaTreeData keeps file order; AfterAllDone sorts by EX so the index stays stable no
// matter how the table is ordered on disk (EX is strictly increasing in the shipped table).
[ResourceEntity("activity/nita_monopoly/tree.json")]
public class NitaTreeExcel : ExcelResource
{
    [JsonProperty("EX")] public JToken? ExRaw { get; set; }
    [JsonProperty("ShelfFlushCount")] public JToken? ShelfFlushCountRaw { get; set; }
    [JsonProperty("BaseMoney")] public JToken? BaseMoneyRaw { get; set; }
    [JsonProperty("Rewards")] public JToken? RewardsRaw { get; set; }

    [JsonIgnore] public uint Ex => ReadUInt(ExRaw);
    [JsonIgnore] public uint ShelfFlushCount => ReadUInt(ShelfFlushCountRaw);
    [JsonIgnore] public uint BaseMoney => ReadUInt(BaseMoneyRaw);

    public override uint GetId() => Ex;

    public override void Loaded()
    {
        GameData.NitaTreeData.Add(this);
    }

    public override void AfterAllDone()
    {
        // Loaded() runs once per row, so every call would sort; the table only has ~19 rows.
        var sorted = GameData.NitaTreeData.OrderBy(row => row.Ex).ToList();
        GameData.NitaTreeData.Clear();
        GameData.NitaTreeData.AddRange(sorted);
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
