using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

/// <summary>
/// Tolerant readers for the JSON bodies the CN client sends to CallGS APIs. json.encode on the Lua
/// side can deliver one number as a number, as a string ("3") or as a one element array ("[3]"), so
/// every read trims those shapes instead of trusting the schema.
/// </summary>
internal static class CallGSParam
{
    /// <summary>Request body as an object; a missing or broken body reads as null.</summary>
    public static JsonObject? Parse(string? param)
    {
        if (string.IsNullOrWhiteSpace(param))
            return null;

        try
        {
            return JsonNode.Parse(param) as JsonObject;
        }
        catch
        {
            // A Lua empty table arrives as "[]" (json.encode({})): treat it as "no parameters".
            return null;
        }
    }

    public static uint ReadUint(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0;

        try
        {
            if (value.TryGetValue<uint>(out var number))
                return number;

            if (value.TryGetValue<string>(out var text) &&
                uint.TryParse(text.Trim().Trim('[', ']', '"'), out var parsed))
                return parsed;
        }
        catch
        {
            // fall through: an unreadable entry counts as 0
        }

        return 0;
    }

    public static bool ReadBool(JsonNode? node)
    {
        if (node is not JsonValue value)
            return false;

        try
        {
            if (value.TryGetValue<bool>(out var flag))
                return flag;

            if (value.TryGetValue<uint>(out var number))
                return number != 0;

            if (value.TryGetValue<string>(out var text))
            {
                var trimmed = text.Trim().Trim('[', ']', '"');
                return trimmed == "1" || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // fall through: an unreadable entry counts as false
        }

        return false;
    }

    /// <summary>
    /// Failure answer for the APIs whose client callback reads tbParam.sError (the assault ops
    /// callbacks do), while CallGSResult.Error writes the sErr spelling.
    /// </summary>
    public static CallGSResult Failure(string key)
        => CallGSResult.Ok(new JsonObject { ["sError"] = key });
}
