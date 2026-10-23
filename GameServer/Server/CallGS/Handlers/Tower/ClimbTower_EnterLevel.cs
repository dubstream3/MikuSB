using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.ClimbTower;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

/// <summary>
/// DLC 3.3 爬塔 entry (DLC/ClimbTower/ClimbTower.lua:589-629): the client validates level/buff/condition
/// itself and only needs a battle seed back - ClimbTowerBattleLogic.OnEnterLevelRsp (:64-76) reads nothing
/// but sErr and nSeed, then Map.Open's the map from its own level config. Without this handler the
/// connection mask over the whole ladder never cleared, because ClimbTower.EnterLevel raises it (:628).
/// </summary>
[CallGSApi("ClimbTower_EnterLevel")]
public class ClimbTower_EnterLevel : CallGSHandler<ClimbTowerEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(ClimbTower_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerEnterLevelParam request)
    {
        if (request.Id == 0 || !GameData.ClimbTowerBattleData.ContainsKey(request.Id))
        {
            Log.Warn($"ClimbTower_EnterLevel: battle {request.Id} is not in climbtower_battle.json.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = seed }));
    }
}

public sealed class ClimbTowerEnterLevelParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
    [JsonPropertyName("nBuffId")] public uint BuffId { get; set; }
    [JsonPropertyName("nTeamId")] public uint TeamId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
