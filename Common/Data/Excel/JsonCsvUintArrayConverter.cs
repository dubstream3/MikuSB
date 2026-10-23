using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Reads a config column that is written as a brace list in the client CSV ("{3,20000}") and ends
/// up as a JSON string after the conversion script, while still accepting a real JSON array so the
/// same model can be fed from either shape. An empty cell, a null and "{}" all resolve to null.
/// </summary>
public sealed class JsonCsvUintArrayConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(uint[]);

    public override object? ReadJson(
        JsonReader reader,
        Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        if (reader.TokenType == JsonToken.StartArray)
            return JArray.Load(reader).ToObject<uint[]>();

        var text = reader.Value?.ToString();
        return ParseCsvList(text);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
        writer.WriteValue(value);

    /// <summary>
    /// Parses "{3,20000}" / "[202109010000]" / "3,20000" into the numbers it contains. Anything that
    /// does not parse contributes nothing, so a malformed cell degrades to an empty (never crashing)
    /// row rather than breaking the whole resource file.
    /// </summary>
    public static uint[]? ParseCsvList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim().Trim('[', ']', '{', '}');
        if (trimmed.Length == 0)
            return null;

        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<uint>(parts.Length);
        foreach (var part in parts)
        {
            if (uint.TryParse(part, out var value))
                values.Add(value);
        }

        return values.Count == 0 ? null : values.ToArray();
    }
}
