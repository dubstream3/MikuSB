using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 (Donoduel) arena challenge entry point.
//
// DLC/DonoDuel/DonoDuelArena.lua:604 DonoDuelArena:Req_EnterLevel sends
// { nID, nTeamID, tbLog } and its s2c callback (:624) only needs an nSeed:
//
//     DonoDuel:SetSeed(tbParam.nSeed)
//     Launch.Response(DonoDuelArena.REQ_ENTER_LEVEL)
//
// That resumes Launch/Classes/DonoDuelArenaLogic.lua:OnStart, whose registered fFun calls
// Map.Open(tbLevelCfg.nMapID, "") -- the arena map (5505 for every row of
// dlc/DonoDuel/donoduel_arena_level.json) is loaded by the client itself, so unlike the online
// party modes (海姆优化作战 / 进击行动) no dedicated server is involved.
//
// Request:  { nID, nTeamID, tbLog }
// Response: { nSeed }
[CallGSApi("DonoDuelArena_EnterLevel")]
public class DonoDuelArena_EnterLevel : CallGSHandler<DonoDuelArenaEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(DonoDuelArena_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelArenaEnterLevelParam request)
    {
        var player = context.Connection.Player;
        var uid = player?.Uid ?? 0;

        if (request.LevelId == 0 || !GameData.DonoDuelArenaBattleData.ContainsKey(request.LevelId))
        {
            Log.Warn($"Unknown Donoduel arena level {request.LevelId} requested by uid={uid}.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        // Any non-zero seed works: the client only stores it (DonoDuel:SetSeed) and echoes it back
        // inside the settlement payload.
        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        Log.Info($"Donoduel arena level {request.LevelId} entered by uid={uid} (seed={seed}).");

        var response = new JsonObject { ["nSeed"] = seed };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}

public sealed class DonoDuelArenaEnterLevelParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public uint TeamId { get; set; }

    [JsonPropertyName("tbLog")]
    public JsonNode? Log { get; set; }
}
