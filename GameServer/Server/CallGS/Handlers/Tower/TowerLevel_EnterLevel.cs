using MikuSB.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerLevel_EnterLevel")]
public class TowerLevel_EnterLevel : CallGSHandler<TowerLevelEnterLevelParam>
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, TowerLevelEnterLevelParam req)
    {

        if (req == null || req.LevelId == 0 || req.TeamId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (!GameData.TowerLevelData.ContainsKey((uint)req.LevelId))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var rsp = $"{{\"nSeed\":{Random.Next(1, 1_000_000_000)}}}";
        return Task.FromResult(CallGSResult.Ok(rsp));
    }
}

public sealed class TowerLevelEnterLevelParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public int TeamId { get; set; }
}
