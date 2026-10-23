using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

/// <summary>
/// 防御活动 (defend) mid-run checkpoint. <c>DefendLogic.SaveFightData</c> (Challenge/Defend/Defend.lua:763)
/// is driven by the "save defend info" game task (Task/GameTaskExectue/SaveDefendInfo.lua:14); the payload
/// goes out over the shared reconnect channel (Reconnect.Send_SettleInfo, Defend.lua:793) as
/// Chapter_DealLevelSettlement with sCmd = "DefendLogic_SaveFightData", so the direct CallGS API is never
/// hit by the live client.
///
/// The registered callback (Defend.lua:967-974) reads nothing from the reply: it closes the connection
/// overlay and then runs the task callback, so an empty object is the whole answer.
///
/// <c>tbData</c> is the run's fight state (Defend.lua:44-56 tbFightDataTpl: Device/Money/Wave/
/// ExTimeWaves/PlayerHp/MonsterWave/NpcSpawnDeviceData/NpcDevicePoint/TurretBullet/MultiKill/Modifier).
/// The client reads it back through <c>me:GetStrAttribute(DefendLogic.GID = 10, DefendLogic.FightData = 1)</c>
/// (Defend.lua:405-428 GetFightData / IsInFight) and restores it in LoadDefendInfo.lua:30-61, so storing it
/// here is what makes a disconnect or a restart resume the wave instead of falling back to the template.
/// The slot is cleared again by DefendSettlement when the run settles - the client's manual reset
/// (Defend.lua:578-592 ClearFightData -> DefendLogic_ClearFightData) is commented out in the shipped
/// scripts, so nothing else would ever drop a finished run's checkpoint.
///
/// The 图鉴 monster kills carried in <c>tbMonster</c> are still credited centrally for every sCmd by
/// Chapter_DealLevelSettlement (RikiUnlockService), so they are deliberately not repeated here.
/// </summary>
internal static class DefendFightSave
{
    private static readonly Logger Logger = new("Defend");

    /// <summary>DefendLogic.FightData (Defend.lua:34): the string slot holding the fight checkpoint.</summary>
    private const uint FightDataSid = 1;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSaveFightDataAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var diffId = ReadUint(tbParam, "nDiff");
        var seed = ReadUint(tbParam, "nSeed");
        var logSize = (tbParam as JsonObject)?["tbLog"] is JsonArray log ? log.Count : 0;
        var monsterCount = (tbParam as JsonObject)?["tbMonster"] is JsonArray monsters ? monsters.Count : 0;
        var checkpoint = (tbParam as JsonObject)?["tbData"]?.ToJsonString();

        if (string.IsNullOrEmpty(checkpoint))
        {
            Logger.Info(
                $"Defend checkpoint for uid={player.Uid}: level={levelId} diff={diffId} seed={seed} " +
                $"logRows={logSize} monsters={monsterCount} carried no tbData; nothing stored.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), null));
        }

        var attr = player.Attributes.SetString(AttrIds.Defend.Gid, FightDataSid, checkpoint);
        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Defend checkpoint for uid={player.Uid}: level={levelId} diff={diffId} seed={seed} " +
            $"logRows={logSize} monsters={monsterCount}; stored {checkpoint.Length} chars at " +
            $"(gid={AttrIds.Defend.Gid}, sid={FightDataSid}).");

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
