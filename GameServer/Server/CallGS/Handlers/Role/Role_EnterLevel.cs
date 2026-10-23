using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Quest;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Role;

public sealed class RoleEnterLevelParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public uint TeamId { get; set; }
}

// Success response shape expected by Lua: { nSeed = random_number }
[CallGSApi("Role_EnterLevel")]
public class Role_EnterLevel : CallGSHandler<RoleEnterLevelParam>
{
    private static readonly Random _random = new Random();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RoleEnterLevelParam request)
    {
        if (request.LevelId == 0 || request.TeamId == 0 || !GameData.RoleLevelData.ContainsKey(request.LevelId) ||
            !context.Player.QuestManager.CanEnterLevel(QuestLevelType.Role, request.LevelId))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        uint seed = (uint)_random.Next(1, 1000000000);
        context.Player.BeginLevelSession(QuestLevelType.Role, request.LevelId, seed, request.TeamId);

        string rsp = $"{{\"nSeed\":{seed}}}";
        return Task.FromResult(CallGSResult.Ok(rsp));
    }

}
