using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

// 星祈心愿 entry gate (DLC/StarWish/StarWishLogic.lua:946-990).
//
//     request : me:CallGS("StarWish_CheckOpenAct")   -- no body
//     reply   : { bOpen, nID, nStartTime, nEndTime }
//
// The panel is opened from DlcFuncCelebrate, which hops straight into
// StarWishLogic:CheckOpenAct after UI.ShowConnection(), so a missing handler leaves the spinner up
// forever. nID must be a valid StarWishConfig.tbTimeList key or GetCurTimeConfig() returns nil and
// the callback dies on conf.nID (:970-971) -- ID=1 is the only row we ship.
//
// The shipped window (2025-12-25 .. 2026-01-29) is long gone and the client compares GetTime()
// directly (:123/:147/:713), so the reply carries a fresh window and the client overwrites its local
// copy with it. Both fields must be present: passing only one leaves the other untouched
// (:965-968).
[CallGSApi("StarWish_CheckOpenAct")]
public class StarWish_CheckOpenAct : CallGSHandler
{
    private static readonly Logger Log = new(nameof(StarWish_CheckOpenAct));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var now = DateTime.Now;
        var act = StarWishService.SelectAct(now);
        if (act == null)
        {
            Log.Warn("StarWish_CheckOpenAct without a loaded period row.");
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));
        }

        var sync = new NtfSyncPlayer();

        // Seed the period id before answering: the callback resolves the time config from
        // me:GetAttribute(StarWish, ActivitySid) and dereferences it without a nil check.
        if (StarWishService.GetActId(player) != act.Id)
            StarWishService.Set(player, AttrIds.StarWish.ActivitySid, act.Id, sync);

        var unixNow = new DateTimeOffset(now).ToUnixTimeSeconds();
        var start = unixNow - 3600;
        var end = unixNow + 30 * 24 * 3600;

        Log.Info($"uid={player.Uid} opens StarWish period {act.Id} (window {start}..{end}).");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bOpen"] = true,
            ["nID"] = act.Id,
            ["nStartTime"] = start,
            ["nEndTime"] = end
        }, sync));
    }
}
