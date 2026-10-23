using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

// Client side origin: DefendLogic.Req_SelectDiff (Challenge/Defend/Defend.lua:559-565) shows the
// connection spinner and sends {nDiffId}. The registered callback (Defend.lua:567-575) closes the
// spinner and then calls UI.Call2('DefenseDifficulty', 'OnSelectRsp') as long as the answer carries
// no sErr, so an empty object is all the difficulty list needs.
//
// The picked difficulty is mirrored into attribute (gid 10, sid 8) = CurDiff, which is the slot the
// client reads back in DefendLogic.GetIDAndDiff, so the following level entry uses the same diff.
// Request: { "nDiffId": <uint> }
// Response: {} (must NOT contain sErr)
[CallGSApi("DefendLogic_SelectDiff")]
public class DefendLogic_SelectDiff : CallGSHandler
{
    private const uint GroupId = AttrIds.Defend.Gid;
    private const uint CurDiffSid = 8;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        var diff = 0u;

        if (player != null && JsonNode.Parse(param) is JsonObject request)
            diff = uint.TryParse(request["nDiffId"]?.ToString(), out var parsed) ? parsed : 0;

        if (player == null || diff == 0)
        {
            Logger.GetByClassName().Info($"Difficulty selection accepted without a usable diff (param='{param}').");
            return Task.FromResult(CallGSResult.Ok("{}"));
        }

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.GetOrCreate(GroupId, CurDiffSid);
        if (attr.Val != diff)
        {
            attr.Val = diff;
            player.Attributes.SyncTo(sync, attr);
        }

        Logger.GetByClassName().Info($"Difficulty {diff} selected for uid={player.Uid}.");
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}
