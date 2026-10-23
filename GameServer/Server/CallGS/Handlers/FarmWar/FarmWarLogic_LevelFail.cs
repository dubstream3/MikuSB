using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// FarmWarActivity.Req_LevelFail (FarmWarActivity.lua:879-903). The packet carries the same fight
// counters as the clear packet but there is NO s2c.Register for "FarmWarLogic_LevelFail" anywhere in
// the client, and Misc/S2C.lua errors on unregistered callbacks, so this api must stay silent. The
// server only records the attempt for the logs.
[CallGSApi("FarmWarLogic_LevelFail")]
public class FarmWarLogic_LevelFail : CallGSHandler<FarmWarLevelFailParam>
{
    private static readonly Logger Log = new(nameof(FarmWarLogic_LevelFail));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarLevelFailParam request)
    {
        var player = context.Connection.Player!;

        Log.Info($"FarmWar fail stage={request.Id} kills={request.Kill} plants={request.Plant} " +
                 $"upgrades={request.Upgrade} buffs={request.Buff} for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.NoResponse());
    }
}

public sealed class FarmWarLevelFailParam
{
    [JsonPropertyName("nID")] public uint Id { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
    [JsonPropertyName("F")] public JsonNode? Buffs { get; set; }
    [JsonPropertyName("K")] public uint Kill { get; set; }
    [JsonPropertyName("P")] public uint Plant { get; set; }
    [JsonPropertyName("U")] public uint Upgrade { get; set; }
    [JsonPropertyName("B")] public uint Buff { get; set; }
}
