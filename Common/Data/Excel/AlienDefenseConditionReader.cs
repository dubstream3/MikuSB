using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Reads the loose array columns the alien defense (Mow) roster tables use. Both rosters store their
/// requirement as a JSON array of numbers, but an entry without a requirement stores an empty string
/// (aliendefense_pal_main.json pal 1 and 3), so callers have to expect a non-array token and read it
/// as "no requirement" instead of throwing.
/// </summary>
internal static class AlienDefenseConditionReader
{
    public static IReadOnlyList<uint> ReadList(JToken? token)
    {
        var values = new List<uint>();
        if (token is not JArray array)
            return values;

        foreach (var entry in array)
        {
            var raw = entry?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
            if (uint.TryParse(raw, out var value))
                values.Add(value);
        }

        return values;
    }
}
