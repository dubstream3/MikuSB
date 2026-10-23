using MikuSB.Database;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Lineup;

[CallGSApi("Lineups_Update")]
public class Lineups_Update : CallGSHandler<List<LineupUpdateBatchParam>>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, List<LineupUpdateBatchParam> req)
    {

        if (req == null)
        {
            return CallGSResult.Ok("{}", "UpdateLineup");
        }

        foreach (var lineup in req)
        {
            if (lineup == null)
                continue;

            await context.Connection.Player!.LineupManager.UpdateLineup(
                lineup.Index,
                lineup.Member1,
                lineup.Member2,
                lineup.Member3);
        }

        DatabaseHelper.SaveDatabaseType(context.Connection.Player!.LineupManager.LineupData);
        return CallGSResult.Ok("{}", "UpdateLineup");
    }
}

public sealed class LineupUpdateBatchParam
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("member1")] public uint Member1 { get; set; }
    [JsonPropertyName("member2")] public uint Member2 { get; set; }
    [JsonPropertyName("member3")] public uint Member3 { get; set; }
}
