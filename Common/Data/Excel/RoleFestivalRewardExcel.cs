using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 心愿寄语 (Dorm / RoleFestival) reward rows from house/MessageActivity/ActivityRewards.json.
///
/// One row per reward tier: MessageNum is the number of letters that must have been read before the
/// tier can be claimed (the act 3 rows therefore sit on 3/6/9/12/16/21, the cumulative sizes of the
/// six UnlockNum groups in ActivityTime.json), and Rewards holds the usual GDPL rows the client
/// forwards straight into Item.Gain (Activity/RoleFestivalMessage.lua:320).
/// </summary>
[ResourceEntity("house/MessageActivity/ActivityRewards.json")]
public class RoleFestivalRewardExcel : ExcelResource
{
    [JsonProperty("ActivityID")] public JToken? ActivityId { get; set; }
    [JsonProperty("MessageNum")] public JToken? MessageNum { get; set; }
    [JsonProperty("Rewards")] public JToken? Rewards { get; set; }

    /// <summary>Activity the tier belongs to; keyed by MessageNum inside one activity.</summary>
    public uint ActId { get; private set; }

    /// <summary>Read letters required before the tier unlocks (and its claim key).</summary>
    public uint RequiredReads { get; private set; }

    public override uint GetId() => ReadUint(ActivityId) * 1000 + ReadUint(MessageNum);

    public override void Loaded()
    {
        ActId = ReadUint(ActivityId);
        RequiredReads = ReadUint(MessageNum);

        if (ActId == 0 || RequiredReads == 0 || RequiredReads > 999)
            return;

        GameData.RoleFestivalRewardData[GetId()] = this;
    }

    /// <summary>
    /// Tolerant number read: the shipped tables mix numbers and strings ("3", "[3]", "").
    /// </summary>
    private static uint ReadUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        try
        {
            return token.Type switch
            {
                JTokenType.Integer => token.Value<uint>(),
                JTokenType.Float => (uint)token.Value<double>(),
                _ => uint.TryParse(token.ToString().Trim().Trim('[', ']', '"'), out var parsed) ? parsed : 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
