using System.Text.Json.Nodes;
using MikuSB.GameServer.Game.Quest;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_SyncGuideLevelPassData")]
public class Chapter_SyncGuideLevelPassData : CallGSHandler<JsonNode>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        context.Player.QuestManager.SyncGuideLevelPassData(request);
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
