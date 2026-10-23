using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// house/coinpusher/coinpusher_rewards.json — the coin pusher (推币大赢家, "ArcadeGame") level
/// reward ladder: one row per level, 15 levels in the shipped table.
///
/// The client loads the same file through HouseArcadeConfig.tbRewardsConfig
/// (House/HouseMiniGame/HouseArcade/HouseArcadeConfig.lua:180-182, columns Level -> nLevel,
/// Exp -> nExp, Reward -> tbReward) and uses it for two things:
///   * HouseArcadeToSeverMsg:GetLevel (HouseArcadeToSeverMsg.lua:174-197) walks the ladder
///     accumulating Exp to turn the raw experience attribute into a level + leftover exp;
///   * CheckCanGetLevelReward (:234-248) shows the red dot while a reached level has not been
///     claimed, using the bitfield at arcade offset 3 (Attr_LevelRewardGot).
///
/// Reward is a FLAT [G,D,P,L,Count] array here, not the nested [[G,D,P,L,Count]] shape the DLC
/// tables use, so DlcRuntimeOverrides.ReadRewardRows (which only walks an array of arrays) cannot
/// be used on it — this class parses the flat row once at load time instead.
/// </summary>
[ResourceEntity("house/coinpusher/coinpusher_rewards.json")]
public class CoinPusherRewardExcel : ExcelResource
{
    [JsonProperty("Level")] public JToken? LevelRaw { get; set; }
    [JsonProperty("Exp")] public JToken? ExpRaw { get; set; }
    [JsonProperty("Reward")] public JToken? Reward { get; set; }

    [JsonIgnore] public uint Level => ReadUint(LevelRaw);
    [JsonIgnore] public uint Exp => ReadUint(ExpRaw);

    /// <summary>Flat [G,D,P,L,Count] row, or null when the table row is malformed.</summary>
    [JsonIgnore] public uint[]? RewardRow { get; private set; }

    public override uint GetId() => Level;

    public override void Loaded()
    {
        if (Reward is JArray array && array.Count >= 5)
        {
            var row = new uint[5];
            for (var i = 0; i < 5; i++)
                row[i] = ReadUint(array[i]);

            // GrantConfiguredRewardsAsync drops rows with a zero count, so keep the shape aligned
            // with the tables that do go through it.
            if (row[0] != 0 && row[4] != 0)
                RewardRow = row;
        }

        if (Level != 0)
            GameData.CoinPusherRewardData[Level] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
