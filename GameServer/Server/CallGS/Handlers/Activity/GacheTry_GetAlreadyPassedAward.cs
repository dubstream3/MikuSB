using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Activity;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 先行试用 / 角色试玩 (gacha try) "claim every reward already earned" button. The client scans its own
// configuration first - GachaTry.Req_GetAlreadyPassedAward (Activity/GachaTry.lua:347-372) only sends
// the request when CheckTryGirlAlreadyPassed (:195-209) found a girl that was cleared in an *older*
// try activity of the same character, and it sends the misspelled api name on purpose:
//
//     me:CallGS("GacheTry_GetAlreadyPassedAward", json.encode({ nActId = nActId }))
//
// The callback (:375-388) closes the spinner and merges the groups into one Item.Gain:
//
//     if tbRet.sErr or not tbRet.tbAwardList or #tbRet.tbAwardList == 0 then return UI.ShowTip(tbRet.sErr) end
//     for _, v in ipairs(tbRet.tbAwardList) do Launch.Merg(v, tbAwards) end
//     Item.Gain(tbAwards)
//
// That scan is a cross-era catch-up: the relation table it walks (GachaTry.tbTryRelationActivty) points
// at try activities from earlier versions, and every one of them is closed under this server's era gate
// (the pinned DLC only opens its own era), so the client never reaches this request in practice. The
// handler therefore stays a well-formed answer with an empty list instead of re-paying anything: the
// per-girl progress bit *is* the grant flag (GachaTryService.GrantGirlAsync), so a girl can never be
// "passed but unpaid" on this server.
//
// Request:  { nActId }
// Response: { nActId, tbAwardList: [] }
[CallGSApi("GacheTry_GetAlreadyPassedAward")]
public class GacheTry_GetAlreadyPassedAward : CallGSHandler<GacheTryGetAlreadyPassedAwardParam>
{
    private static readonly Logger Log = new(nameof(GacheTry_GetAlreadyPassedAward));

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        GacheTryGetAlreadyPassedAwardParam request)
    {
        var uid = context.Connection.Player?.Uid ?? 0;
        var actId = GachaTryService.ResolveActId(request.ActId);

        Log.Info($"Gacha try catch-up rewards requested by uid={uid} (act={request.ActId} -> {actId}): " +
                 "nothing to re-pay, the progress bit is the grant flag.");

        var response = new JsonObject
        {
            ["nActId"] = actId,
            ["tbAwardList"] = new JsonArray()
        };

        return Task.FromResult(CallGSResult.Ok(response));
    }
}

public sealed class GacheTryGetAlreadyPassedAwardParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }
}
