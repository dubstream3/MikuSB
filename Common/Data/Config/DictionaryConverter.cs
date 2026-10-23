using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace MikuSB.Data.Config;

class IntDictionaryConverter : JsonConverter<Dictionary<int, int>>
{
    public override Dictionary<int, int>? ReadJson(JsonReader reader, Type objectType, Dictionary<int, int>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.StartArray)
        {
            JArray.Load(reader);
            return new Dictionary<int, int>();
        }
        else if (reader.TokenType == JsonToken.StartObject)
        {
            var obj = JObject.Load(reader);
            var dict = new Dictionary<int, int>();

            foreach (var prop in obj.Properties())
            {
                if (int.TryParse(prop.Name, out var key))
                {
                    dict[key] = prop.Value.ToObject<int>();
                }
            }
            return dict;
        }

        return new Dictionary<int, int>();
    }

    public override void WriteJson(JsonWriter writer, Dictionary<int, int>? value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        foreach (var kv in value!)
        {
            writer.WritePropertyName(kv.Key.ToString());
            writer.WriteValue(kv.Value);
        }
        writer.WriteEndObject();
    }
}
