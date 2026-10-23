using System.Text.Json.Nodes;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 联机-训练关卡 进入关卡 (T5/T9/T13 share this entry; OnlineTraining.lua header "联机 训练关卡逻辑 (单机)").
//
// Client origin: OnlineTraining:Req_EnterLevel (Launch/Online/OnlineTraining.lua:96-121) sends
// { nOnlineId, nID, nTeamID, tbLog } behind UI.ShowConnection(); the s2c callback (:123-133) closes the
// connection, fails on sErr and otherwise stores nSeed before Launch.Response loads the level. The map
// itself is opened locally by OnlineTrainingLogic:OnStart (Launch/Classes/OnlineTrainingLogic.lua:7-24)
// through Map.Open, so the server only has to hand out the seed - the same shape as the other
// single-player online entries (Handlers/VirCapture/VirCaptureLevel_EnterLevel.cs:55,
// Handlers/Rogue3D/Rogue3D_EnterSeasonLevel.cs:28).
//
// Request:  { "nOnlineId": <online id>, "nID": <level id>, "nTeamID": <team>, "tbLog": [...] }
// Response: { "nSeed": <seed> }
[CallGSApi("OnlineTraining_EnterLevel")]
public class OnlineTraining_EnterLevel : CallGSHandler
{
    private static readonly Logger Log = new(nameof(OnlineTraining_EnterLevel));
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = CallGSParam.Parse(param);

        var levelId = CallGSParam.ReadUint(request?["nID"]);
        var onlineId = CallGSParam.ReadUint(request?["nOnlineId"]);

        if (levelId == 0)
        {
            Log.Warn($"Online training entry without a level id from uid={player.Uid}.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var seed = Random.Next(1, 1_000_000_000);
        Log.Info(
            $"Online training level {levelId} (online id {onlineId}) entered by uid={player.Uid}, seed={seed}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = seed }));
    }
}
