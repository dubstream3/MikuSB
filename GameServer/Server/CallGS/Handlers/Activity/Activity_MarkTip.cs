using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// "已读" marker for the activity notice popup (公告打脸).
///
/// Client send point: Activity/Sign.lua:528-535 Sign.Req_MarkActivityTip(InMarkSubId, InCallBack)
/// builds cmd = { nGId = Sign.MarkTipGId, nSubId = InMarkSubId } and calls
/// me:CallGS("Activity_MarkTip", json.encode(cmd)). Sign.MarkTipGId is never assigned anywhere in the
/// script tree, so nGId ships as null and the request is effectively {nSubId: N}. The callback
/// (s2c.Register 'Activity_MarkTip', Sign.lua:537-545) reads nothing - it only fires the caller's
/// local flag.
///
/// No state is persisted on purpose: the only consumer is the popup's own local cache, and the audit
/// (docs/progress/048 §1) confirmed the notice bitmap belongs to the client-side Activityface group
/// 60, which is updated through ActivityFace_Update. Answering keeps the spinner closed; inventing an
/// attribute write here would only make a later 60-group read disagree with the client's own counter.
/// </summary>
[CallGSApi("Activity_MarkTip")]
public class Activity_MarkTip : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        Logger.GetByClassName().Info($"Activity_MarkTip for uid={player.Uid}: {param}");

        return Task.FromResult(CallGSResult.Ok(new JsonObject().ToJsonString(), null, "Activity_MarkTip"));
    }
}
