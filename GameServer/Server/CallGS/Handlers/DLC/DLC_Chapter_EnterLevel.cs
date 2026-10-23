using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DLC_Chapter.Req_EnterLevel (Launch/Dlc/DLC_Chapter.lua:407-434) sends
// {nID, nChapterID, nTeamID, tbLog, nMultiple, bFree}; the callback (DLC_Chapter.lua:435-447) only
// reads tbRet.nSeed and then hands control back to Launch, which opens the level map itself. The DLC
// story chapters use dlc/dlc1/chapter/level.json, which the generic chapter table does not contain,
// hence the dedicated lookup here.
// Request:  { "nID": <levelId>, "nChapterID": <chapterId>, "nTeamID": <teamId> }
// Response: { "nSeed": <seed> }
//
// Side effect: marks the act's opening cinematic as watched, (15, 9) = 1.
//
// DLC_Logic.SetOpFinish (DLC/DLC_Logic.lua:285-287) writes that slot through the native
// me:SetAttribute, which is purely client local -- the protocol has no ReqSetAttribute command
// (Proto/CmdIds.cs only defines the NtfSetAttr / NtfSetStrAttr notifications), so the value is lost at
// the next login and the "自往昔归还" opening movie (conf.sOP, DLC_Logic.lua:364-374) replays every
// time the activity is opened. Reaching this handler means the player already walked through the
// activity's main UI, which only opens after the movie has played, so it is the earliest point the
// server can observe the movie being done. The slot is shared by every DLC act, exactly as it is on the
// client (DLC_Logic.GetCurConf picks the current act and IsOpFinish does not scope it per act).
[CallGSApi("DLC_Chapter_EnterLevel")]
public class DLC_Chapter_EnterLevel : CallGSHandler<DlcChapterEnterLevelParam>
{
    private static readonly Random Random = new();
    private static readonly Logger Logger = new("DLC_Chapter");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DlcChapterEnterLevelParam request)
    {
        if (request.LevelId == 0 || !GameData.DlcChapterLevelData.ContainsKey(request.LevelId))
        {
            Logger.Warn($"Rejected DLC chapter enter for unknown level {request.LevelId} (chapter {request.ChapterId}).");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Player;
        var sync = new NtfSyncPlayer();
        var opAttr = player.Attributes.GetOrCreate(AttrIds.Dlc.Gid, AttrIds.Dlc.OperationFinishSid);
        if (opAttr.Val == 0)
        {
            opAttr.Val = 1;
            player.Attributes.SyncTo(sync, opAttr);
            Logger.Info($"DLC opening cinematic marked as watched for uid={player.Uid} (act {request.ChapterId}).");
        }

        var seed = (uint)Random.Next(1, 1000000000);
        Logger.Info($"DLC chapter {request.ChapterId} level {request.LevelId} entered with seed {seed} for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = seed }, sync));
    }
}

public sealed class DlcChapterEnterLevelParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nChapterID")] public uint ChapterId { get; set; }
    [JsonPropertyName("nTeamID")] public uint TeamId { get; set; }
}
