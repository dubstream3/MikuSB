using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Replace")]
public class Weapon_Replace : CallGSHandler<WeaponReplaceParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponReplaceParam req)
    {
        var player = context.Connection.Player!;

        var cardData = req == null ? null : player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
        var newWeapon = req == null ? null : player.InventoryManager.GetWeaponItem((uint)req.Id);

        if (cardData != null && newWeapon != null)
        {
            var oldWeaponId = cardData.WeaponUniqueId;
            var oldWeapon = oldWeaponId == 0 ? null : player.InventoryManager.GetWeaponItem(oldWeaponId);

            cardData.WeaponUniqueId = newWeapon.UniqueId;
            newWeapon.EquipAvatarId = cardData.Guid;

            if (oldWeapon != null && oldWeapon.UniqueId != newWeapon.UniqueId)
            {
                oldWeapon.EquipAvatarId = 0;
            }

            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

            var sync = new NtfSyncPlayer
            {
                Items = { cardData.ToProto() }
            };
            return Task.FromResult(CallGSResult.Ok("{}", sync));
        }

        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}

public sealed class WeaponReplaceParam
{
    public int CardId { get; set; }
    public int Id { get; set; }
}
