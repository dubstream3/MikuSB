using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.SdkServer.Utils;

public class JsonStringToObjectConverter<T> : JsonConverter<T> where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            return JsonSerializer.Deserialize<T>(ref reader, options);

        var jsonString = reader.GetString();
        return !string.IsNullOrEmpty(jsonString)
            ? JsonSerializer.Deserialize<T>(jsonString, options)
            : null;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(value, options);
        writer.WriteStringValue(json);
    }
}
