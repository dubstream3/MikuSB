using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rmol;

/// <summary>
/// DLC 绘梦沙盘（RMOL）：一局结束后的记录写入。
///
/// 客户端的解锁链完全建立在一组属性上：<c>DLC_RMOL.lua</c> 里 <c>LevelGID = 173</c>，
/// <c>DLC_RMOL_Level:IsPassed()</c> 读 <c>me:GetAttribute(173, 关卡ID)</c> 并用
/// <c>GetBits(nRecord, 31, 31) &gt; 0</c> 判「已通关」（GetBits 是 0 基），挑战关的星星是第 0..2 位；
/// <c>DLC_RMOL_Level:Available()</c> 再用 <c>Condition.Check(cfg.Condition)</c> 里的
/// <c>RMOL_LEVEL_FINISHED</c> 去看上一关那一位 —— 所以「第一关打完第二关还锁着」的唯一原因就是
/// 结算时没人写这个属性（本 handler 之前只回 Awards/ExpGain，什么都没落库）。
/// </summary>
[CallGSApi("DLC_RMOL_LevelEnd")]
public class DLC_RMOL_LevelEnd : CallGSHandler<DlcRmolLevelEndParam>
{
    private static readonly Logger Log = new("DLC_RMOL");

    /// <summary>通关记录位（0 基，与客户端 <c>GetBits(nRecord, 31, 31)</c> 对应）。</summary>
    private const int PassedBit = 31;

    /// <summary>首通成长经验；重复通关回 0，客户端据此显示「首通奖励已领」而不是「经验已达上限」。</summary>
    private const int FirstClearExpGain = 30;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DlcRmolLevelEndParam req)
    {
        if (req == null || req.Level <= 0)
        {
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["Err"] = "error.BadParam" }.ToJsonString()));
        }

        var player = context.Connection?.Player;
        if (player == null)
        {
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["Err"] = "error.NotLogin" }.ToJsonString()));
        }

        var sync = new NtfSyncPlayer();
        var record = player.Attributes.GetOrCreate(AttrIds.Dlc.RmolLevelGroupId, (uint)req.Level);
        var before = record.Val;
        var alreadyPassed = ((before >> PassedBit) & 1u) != 0;

        var value = before;
        if (req.IsVictory)
            value |= 1u << PassedBit;

        for (var i = 0; i < req.Stars.Count && i < 3; i++)
        {
            if (req.Stars[i])
                value |= 1u << i;
        }

        if (value != before)
        {
            record.Val = value;
            player.Attributes.SyncTo(sync, record);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        var expGain = req.IsVictory && !alreadyPassed ? FirstClearExpGain : 0;

        Log.Info($"DLC_RMOL_LevelEnd: uid={player.Uid} level={req.Level} victory={req.IsVictory} " +
                 $"passed={(alreadyPassed ? "already" : req.IsVictory ? "new" : "no")} record=0x{value:X8} exp={expGain}.");

        var response = new JsonObject
        {
            ["Level"] = req.Level,
            ["Awards"] = new JsonArray(),
            ["ExpGain"] = expGain
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }
}

public sealed class DlcRmolLevelEndParam
{
    [JsonPropertyName("Level")]
    public int Level { get; set; }

    [JsonPropertyName("Session")]
    public int Session { get; set; }

    [JsonPropertyName("IsVictory")]
    public bool IsVictory { get; set; }

    [JsonPropertyName("Stars")]
    public List<bool> Stars { get; set; } = [];
}
