using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

// Swaps the weapons equipped by two character cards (Script/Item/Weapon.lua:1367-1374
// Weapon.Req_Exchange -> me:CallGS("Weapon_Exchange", {CardIdA, CardIdB})). The callback
// (Weapon.lua:1376) only closes the spinner and tells the Role UI's weapon switcher to re-read its
// state, so the payload can stay empty - but both cards have to be present in the sync or the UI
// keeps showing the weapons on the cards they were taken from.
// A card with no weapon is a valid participant: it simply receives the other card's weapon, which is
// the normal way to move a weapon from a benched character to an active one.
[CallGSApi("Weapon_Exchange")]
public class Weapon_Exchange : CallGSHandler<WeaponExchangeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponExchangeParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.CardIdA == 0 || req.CardIdB == 0 || req.CardIdA == req.CardIdB)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var cardA = player.CharacterManager.GetCharacterByGUID((uint)req.CardIdA);
        var cardB = player.CharacterManager.GetCharacterByGUID((uint)req.CardIdB);

        if (cardA == null || cardB == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var weaponA = cardA.WeaponUniqueId == 0 ? null : player.InventoryManager.GetWeaponItem(cardA.WeaponUniqueId);
        var weaponB = cardB.WeaponUniqueId == 0 ? null : player.InventoryManager.GetWeaponItem(cardB.WeaponUniqueId);

        cardA.WeaponUniqueId = weaponB?.UniqueId ?? 0;
        cardB.WeaponUniqueId = weaponA?.UniqueId ?? 0;

        // EquipAvatarId is what the bag and the weapon list use to show "equipped by", so it has to
        // follow the weapon to its new owner (Weapon_Replace.cs:23 does the same for one card).
        if (weaponB != null)
            weaponB.EquipAvatarId = cardA.Guid;

        if (weaponA != null)
            weaponA.EquipAvatarId = cardB.Guid;

        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var sync = new NtfSyncPlayer
        {
            Items = { cardA.ToProto(), cardB.ToProto() }
        };

        Logger.GetByClassName().Info(
            $"Weapon exchange between cards {cardA.Guid} and {cardB.Guid} for uid={player.Uid} " +
            $"(weapons {weaponA?.UniqueId ?? 0} <-> {weaponB?.UniqueId ?? 0}).");

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class WeaponExchangeParam
{
    [JsonPropertyName("CardIdA")] public int CardIdA { get; set; }
    [JsonPropertyName("CardIdB")] public int CardIdB { get; set; }
}
