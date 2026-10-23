using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) level entry. AlienDefense.EnterLevel
// (DLC/AlienDefense/AlienDefense.lua:827-854) sends the chosen level plus a fight log:
//
//     { nID, tbLog = { AlienDefenseEnter = { levelType, levelId, pal1, pal2, actId, fightGirl } } }
//
// and the callback (:856-865) stores nSeed (or tips sErr) before Launch.Response resumes the map.
// No dedicated server is involved: the level row's MapID points at a map the client loads
// standalone, and the seed is only echoed back inside the settlement payload.
//
// nID is the aliendefense_level ID column (6231..6236 normal for act 4), which is what
// alienlist's LevelList groups hold, so it validates against AlienDefenseData.
//
// Request:  { nID, tbLog }
// Response: { nSeed }
[CallGSApi("AlienDefense_EnterLevel")]
public class AlienDefense_EnterLevel : CallGSHandler<AlienDefenseEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, AlienDefenseEnterLevelParam request)
    {
        var player = context.Connection.Player;
        var uid = player?.Uid ?? 0;

        var actId = player != null
            ? player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid)
            : 0;

        if (request.LevelId == 0 ||
            Data.Excel.AlienDefenseExcel.FindByLevel(request.LevelId, actId) == null)
        {
            Log.Warn($"Unknown alien defense level {request.LevelId} requested by uid={uid} (act={actId}).");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        // The client only stores the seed and echoes it back inside the settlement payload, so any
        // non-zero value works.
        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        Log.Info($"Alien defense level {request.LevelId} entered by uid={uid} (seed={seed}).");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = seed }));
    }
}

public sealed class AlienDefenseEnterLevelParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
