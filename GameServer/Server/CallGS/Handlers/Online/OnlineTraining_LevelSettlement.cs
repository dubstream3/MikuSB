using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 联机-单机训练关卡 (online training) 通关结算.
//
// Client origin: OnlineTraining:Req_LevelSettlement (Launch/Online/OnlineTraining.lua:146-176) builds
// {nID, nSeed, tbLog, tbKill, tbMonster, nDestructibleNum} and hands it to Reconnect.Send_SettleInfo
// (:172), so it arrives as Chapter_DealLevelSettlement with sCmd = "OnlineTraining_LevelSettlement" and
// never hits a direct CallGS API. The shared reconnect wrapper re-sends for up to five attempts, so this
// branch has to answer with a payload rather than NoResponse.
//
// The wrapper callback (OnlineTraining.lua:178-181) closes the overlay and calls Launch.Response, which
// dispatches to the launch class (Launch/Classes/OnlineTrainingLogic.lua:32-35):
//     Launch.tbAward = tbData or {}
//     UI.OpenWithCallback("Success", ...)
// i.e. the whole payload becomes the award list of the success window; an empty object is what the other
// level families send for a cleared run (e.g. VirCaptureTower_LevelSettlement), and no field of it is
// required for the panel to open.
//
// The counter itself is the only state: the client reads OnlineTrainingLevel.GetPassTime as
// me:GetAttribute(OnlineTrainingLevel.GID = 24, level id) (OnlineTrainingLevel.lua:11, :47-49) and decides
// IsFirstPass / IsPass / IsCompleted from it (OnlineTrainingLevel.lua:42-44, :58-61, :77-80). The client
// never calls its own AddPassTime (the only definition is OnlineTrainingLevel.lua:52-56 with no caller),
// so nothing but the server will ever mark the level as cleared - which is why every training level kept
// offering its first-clear drop. Only a cleared run reaches OnlineTraining:Req_LevelSettlement
// (OnlineTrainingLogic.lua:26-40 sends Req_LevelFail otherwise), so this branch counts unconditionally.
internal static class OnlineTrainingLevelSettlement
{
    private static readonly Logger Logger = new("OnlineTraining");

    /// <summary>OnlineTrainingLevel.GID (OnlineTrainingLevel.lua:11): group 24, one slot per level id.</summary>
    private const uint LevelPassGroupId = AttrIds.Online.ActivityLevelGid;

    /// <summary>
    /// OnlineTrainingLevel.lua:13 documents the slot layout: "第0-7位存放星级达成Flag，第8位存放首通Flag".
    /// Only the clear flag is knowable here (the payload carries no star fields), and OR-ing it in leaves
    /// the star bits the client may set alone - the same convention as the tower family's PassedFlagBit.
    /// </summary>
    private const uint FirstPassFlagBit = 1u << 8;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        if (levelId == 0)
        {
            Logger.Info(
                $"Online training settlement for uid={player.Uid} carried no level id; nothing written.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), null));
        }

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
        attr.Val |= FirstPassFlagBit;
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Online training level {levelId} cleared for uid={player.Uid}; slot " +
            $"(gid={LevelPassGroupId} sid={levelId}) is now {attr.Val} (first-pass flag set).");

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), sync));
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
