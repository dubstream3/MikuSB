using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// FarmWarActivity.UpdateFormation (FarmWarActivity.lua:1491-1507). The client throttles this: it only
// sends when the saved formation/plant pair actually changed, and the s2c target
// ("FarmwarFormation.OnUpdateFormation") does not exist in the widget file, so an empty object is a
// valid success path. The payload is persisted as the (104, levelId) JSON string the level template
// reads back in FarmWarLevel.lua:59.
[CallGSApi("FarmWarLogic_UpdateFormation")]
public class FarmWarLogic_UpdateFormation : CallGSHandler<FarmWarUpdateFormationParam>
{
    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        FarmWarUpdateFormationParam request)
    {
        var player = context.Connection.Player!;
        if (!GameData.FarmWarLevelData.ContainsKey(request.LevelId))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen", "FarmWarLogic_UpdateFormation"));

        var sync = new NtfSyncPlayer();
        var json = request.Team?.ToJsonString() ?? "[]";
        FarmWarActivityService.SetFormation(player, request.LevelId, json, sync);

        return Task.FromResult(CallGSResult.Ok("{}", sync, "FarmWarLogic_UpdateFormation"));
    }
}

public sealed class FarmWarUpdateFormationParam
{
    [JsonPropertyName("nLevelId")] public uint LevelId { get; set; }
    [JsonPropertyName("tbTeam")] public JsonArray? Team { get; set; }
}
