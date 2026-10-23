using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

// me:RankList(rankName) fetches the leaderboard itself, after (or instead of) the own-rank query,
// e.g. Script/UMG/WorldBoss/umg_worldboss_rank.lua:41 and Script/UMG/Lucky/umg_lucky_rank.lua:72.
// The client only accepts the answer when the echoed rank_name matches the requested one
// (umg_worldboss_rank.lua:22), so RankName is copied verbatim from the request.
//
// RankList.Types.ListItem.MemberName carries the *player id as a string* -- consumers do
// tonumber(RankInfo.MemberName) (umg_worldboss_rank.lua:181, umg_lucky_rank.lua:32,
// umg_dungeonsboss_main.lua:359) and compare it with me:Id(). MikuSB only ever has the local player,
// so the list contains exactly that one entry with the placeholder score of one point.
[Opcode(CmdIds.ReqRankList)]
public class HandlerReqRankList : Handler
{
    private static readonly Logger Logger = new("ReqRankList");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var strings = RankPayload.ReadStrings(data);
        var rankName = strings.Count > 0 ? strings[0] : string.Empty;
        var player = connection.Player;

        Logger.Debug($"ReqRanklist payload: {Convert.ToHexString(data)}");
        Logger.Info($"Rank list '{rankName}' for uid={player?.Uid ?? 0} ({data.Length} byte(s), {strings.Count} string field(s)).");

        var list = new RankList { RankName = rankName };
        if (player != null && TryBuildChessFansRank(player, list))
        {
            await connection.SendPacket(CmdIds.RspRankList, list, seqNo);
            return;
        }

        if (player != null)
        {
            list.List.Add(new RankList.Types.ListItem
            {
                MemberName = player.Uid.ToString(),
                Score = 1,
                Info = player.Data.Name ?? string.Empty
            });
        }

        await connection.SendPacket(CmdIds.RspRankList, list, seqNo);
    }

    // 大富翁应援榜 (RichmanLogic.GetRankList -> me:RankList("RichmanGirlFansRank"),
    // RichmanLogic.lua:356-360). umg_richman_fansmain.lua:14-31 tonumber()s every MemberName and uses
    // it as an npc index into the 02_npcs table (1..8), so the board must list npc ids -- not uids --
    // and must never come back empty, otherwise the panel renders eight blank rows. Votes live in
    // (118, 2*id+1); a player who has not voted yet still needs a rank-able row, hence the 1 point floor
    // that also matches the placeholder score used everywhere else in this handler.
    private static bool TryBuildChessFansRank(PlayerInstance player, RankList list)
    {
        if (!string.Equals(list.RankName, "RichmanGirlFansRank", StringComparison.Ordinal))
            return false;

        var rows = new List<(uint NpcId, uint Score)>();
        for (uint npc = 1; npc <= 8; npc++)
        {
            var votes = ChessActivityService.Votes(player, npc);
            rows.Add((npc, votes == 0 ? 1 : votes));
        }

        foreach (var row in rows.OrderByDescending(r => r.Score).ThenBy(r => r.NpcId))
        {
            list.List.Add(new RankList.Types.ListItem
            {
                MemberName = row.NpcId.ToString(),
                Score = row.Score,
                Info = string.Empty
            });
        }

        return true;
    }
}
