using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Quest;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

// Success response shape expected by Lua:
// { nSeed = random_number }
[CallGSApi("Chapter_EnterLevel")]
public class Chapter_EnterLevel : CallGSHandler<ChapterEnterLevelParam>
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChapterEnterLevelParam request)
    {
        if (request.LevelId == 0 || request.TeamId == 0 || !GameData.ChapterLevelData.ContainsKey(request.LevelId) ||
            !context.Player.QuestManager.CanEnterLevel(QuestLevelType.Chapter, request.LevelId))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var seed = (uint)Random.Next(1, 1000000000);
        context.Player.BeginLevelSession(QuestLevelType.Chapter, request.LevelId, seed, request.TeamId);
        var rsp = $"{{\"nSeed\":{seed}}}";
        return Task.FromResult(CallGSResult.Ok(rsp));
    }
}

public sealed class ChapterEnterLevelParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public uint TeamId { get; set; }
}
