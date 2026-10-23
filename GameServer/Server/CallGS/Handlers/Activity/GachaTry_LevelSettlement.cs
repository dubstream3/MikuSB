using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 先行试用 / 角色试玩 (gacha try) level settlement, wrapped channel entry. GachaTry.Req_LevelSettlement
// (Activity/GachaTry.lua:289-321) sends the run through Reconnect.Send_SettleInfo, so the request
// actually arrives as Chapter_DealLevelSettlement with sCmd = "GachaTry_LevelSettlement" and the
// direct CallGS API below is never hit by the live client:
//
//     { nActId, nGirlIdx, nLevelID, nTime, nSeed, tbKill, tbMonster, tbLog }
//
// The callback (:324-327) closes the connection spinner and hands the payload straight to the
// success panel:
//
//     UI.CloseConnection()
//     Launch.Response('GachaTry_LevelSettlement', tbAward)   -- GachaTryLogic.lua: Launch.tbAward = tbData
//
// Launch.tbAward has to be a list of reward *groups*, each group a list of [G,D,P,L,Count] rows
// (UMG/Success/umg_success.lua:158-175 unpacks two levels deep) - a flat row array renders no icons
// at all, which is the trap DlcChapterSettlement.cs documents.
//
// A missing settlement handler is fatal rather than cosmetic: Reconnect.Tick_SettleInfo re-sends every
// 2 s and force-disconnects the client after 5 unanswered attempts (Misc/Reconnect.lua:160-180), i.e.
// roughly ten seconds after the trial fight ends.
//
// Request:  { nActId, nGirlIdx, nLevelID, ... }
// Response: [ [ [G,D,P,L,Count], ... ] ]  (one group: the reward of this girl's first clear)
[CallGSApi("GachaTry_LevelSettlement")]
public class GachaTry_LevelSettlement : CallGSHandler<GachaTryLevelSettlementParam>
{
    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        GachaTryLevelSettlementParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var (payload, sync) = await ApplyAsync(player, request);
        return CallGSResult.Ok(payload, sync);
    }

    /// <summary>
    /// Entry point for the wrapped settlement channel (see the class comment). Chapter_DealLevelSettlement
    /// calls this to share one implementation with the direct API above.
    /// </summary>
    internal static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var request = tbParam?.Deserialize<GachaTryLevelSettlementParam>(CallGSJson.Options);
        if (request == null || request.ActId == 0)
            return (new JsonObject { ["sErr"] = "error.BadParam" }, null);

        return await ApplyAsync(player, request);
    }

    private static async ValueTask<(JsonNode Payload, NtfSyncPlayer Sync)> ApplyAsync(
        PlayerInstance player,
        GachaTryLevelSettlementParam request)
    {
        var sync = new NtfSyncPlayer();
        var actId = GachaTryService.ResolveActId(request.ActId);
        var group = await GachaTryService.GrantGirlAsync(player, actId, request.GirlIdx, sync);

        return (new JsonArray { group }, sync);
    }
}

public sealed class GachaTryLevelSettlementParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }
    [JsonPropertyName("nGirlIdx")] public uint GirlIdx { get; set; }
    [JsonPropertyName("nLevelID")] public uint LevelId { get; set; }
    [JsonPropertyName("nTime")] public uint Time { get; set; }
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
