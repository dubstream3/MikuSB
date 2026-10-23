using System.Text.Json;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Quest;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

// Success response shape expected by Lua:
// { nSeed = random_number }
[CallGSApi("Daily_EnterLevel")]
public class Daily_EnterLevel : CallGSHandler<DailyEnterLevelParam>
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DailyEnterLevelParam req)
    {

        if (req == null || req.LevelId == 0 || req.TeamId == 0 || !GameData.DailyLevelData.ContainsKey(req.LevelId) ||
            !context.Connection.Player!.QuestManager.CanEnterLevel(QuestLevelType.Daily, req.LevelId))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var seed = (uint)Random.Next(1, 1000000000);
        context.Connection.Player.BeginLevelSession(QuestLevelType.Daily, req.LevelId, seed, req.TeamId);
        var rsp = $"{{\"nSeed\":{seed}}}";
        return Task.FromResult(CallGSResult.Ok(rsp));
    }
}

public sealed class DailyEnterLevelParam
{
    [JsonPropertyName("nLevelID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public uint TeamId { get; set; }
}
