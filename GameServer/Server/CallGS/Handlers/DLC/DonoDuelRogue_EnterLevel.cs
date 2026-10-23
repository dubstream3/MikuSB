using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 抓捕玩法 (rogue mode) entry point.
//
// Launch/Classes/DonoDuelRogueLogic.lua:OnStart registers DonoDuelRogue.REQ_ENTER_LEVEL and calls
// DonoDuelRogue:Req_EnterLevel (DLC/DonoDuel/DonoDuelRogue.lua:631-654), which sends
// { nID, nTeamID, tbLog } and shows the connection overlay; the s2c callback (:656-666) only needs
// an nSeed for DonoDuel:SetSeed before Launch.Response resumes the registered fFun, which opens the
// room map the client itself loaded (Map.Open(tbLevelCfg.nMapID)). No dedicated server involved.
//
// Request:  { nID, nTeamID, tbLog }
// Response: { nSeed }
[CallGSApi("DonoDuelRogue_EnterLevel")]
public class DonoDuelRogue_EnterLevel : CallGSHandler<DonoDuelRogueEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(DonoDuelRogue_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelRogueEnterLevelParam request)
    {
        var player = context.Connection.Player;
        var uid = player?.Uid ?? 0;

        // The room table (dlc/DonoDuel/donoduel_catch_room_*.json) is only mirrored on the client,
        // so the id is accepted as-is; a zero id means the client never resolved a room.
        if (request.LevelId == 0)
        {
            Log.Warn($"Donoduel rogue entry without a room id requested by uid={uid}.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        Log.Info($"Donoduel rogue room {request.LevelId} entered by uid={uid} (seed={seed}).");

        var response = new JsonObject { ["nSeed"] = seed };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}

public sealed class DonoDuelRogueEnterLevelParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public uint TeamId { get; set; }

    [JsonPropertyName("tbLog")]
    public JsonNode? Log { get; set; }
}
