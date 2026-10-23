using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

// 弑神斗场 (season boss) entry gate. SeasonBossLogic.CheckOpen (Challenge/SeasonBoss/SeasonBoss.lua:1110)
// sends { ForceEnter? } and the callback (:1112-1129) only reads two flags:
//
//     if tbData.bNeedReresh == true then SeasonBossLogic.SetCurSelectLevelIndex(0) end
//     if tbData.bForceEnter then UI.SafeOpen("DungeonsSeasonBoss", SeasonBossLogic.GetCurActID()) end
//
// bNeedReresh exists to re-sync the UI after the server rotated a term mid-session. This server pins
// one act (login seeds (18,1)/(18,2) and PushSeasonBossTime widens every term window), so nothing ever
// rotates and the flag stays absent. bForceEnter is echoed back so the client's forced re-open works.
//
// Request:  { ForceEnter? }
// Response: { bForceEnter? }
[CallGSApi("SeasonBoss_CheckOpen")]
public class SeasonBoss_CheckOpen : CallGSHandler<SeasonBossCheckOpenParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SeasonBossCheckOpenParam request)
    {
        var response = new JsonObject();
        if (request.ForceEnter)
            response["bForceEnter"] = true;

        return Task.FromResult(CallGSResult.Ok(response));
    }
}

public sealed class SeasonBossCheckOpenParam
{
    [JsonPropertyName("ForceEnter")] public bool ForceEnter { get; set; }
}
