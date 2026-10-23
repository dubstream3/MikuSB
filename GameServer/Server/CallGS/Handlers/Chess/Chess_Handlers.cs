using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Chess;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

// DLC1 board (chess.txt ID 1) and the shared "enter a board / fight a board fight" entry points.
//
// The three CheckOpenAct handlers in this family all answer the same shape:
//   { bOpen, nId, nBeginTime, nEndTime }
// nId decides which UI the client opens (ChessLogic.lua:151-157: 2/4 -> Richman, else ChessActivity;
// RichmanLogic.lua:418-419 only accepts 2/4; RichManLoveLogic.lua:462 only accepts 3). The two time
// fields must be supplied *together* -- the client only overwrites its own tbTimeConf row when all
// three of nId/nBeginTime/nEndTime are present (ChessLogic.lua:146-150), otherwise the stale config
// window keeps the board locked.
[CallGSApi("ChessLogic_CheckOpenAct")]
public class ChessLogic_CheckOpenAct : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var row = ChessActivityService.SelectActivity(DateTime.Now, 4, 2, 1);
        if (row == null)
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));

        if (player != null && player.Attributes.GetValue(AttrIds.Chess.Gid, AttrIds.Chess.ActivitySid) != row.Id)
        {
            var sync = new NtfSyncPlayer();
            var attr = player.Attributes.Set(AttrIds.Chess.Gid, AttrIds.Chess.ActivitySid, row.Id);
            player.Attributes.SyncTo(sync, attr);
        }

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = row.Id,
            ["nBeginTime"] = now - 3600,
            ["nEndTime"] = now + 30L * 24 * 60 * 60
        }));
    }
}

// 3 fixed slots, each [itemId, isTrial]; an empty slot is the one element array [0]
// (ChessLogic.lua:300-318). The reply body is not read at all (only CloseConnection), so the state
// only has to survive into ChessLogic.GetLineup, which json.decodes (12, 51) and walks it with pairs
// (ChessLogic.lua:270-285).
[CallGSApi("ChessLogic_ChangeFormation")]
public class ChessLogic_ChangeFormation : CallGSHandler<ChessFormationParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChessFormationParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var raw = request.Formation?.ToJsonString() ?? "[]";
        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.SetString(AttrIds.Chess.Gid, AttrIds.Chess.TeamInfoSid, raw);
        player.Attributes.SyncTo(sync, attr);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

// ChessLogic.lua:163-185: the callback reads nId/nMapId and then dereferences GetMapConf() without a
// nil check, so this handler must always answer with the echoed pair -- an error reply turns into a
// Lua exception inside the board UI.
[CallGSApi("ChessLogic_EnterMap")]
public class ChessLogic_EnterMap : CallGSHandler<ChessEnterMapParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChessEnterMapParam request)
    {
        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = request.Id,
            ["nMapId"] = request.MapId
        }));
    }
}

// Board fights go through the normal level pipeline: Client.Req_EnterLevel -> Chess_EnterLevel just
// needs to hand back a seed (ChessClient.lua:487-491); the settlement arrives later as
// 'Chess_LevelSettlement' through Chapter_DealLevelSettlement.
[CallGSApi("Chess_EnterLevel")]
public class Chess_EnterLevel : CallGSHandler<ChessEnterLevelParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChessEnterLevelParam request)
    {
        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nSeed"] = ChessActivityService.NewSeed()
        }));
    }
}

// No s2c registration in the whole script tree (ChessClient.lua:518-529 only sends), so answering
// would make Misc/S2C.lua:29-36 raise "cmd not found".
[CallGSApi("Chess_LevelFail")]
public class Chess_LevelFail : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.NoResponse());
}

public sealed class ChessFormationParam
{
    [JsonPropertyName("tbFormation")] public JsonNode? Formation { get; set; }
}

public sealed class ChessEnterMapParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
    [JsonPropertyName("nMapId")] public uint MapId { get; set; }
}

public sealed class ChessEnterLevelParam
{
    [JsonPropertyName("activityId")] public uint ActivityId { get; set; }
    [JsonPropertyName("activityType")] public uint ActivityType { get; set; }
    [JsonPropertyName("moduleName")] public string? Module { get; set; }
    [JsonPropertyName("fightId")] public uint FightId { get; set; }
    [JsonPropertyName("nTeamID")] public uint TeamId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
