using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.GameServer.Game.Player;
using System.Text.Json;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_UpdateData")]
public class DreamCard_UpdateData : CallGSHandler
{
    private const uint DataGroupId = AttrIds.DreamCard.DataGid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var dirty = false;

        try
        {
            var entries = JsonSerializer.Deserialize<List<DreamCardUpdateDataEntry>>(param) ?? [];
            foreach (var entry in entries)
            {
                if (entry.Id <= 0)
                    continue;

                var value = NormalizeJson(entry.Data);
                var attr = player.Attributes.SetString(DataGroupId, (uint)entry.Id, value);
                player.Attributes.SyncTo(sync, attr);
                dirty = true;
            }
        }
        catch
        {
            // Ignore malformed payloads so the client-side save queue can continue.
        }

        if (dirty)
            DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

    private static string NormalizeJson(JsonElement data)
    {
        return data.ValueKind == JsonValueKind.Undefined
            ? "null"
            : data.GetRawText();
    }
}

internal sealed class DreamCardUpdateDataEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}
