using MikuSB.GameServer.Game.Dlc;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

// 荣誉冲突 entry point. The client asks for the current act right before opening the UI
// (Challenge/Defend/Defend.lua:610 DefendLogic.CheckOpenAct -> me:CallGS("DefendLogic_CheckOpenAct"))
// and its s2c callback (:634) copies nBeginTime/nEndTime over DefendLogic.tbActConf[nId], then feeds
// tbParam.bOpen into the stored callback. Every shipped window has expired, so the pinned DLC's act
// (challenge/defend/defend_act.json DlcId == the served activity id; DLC35 -> ids 49 and 50) is answered
// with a fresh one.
// Request: {} (no parameters)
// Response: { bOpen: true, nId, nBeginTime, nEndTime } with unix seconds
[CallGSApi("DefendLogic_CheckOpenAct")]
public class DefendLogic_CheckOpenAct : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var pinnedId = DlcRuntimeOverrides.EffectiveDlcId;
        var acts = DlcRuntimeOverrides.SelectDefendActs(pinnedId);

        if (acts.Count == 0)
        {
            Logger.GetByClassName().Warn($"No challenge/defend/defend_act row for DLC {pinnedId}.");
            return Task.FromResult(CallGSResult.Ok("{\"bOpen\":false}"));
        }

        var act = acts[0];
        var (start, end) = DlcRuntimeOverrides.Window();

        Logger.GetByClassName().Info(
            $"Opening defend act {act.Id} of DLC {pinnedId} for uid={context.Connection.Player?.Uid}.");

        var response = new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = act.Id,
            ["nBeginTime"] = start,
            ["nEndTime"] = end
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}
