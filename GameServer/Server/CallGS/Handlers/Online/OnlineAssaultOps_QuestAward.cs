namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 进击行动 一键领取任务奖励.
//
// Client origin: OnlineAssaultOps:ReqQuestAward (Launch/Online/Classes/OnlineAssaultOps.lua:1492-1531)
// sends { nOnlineId, tbIdList } and its callback (:1518-1531) hands tbAwardList to Item.Gain before
// refreshing the mission panel. The per-season mission progress this mode reads out of group 183
// (sids 1001+, GetHistoryValue) is not tracked by MikuSB yet, so there is nothing to grant: answer
// with an empty success so the connection UI closes and the list refreshes, instead of the router's
// ui.TxtNotOpen fallback.
//
// Request:  { "tbIdList": [<mission id>, ...] }
// Response: {}
[CallGSApi("OnlineAssaultOps_QuestAward")]
public class OnlineAssaultOps_QuestAward : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
