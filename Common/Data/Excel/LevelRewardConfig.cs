using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

public interface ILevelRewardConfig
{
    uint ID { get; }
    IDictionary<string, JToken> ExtraData { get; }
}

public static class LevelRewardConfigExtensions
{
    public static uint LevelType(this ILevelRewardConfig config) =>
        ReadUInt(config, "Type");

    public static bool IsPlot(this ILevelRewardConfig config) =>
        config.LevelType() is 2 or 7;

    public static uint NextId(this ILevelRewardConfig config) =>
        ReadUInt(config, "NextID");

    public static uint PlayerExp(this ILevelRewardConfig config) =>
        ReadUInt(config, "PlayerExp");

    public static uint RoleExp(this ILevelRewardConfig config) =>
        ReadUInt(config, "RoleExp");

    public static IReadOnlyList<uint> FirstDropIds(this ILevelRewardConfig config) =>
        ReadUIntList(config, "FirstDropID");

    public static IReadOnlyList<uint> BaseDropIds(this ILevelRewardConfig config) =>
        ReadUIntList(config, "BaseDropID");

    public static IReadOnlyList<uint> RandomDropIds(this ILevelRewardConfig config) =>
        ReadUIntList(config, "RandomDropID");

    public static IReadOnlyList<IReadOnlyList<uint>> StarAward(this ILevelRewardConfig config) =>
        ReadRewardRows(config, "StarAward");

    public static IReadOnlyList<IReadOnlyList<uint>> ShowAward(this ILevelRewardConfig config) =>
        ReadRewardRows(config, "ShowAward");

    public static IReadOnlyList<IReadOnlyList<uint>> ShowRandomAward(this ILevelRewardConfig config) =>
        ReadRewardRows(config, "ShowRandomAward");

    public static IReadOnlyList<IReadOnlyList<uint>> ShowFirstAward(this ILevelRewardConfig config) =>
        ReadRewardRows(config, "ShowFirstAward");

    private static uint ReadUInt(ILevelRewardConfig config, string key)
    {
        if (!config.ExtraData.TryGetValue(key, out var token))
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }

    private static IReadOnlyList<uint> ReadUIntList(ILevelRewardConfig config, string key)
    {
        if (!config.ExtraData.TryGetValue(key, out var token) || token is not JArray array)
            return [];

        return array
            .Select(ReadUInt)
            .Where(x => x > 0)
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<uint>> ReadRewardRows(ILevelRewardConfig config, string key)
    {
        if (!config.ExtraData.TryGetValue(key, out var token) || token is not JArray array)
            return [];

        return array
            .OfType<JArray>()
            .Select(row => (IReadOnlyList<uint>)row.Select(ReadUInt).ToArray())
            .Where(row => row.Count >= 5)
            .ToArray();
    }

    private static uint ReadUInt(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}
