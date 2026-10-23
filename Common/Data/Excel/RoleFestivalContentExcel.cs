using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 心愿寄语 (Dorm / RoleFestival) letter rows from house/MessageActivity/MessageContent.json.
///
/// The client keeps these rows in file order only - "此处没有可用的单独主键，所以key使用顺序排序"
/// (Activity/RoleFestivalMessage.lua:393) - and lists a letter as soon as attribute
/// (gid 72, sid 1000 + NpcID) is 1 (GetMessageOpenId, :271-286). Nothing on the client ever sets
/// that flag, so the server has to know the npc ids of the current act to open them at login.
/// (ActivityID, NpcID) is unique in the shipped table and therefore a usable dictionary key.
/// </summary>
[ResourceEntity("house/MessageActivity/MessageContent.json")]
public class RoleFestivalContentExcel : ExcelResource
{
    [JsonProperty("ActivityID")] public JToken? ActivityId { get; set; }
    [JsonProperty("NpcID")] public JToken? NpcId { get; set; }
    [JsonProperty("Message")] public string? Message { get; set; }
    [JsonProperty("NpcName")] public string? NpcName { get; set; }
    [JsonProperty("ProfileID")] public JToken? ProfileId { get; set; }
    [JsonProperty("EasterEgg")] public JToken? EasterEgg { get; set; }

    /// <summary>Activity the letter belongs to (the value stored in attribute (72, 1)).</summary>
    public uint ActId { get; private set; }

    /// <summary>Npc that wrote the letter; also the offset of its open/read attribute.</summary>
    public uint Npc { get; private set; }

    /// <summary>Non-zero for 彩蛋 letters, which the UI shows without an open flag.</summary>
    public uint IsEasterEgg { get; private set; }

    public override uint GetId() => ReadUint(ActivityId) * 1000 + ReadUint(NpcId);

    public override void Loaded()
    {
        ActId = ReadUint(ActivityId);
        Npc = ReadUint(NpcId);
        IsEasterEgg = ReadUint(EasterEgg);

        // Npc ids above 999 would collide with the reward rows in the same id space.
        if (ActId == 0 || Npc == 0 || Npc > 999)
            return;

        GameData.RoleFestivalContentData[GetId()] = this;
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
