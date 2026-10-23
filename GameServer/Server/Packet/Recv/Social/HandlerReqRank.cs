using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

// me:QueryRank(rankName, tostring(me:Id())) asks for the local player's own standing, e.g.
// Script/UMG/WorldBoss/umg_worldboss_rank.lua:143 or Script/UMG/DungeonsSeasonBoss/
// umg_dungeonsseasonboss_main.lua:665 -> RankReward. Several widgets raise a full screen
// "Connection" overlay before the request and only drop it when Event.QueryRank(bok, score, rank,
// sum, des) arrives (Script/Misc/Enums.lua:157), so an unanswered REQ_RANK leaves the UI frozen --
// exactly what "奖励总览点击冥域卡死" was.
//
// MikuSB has no leaderboard backend, so answer with a well formed RankInfo: a single player server
// reports the local player as the only runner (rank 1 of 1) with a placeholder score of 1 point.
// Rank 0 would make rank-reward panels bail out with "not on the board"; every widget we ship only
// needs a non-empty, internally consistent answer.
[Opcode(CmdIds.ReqRank)]
public class HandlerReqRank : Handler
{
    private static readonly Logger Logger = new("ReqRank");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var strings = RankPayload.ReadStrings(data);
        var rankName = strings.Count > 0 ? strings[0] : string.Empty;
        var player = connection.Player;

        Logger.Debug($"ReqRank payload: {Convert.ToHexString(data)}");
        Logger.Info($"Rank query '{rankName}' for uid={player?.Uid ?? 0} ({data.Length} byte(s), {strings.Count} string field(s)).");

        await connection.SendPacket(CmdIds.RspRank, new RankInfo
        {
            Score = 1,
            Rank = 1,
            Sum = 1,
            Info = player?.Data.Name ?? string.Empty
        }, seqNo);
    }
}
