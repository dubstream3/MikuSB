using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_Equip")]
public class SupporterCard_Equip : CallGSHandler<SupporterCardEquipParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardEquipParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.CardId == 0 || req.SupportCardUid == 0)
        {
            return Task.FromResult(CallGSResult.Ok("{}", "Logistics_Equip"));
        }

        var card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
        if (card == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}", "Logistics_Equip"));
        }

        var teamIndex = card.SupportTeamIndex;
        var slot = GetTeamIndex((uint)req.EqSlot, teamIndex);

        // If an existing card is equipped in this slot and bForce is false, ask for confirmation
        if (!req.Force && req.CurrentEquippedUid != 0 && card.SupportSlots.TryGetValue(slot, out var existing) && existing != 0)
        {
            return Task.FromResult(CallGSResult.Ok("{}", "Logistics_Confirm"));
        }

        // Perform equip
        card.SupportSlots[slot] = (uint)req.SupportCardUid;

        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var sync = new NtfSyncPlayer();
        sync.Items.Add(card.ToProto());

        // Req_EquipChange (no Model) → Logistics_Change; Req_Equip (has Model) → Logistics_Equip
        var responseApi = string.IsNullOrEmpty(req.Model) ? "Logistics_Change" : "Logistics_Equip";
        return Task.FromResult(CallGSResult.Ok("{}", sync, responseApi));
    }

    private uint GetTeamIndex(uint slot, uint teamIndex)
    {
        if (teamIndex == 1) return slot;
        if (teamIndex == 2) return slot + 7;
        if (teamIndex == 3) return slot + 10;
        return slot;
    }
}

public sealed class SupporterCardEquipParam
{
    [JsonPropertyName("EqId")]
    public int CardId { get; set; }

    [JsonPropertyName("beEqId")]
    public int SupportCardUid { get; set; }

    [JsonPropertyName("EqSlot")]
    public int EqSlot { get; set; }

    [JsonPropertyName("BEqId")]
    public int CurrentEquippedUid { get; set; }

    [JsonPropertyName("bForce")]
    public bool Force { get; set; }

    [JsonPropertyName("Model")]
    public string? Model { get; set; }
}
