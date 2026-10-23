using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Lineup;

[CallGSApi("Lineup_Update")]
public class Lineup_Update : CallGSHandler<LineupUpdateParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, LineupUpdateParam req)
    {

        if (req == null)
        {
            return CallGSResult.Ok("{}", "UpdateLineup");
        }

        var formation = await context.Connection.Player!.LineupManager.UpdateLineup(req.Index,req.Member1,req.Member2,req.Member3);
        if (formation == null)
        {
            return CallGSResult.Ok("{}", "UpdateLineup");
        }
        return CallGSResult.Ok("{}", "UpdateLineup");
    }
}

public sealed class LineupUpdateParam
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("member1")] public uint Member1 { get; set; }
    [JsonPropertyName("member2")] public uint Member2 { get; set; }
    [JsonPropertyName("member3")] public uint Member3 { get; set; }
}
