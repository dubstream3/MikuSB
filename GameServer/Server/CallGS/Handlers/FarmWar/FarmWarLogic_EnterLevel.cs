using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// FarmWarActivity.Req_EnterLevel (FarmWarActivity.lua:719-747). The client raises the connection
// overlay before sending and the s2c callback only reads sErr / nSeed, which is then echoed back with
// the settlement packet. The seed is the dedup key of the settle channel on our side too.
[CallGSApi("FarmWarLogic_EnterLevel")]
public class FarmWarLogic_EnterLevel : CallGSHandler<FarmWarEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(FarmWarLogic_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarEnterLevelParam request)
    {
        var player = context.Connection.Player!;
        if (!GameData.FarmWarLevelData.ContainsKey(request.Id))
        {
            FarmWarActivityService.LogUnknownStage(Api, request.Id);
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen", "FarmWarLogic_EnterLevel"));
        }

        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        Log.Info($"FarmWar enter stage={request.Id} team={request.TeamId} seed={seed} for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nSeed"] = seed
        }.ToJsonString(), null, "FarmWarLogic_EnterLevel"));
    }
}

public sealed class FarmWarEnterLevelParam
{
    [JsonPropertyName("nID")] public uint Id { get; set; }
    [JsonPropertyName("nTeamID")] public uint TeamId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
