using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 先行试用 / 角色试玩 (gacha try) failed run. GachaTry.Req_LevelFail (Activity/GachaTry.lua:330-345)
// reports a lost/aborted trial fight:
//
//     { nActId, nGirlIdx, nLevelID, tbLog }
//
// Nothing is registered for the response name: grep for s2c.Register('GachaTry_LevelFail') over
// Game/Content/Script finds only GachaTry_EnterLevel (:279), GachaTry_LevelSettlement (:324) and
// GacheTry_GetAlreadyPassedAward (:375). Answering with a payload would therefore make the client log
// "No command named", so this handler deliberately answers nothing - the request only has to stop
// being an "unknown API" error.
//
// Request:  { nActId, nGirlIdx, nLevelID, tbLog }
// Response: none
[CallGSApi("GachaTry_LevelFail")]
public class GachaTry_LevelFail : CallGSHandler<GachaTryLevelFailParam>
{
    private static readonly Logger Log = new(nameof(GachaTry_LevelFail));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GachaTryLevelFailParam request)
    {
        var uid = context.Connection.Player?.Uid ?? 0;
        Log.Info($"Gacha try act {request.ActId} girl {request.GirlIdx} level {request.LevelId} " +
                 $"failed by uid={uid}.");
        return Task.FromResult(CallGSResult.NoResponse());
    }
}

public sealed class GachaTryLevelFailParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }
    [JsonPropertyName("nGirlIdx")] public uint GirlIdx { get; set; }
    [JsonPropertyName("nLevelID")] public uint LevelId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
