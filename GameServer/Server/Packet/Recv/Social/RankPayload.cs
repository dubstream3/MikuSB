using Google.Protobuf;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

// MikuSB has no generated message class for REQ_RANK(53) / REQ_RANKLIST(51): Proto/SnowBreak.cs only
// carries the two *response* shapes (RankList = rank_name + repeated ListItem{member_name,score,info}
// and RankInfo = score/rank/sum/info). The request is therefore decoded field by field: every length
// delimited field is collected as a string, which yields the rank name first and, for REQ_RANK, the
// player id the client appends as the second argument
// (me:QueryRank(name, tostring(me:Id())) -- Script/UMG/WorldBoss/umg_worldboss_rank.lua:143).
internal static class RankPayload
{
    public static List<string> ReadStrings(byte[] data)
    {
        var result = new List<string>();
        if (data.Length == 0)
            return result;

        try
        {
            var input = new CodedInputStream(data);
            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0)
                    break;

                if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
                {
                    var value = input.ReadString();
                    if (!string.IsNullOrEmpty(value))
                        result.Add(value);
                }
                else
                {
                    input.SkipLastField();
                }
            }
        }
        catch
        {
            // Fall back to raw UTF-8 payload handling below.
        }

        if (result.Count == 0)
        {
            try
            {
                var raw = System.Text.Encoding.UTF8.GetString(data).Trim('\0', ' ', '\r', '\n', '\t');
                if (!string.IsNullOrWhiteSpace(raw))
                    result.Add(raw);
            }
            catch
            {
                // ignore
            }
        }

        return result;
    }
}
