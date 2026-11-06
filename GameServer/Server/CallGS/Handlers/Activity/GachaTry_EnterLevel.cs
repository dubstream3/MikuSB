using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Activity;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 先行试用 / 角色试玩 (gacha try) level entry. GachaTry.Req_EnterLevel
// (Activity/GachaTry.lua:266-276) sends the act, the 1-based roster index and the level:
//
//     { nActId, nGirlIdx, nLevelID, tbLog }
//
// and the callback (:279-286) only stores the seed before Launch.Response resumes the map:
//
//     GachaTry.bReqEnter = false
//     if tbRet.err then UI.ShowTip(tbRet.err) end
//     GachaTry.nSeed = tbRet.nSeed
//     Launch.Response('GachaTry_EnterLevel')
//
// No dedicated server is involved - the level row's MapID (5133 -> map 5054) is a map the client
// loads standalone - so the seed is only echoed back inside the settlement payload.
//
// Request:  { nActId, nGirlIdx, nLevelID, tbLog }
// Response: { nSeed }
//
// Nothing is granted here. The trial roster belongs to the level itself: try_girl.Girl carries the
// character and its level ([genre, detail, particular, level]) and the client builds the formation from
// that row, so the card never has to be owned — and this handler only runs after the client has already
// sent the entry, so a grant at this point could not have unlocked anything anyway.
[CallGSApi("GachaTry_EnterLevel")]
public class GachaTry_EnterLevel : CallGSHandler<GachaTryEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(GachaTry_EnterLevel));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, GachaTryEnterLevelParam request)
    {
        var player = context.Connection.Player;
        var uid = player?.Uid ?? 0;

        var actId = GachaTryService.ResolveActId(request.ActId);
        if (actId == 0 ||
            !GameData.GachaTryData.TryGetValue(actId, out var act) ||
            act.TryGirlAt(request.GirlIdx) == 0)
        {
            Log.Warn($"Unknown gacha try entry by uid={uid} " +
                     $"(act={request.ActId}, girl={request.GirlIdx}, level={request.LevelId}).");
            return CallGSResult.Error("error.BadParam");
        }

        // The client only stores the seed and echoes it back inside the settlement payload, so any
        // non-zero value works.
        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        Log.Info($"Gacha try act {actId} girl {request.GirlIdx} level {request.LevelId} " +
                 $"entered by uid={uid} (seed={seed}).");

        return CallGSResult.Ok(new JsonObject { ["nSeed"] = seed });
    }
}

public sealed class GachaTryEnterLevelParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }
    [JsonPropertyName("nGirlIdx")] public uint GirlIdx { get; set; }
    [JsonPropertyName("nLevelID")] public uint LevelId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
