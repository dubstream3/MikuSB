using Newtonsoft.Json;

namespace MikuSB.Data.Config;

public class StringToUIntConverter : JsonConverter<uint>
{
    public override uint ReadJson(JsonReader reader, Type objectType, uint existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.Value == null)
            return 0;

        var value = reader.Value.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return 0;

        return uint.TryParse(value, out var result) ? result : 0;
    }

    public override void WriteJson(JsonWriter writer, uint value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }
}