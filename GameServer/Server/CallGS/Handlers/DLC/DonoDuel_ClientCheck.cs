using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Server.Packet.Recv.Login;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 entry point. The client asks the server to open the season right before entering
// (DLC/DonoDuel/DonoDuel.lua:1895 DonoDuel:DoCheckOpen -> me:CallGS("DonoDuel_ClientCheck")) and its
// s2c callback (:1903) copies nOpenTime/nCloseTime/tbCondition over
// DonoDuelConfig.tbSeasonList[tbParam.nActId], then calls DonoDuel:EnterBase(true).
//
// The gate itself is DlcFuncDonoduel.OnGetTimeInfo -> DonoDuelConfig:GetSeasonConfig(cfg), which
// reads the OpenTime/CloseTime columns of dlc/DonoDuel/donoduel_season.txt (season 1 =
// [202602050400]..[202603120400], long expired). DonoDuelConfig:CheckOpenConfig additionally
// requires DonoDuel:GetActId() (~= 0) -- that is attribute (DonoDuel.GroupId=185, DonoDuel.nActId=1).
//
// Request: {} (no parameters)
// Response: { nActId, nOpenTime, nCloseTime, tbCondition } with unix seconds
[CallGSApi("DonoDuel_ClientCheck")]
public class DonoDuel_ClientCheck : CallGSHandler
{
    /// <summary>DonoDuel.lua:7/9 -- me:GetAttribute(185, 1) is the season ("期数").</summary>
    private const uint AttributeGroup = 185;
    private const uint AttributeActId = 1;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        var act = DlcRuntimeOverrides.GetPinnedActivity();

        var actId = DlcRuntimeOverrides.ResolveFuncConfigId(act?.DlcFuncConfId, "Donoduel");
        if (actId == 0)
            actId = 1;

        var (start, end) = DlcRuntimeOverrides.Window();
        var uid = player?.Uid ?? 0;

        // The client only patches tbSeasonList[nActId], so the attribute has to carry the same
        // season id or DonoDuelConfig:GetCurSeasonConfig() would look up a row we never touched.
        // The attribute is pushed explicitly because the client may already be in-game.
        if (player != null)
        {
            var sync = new NtfSyncPlayer();
            var attr = player.Attributes.Set(AttributeGroup, AttributeActId, actId);
            player.Attributes.SyncTo(sync, attr);
            // Same request is also the moment the player is about to pick a deck, so repair the
            // accounts whose story mission cards were dropped before DonoDuelBag existed (no-op once
            // the bag has any card at all).
            DonoDuelBag.EnsureStarterDeck(player, sync);
            await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        }

        Logger.GetByClassName().Info(
            $"Opening Donoduel season {actId} (attr {AttributeGroup},{AttributeActId}) for uid={uid}.");

        // Re-stamp the client's own time tables before the client walks into the base: the season,
        // tbFunctionList (this is what DonoDuel:CheckNpcShowMap gates every function NPC on, so
        // without it the base shows interest markers but no interaction prompt) and tbArenaBattle.
        // The module is guaranteed to be loaded here - this request comes from DonoDuel:DoCheckOpen.
        await HandlerReqLogin.SendClientPatch(
            context.Connection, nameof(DlcClientPatches.DonoDuelArenaWindows), DlcClientPatches.DonoDuelArenaWindows);
        // The base spawn point is repairable here too: this is the request the client sends on its way
        // into DonoDuelBaseHouse.
        await HandlerReqLogin.SendClientPatch(
            context.Connection, nameof(DlcClientPatches.DonoDuelBaseSpawn), DlcClientPatches.DonoDuelBaseSpawn);

        var response = new JsonObject
        {
            ["nActId"] = actId,
            ["nOpenTime"] = start,
            ["nCloseTime"] = end,
            ["tbCondition"] = new JsonArray()
        };

        return CallGSResult.Ok(response.ToJsonString());
    }
}
