using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) failure report. AlienDefense.LevelFail
// (DLC/AlienDefense/AlienDefense.lua:995-1016) fires whenever a run is lost or abandoned:
//
//     { tbLog = { AlienDefenseFinish = { <level type>, <level id>, <finish type>, ... } } }
//
// The payload carries no level id, no score and no kill info of its own -- everything lives inside
// tbLog.AlienDefenseFinish, the positional row the client builds in GetLevelLog
// (AlienDefense.lua:868-939: [1] level type, [2] level id, [3] LevelFinishType, [4] level time,
// [5] act id). The client registers no s2c callback for this api and Misc/S2C.lua:29-35 raises on an
// unknown command, so nothing may be sent back.
//
// A lost run credits nothing on purpose: score, per-pal kills and the per-level pass counter are only
// ever written by a successful AlienDefense_LevelSettlement, and the official server does not award
// partial progress either. This handler therefore exists so the run report is recorded instead of
// being dropped by the "No handler for CallGS API" fallback, and answers nothing.
//
// Request:  { tbLog }
// Response: none
[CallGSApi("AlienDefense_LevelFail")]
public class AlienDefense_LevelFail : CallGSHandler<AlienDefenseLevelFailParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_LevelFail));

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseLevelFailParam request)
    {
        var player = context.Connection.Player;
        var (levelId, actId, finishType) = ReadLevelLog(request.Log);

        Log.Info($"Alien defense run failed: uid={player?.Uid ?? 0}, level={levelId}, act={actId}, " +
                 $"finishType={finishType}.");

        return Task.FromResult(CallGSResult.NoResponse());
    }

    /// <summary>
    /// Reads the (level id, act id, finish type) triple out of tbLog.AlienDefenseFinish. The row is
    /// positional and may be missing (an old client, or a run that failed before the level manager
    /// existed leaves tbLog empty), so every slot is read defensively.
    /// </summary>
    private static (uint LevelId, uint ActId, uint FinishType) ReadLevelLog(JsonNode? log)
    {
        if (log?["AlienDefenseFinish"] is not JsonArray row)
            return (0, 0, 0);

        return (ReadUint(row, 1), ReadUint(row, 4), ReadUint(row, 2));
    }

    /// <summary>Value at <paramref name="index"/> (0-based into the client's 1-based row).</summary>
    private static uint ReadUint(JsonArray row, int index)
    {
        if (index < 0 || index >= row.Count || row[index] is not { } node)
            return 0;

        try
        {
            return node.GetValue<uint>();
        }
        catch
        {
            return uint.TryParse(node.ToString(), out var parsed) ? parsed : 0;
        }
    }
}

public sealed class AlienDefenseLevelFailParam
{
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
