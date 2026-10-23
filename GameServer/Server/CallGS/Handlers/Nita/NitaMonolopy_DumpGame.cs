using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Database;
using MikuSB.Data;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

// 妮塔大富翁 (Nita monopoly, activity 283; DLC27 DlcFuncConfId `Nita=283`) save-game push.
//
// The client hangs one dump callback on two board states (Activity/NitaMonolopy.lua:2359-2360:
// CellPreTriger and CellTrigerEnd), so every cell trigger sends this twice:
//
//     { sGameData: "<json of Game:Dump()>",
//       tbCellData: { "<cellIndex>": "<json of Cell:Dump()>" },   -- map, not array
//       sLog: string|nil,
//       nUniqueNum: int }                                        -- per-request sequence number
//
// The answer is matched purely by nUniqueNum: NitaMonolopy.lua:2362 drops anything that does not
// echo the current value, and :2355-2357 arms a 3 s timer that simply re-sends on timeout (:2366),
// so a wrong or missing nUniqueNum leaves the board stuck on the current cell and hammering the
// server every 3 s. Conversely an sErr aborts the advance through the tip branch (:2371-2376).
//
// Storage layout mirrors the client (:2311-2317, :2390-2424): string attribute group 102, sid 1 for
// the game blob and sid 10 + cellIndex for each visited cell. Nothing is echoed back through
// NtfSyncPlayer -- the client never reads these attributes outside of RecoverGame.
//
// Request:  { sGameData, tbCellData, sLog, nUniqueNum }
// Response: { nUniqueNum } (same value, no sErr)
[CallGSApi("NitaMonolopy_DumpGame")]
public class NitaMonolopy_DumpGame : CallGSHandler<NitaMonolopyDumpGameParam>
{
    private static readonly Logger Log = new(nameof(NitaMonolopy_DumpGame));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, NitaMonolopyDumpGameParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        const uint gid = AttrIds.Activity.NitaMonopolyStringGroupId;
        if (!string.IsNullOrEmpty(request.GameData))
        {
            player.Attributes.SetString(gid, AttrIds.Activity.NitaMonopolyBaseGameDataSid, request.GameData);
        }

        if (request.CellData != null)
        {
            foreach (var (key, value) in request.CellData)
            {
                if (string.IsNullOrEmpty(value))
                    continue;

                if (!uint.TryParse(key, out var cellIndex) || cellIndex == 0)
                {
                    Log.Warn($"Nita dump: unparsable cell key '{key}' for uid={player.Uid}.");
                    continue;
                }

                player.Attributes.SetString(
                    gid,
                    AttrIds.Activity.NitaMonopolyCellDataStartSid + cellIndex,
                    value);
            }
        }

        Log.Debug($"Nita dump #{request.UniqueNum} for uid={player.Uid} " +
                  $"({request.GameData?.Length ?? 0} game byte(s), {request.CellData?.Count ?? 0} cell(s)).");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueNum"] = request.UniqueNum }));
    }
}

public sealed class NitaMonolopyDumpGameParam
{
    [JsonPropertyName("sGameData")] public string? GameData { get; set; }
    [JsonPropertyName("tbCellData")] public Dictionary<string, string>? CellData { get; set; }
    [JsonPropertyName("sLog")] public string? Log { get; set; }
    [JsonPropertyName("nUniqueNum")] public int UniqueNum { get; set; }
}
