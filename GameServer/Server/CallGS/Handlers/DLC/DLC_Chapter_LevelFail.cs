using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DLC_Chapter.Req_LevelFail (Launch/Dlc/DLC_Chapter.lua:578-589), reached from
// Launch/Classes/DlcChapterLogic.lua:79 whenever a DLC story level ends in a defeat. The request is
// {nID, tbLog} and the client registers no s2c callback for 'DLC_Chapter_LevelFail' at all: the
// defeat screen is opened locally (DlcChapterLogic.lua:78-91 -> UI.Open("Fail")) without waiting for
// an answer, exactly like DonoDuelArena_LevelFail. Before this handler existed the router answered
// with its "No handler for CallGS API" error (CallGSRouter.cs:51-65), which Misc/S2C.lua:29-36 turned
// into "s2c.Dispatch error. No command named: DLC_Chapter_LevelFail" while the player was looking at
// the defeat screen.
//
// A defeat credits nothing: (21, levelId) keeps its star bits and (22, levelId) keeps its clear
// counter, so the level still has to be beaten before it can be swept. This handler therefore only
// records the failure and answers nothing.
//
// Request:  { "nID": <level id>, "tbLog": {...} }
// Response: none.
[CallGSApi("DLC_Chapter_LevelFail")]
public class DLC_Chapter_LevelFail : CallGSHandler<DlcChapterLevelFailParam>
{
    private static readonly Logger Logger = new("DLC_Chapter");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DlcChapterLevelFailParam request)
    {
        var levelId = request.LevelId;
        Logger.Info($"DLC chapter level {levelId} failed for uid={context.Player.Uid}.");

        return Task.FromResult(CallGSResult.NoResponse());
    }
}

public sealed class DlcChapterLevelFailParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
