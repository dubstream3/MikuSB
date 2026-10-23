using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("riki/Riki.json")]
public class RikiExcel : ExcelResource
{
    public uint Type { get; set; }
    public uint Id { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    public IReadOnlyList<uint> ItemCondition { get; private set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        // Monster rows: Condition lists the monster template ids the entry covers and has no fixed
        // width (most rows carry two), so they are collected into the reverse map the settlement
        // channel uses instead of the item-condition list below.
        if (Type == 4)
        {
            if (Condition is not JArray monsters)
                return;

            foreach (var monster in monsters)
            {
                if (monster.Type != JTokenType.Integer)
                    continue;

                var monsterId = monster.Value<uint>();
                if (monsterId != 0 && !GameData.RikiMonsterData.ContainsKey(monsterId))
                    GameData.RikiMonsterData[monsterId] = Id;
            }

            return;
        }

        if (Type is not (1 or 2 or 5) || Condition is not JArray condition ||
            condition.Count < 4 || condition.Any(x => x.Type != JTokenType.Integer))
            return;

        ItemCondition = condition.Select(x => x.Value<uint>()).ToArray();
        GameData.RikiData[Id] = this;
    }
}
