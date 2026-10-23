using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// Ticket exchange price table. The client loads the same rows as item/exchange.txt
// (Script/Item/Item.lua:1397-1411 Item.LoadExchangeConf) into Item.tbExchange, keyed
// "G-D-P-L" with value { tbCash = {cashId, ratio}, tbItem = ... }, and umg_gacha_exchange.lua:62-71
// turns the ratio into the money the top-up window asks for.
//
// The server needs its own copy because the Item_Exchange payload only carries how many tickets the
// player is short of (umg_gacha_exchange.lua:85) -- never a price. Cash = [2, 160] means one ticket
// of that GDPL costs 160 of currency id 2 (数据金 / AttrIds.Currency.Gold), so a tampered client can
// at most buy more tickets, never a cheaper rate.
[ResourceEntity("item/exchange.json")]
public class ItemExchangeExcel : ExcelResource
{
    [JsonProperty("G")] public uint Genre { get; set; }
    [JsonProperty("D")] public uint Detail { get; set; }
    [JsonProperty("P")] public uint Particular { get; set; }
    [JsonProperty("L")] public uint Level { get; set; }
    [JsonProperty("Cash")] public JToken? CashRaw { get; set; }

    /// <summary>Template id of the ticket this row prices, the dictionary key.</summary>
    [JsonIgnore] public ulong TemplateId => GameResourceTemplateId.FromGdpl(Genre, Detail, Particular, Level);

    /// <summary>Cash[0]: the moneyType one ticket is paid with. 0 means the row is not purchasable.</summary>
    [JsonIgnore] public uint CashId => ReadCash(0);

    /// <summary>Cash[1]: how much of <see cref="CashId"/> a single ticket costs.</summary>
    [JsonIgnore] public uint CashRatio => ReadCash(1);

    public override uint GetId() => (uint)TemplateId;

    public override void Loaded()
    {
        GameData.ItemExchangeData[TemplateId] = this;
    }

    // The columns of these converted tables are strings as often as numbers (see OtherItemExcel), so
    // read the two Cash entries tolerantly instead of binding them to a uint[].
    private uint ReadCash(int index)
    {
        if (CashRaw is not JArray cash || cash.Count <= index)
            return 0;

        return cash[index].Type switch
        {
            JTokenType.Integer => cash[index].Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, cash[index].Value<decimal>()),
            JTokenType.String when uint.TryParse(cash[index].Value<string>(), out var value) => value,
            _ => 0
        };
    }
}
