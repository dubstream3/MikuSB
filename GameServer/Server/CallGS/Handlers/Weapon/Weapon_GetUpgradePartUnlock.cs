using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

// Claim button of the "special part" widget in the arms panel
// (UMG/Arms/Widgets/uw_arms_partslock.lua:15).
// Client contract (Item/Weapon.lua:1513 Req_GetUpgradePartUnlock + the s2c.Register at 1521):
// request {"gdpl":[g,d,p,l], "weapon":"<weapon g-d-p-l>", "nWeaponId":<inventory unique id>},
// and the callback reads its single argument as an error tip, so a success reply has to leave it
// nil. Weapon_ReplacePart is the same shape and answers the literal "null"; mirror that here.
[CallGSApi("Weapon_GetUpgradePartUnlock")]
public class Weapon_GetUpgradePartUnlock : CallGSHandler<WeaponGetUpgradePartUnlockParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, WeaponGetUpgradePartUnlockParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.WeaponId <= 0 || req.PartGdpl == null || req.PartGdpl.Count < 4)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var weapon = player.InventoryManager.GetWeaponItem((uint)req.WeaponId);
        if (weapon == null)
        {
            return CallGSResult.Error("error.BadParam");
        }

        // Only the part this very weapon awards at its max level can be claimed through it.
        var award = WeaponPartAward.GetAward(weapon.TemplateId);
        if (award == null || !award.Take(4).SequenceEqual(req.PartGdpl.Take(4)))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var config = WeaponUpgradeConfig.Load();
        if (!config.TryGetWeaponTemplate(weapon.TemplateId, out var template))
        {
            return CallGSResult.Error("error.BadParam");
        }

        // Award unlock level (the cap of the weapon's initial break stage, 30 in the live table),
        // not the cap of the stage it currently sits at - see WeaponPartAward.AwardUnlockBreak.
        var unlockLevel = WeaponPartAward.GetAwardUnlockLevel(config, template.BreakLimitId);
        if (unlockLevel == 0 || weapon.Level < unlockLevel)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var sid = WeaponPartAward.GetSid(award);
        if (WeaponPartAward.IsClaimed(player, sid))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var sync = new NtfSyncPlayer();

        // Weapons that already sat at the cap before this line existed have no unlock bit yet,
        // so set it here as well instead of relying on Weapon_Upgrade having done it.
        WeaponPartAward.MarkUnlocked(player, sid, sync);

        var rows = new List<IReadOnlyList<uint>>
        {
            new uint[] { (uint)award[0], (uint)award[1], (uint)award[2], (uint)award[3], 1 }
        };
        await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync).ConfigureAwait(false);

        // Claimed even when the part was already in the bag: AddWeaponPartItem skips duplicates
        // and hands back nothing, which would otherwise keep the button glowing forever.
        WeaponPartAward.MarkClaimed(player, sid, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        return CallGSResult.Ok("null", sync);
    }
}

public sealed class WeaponGetUpgradePartUnlockParam
{
    [JsonPropertyName("gdpl")]
    public List<int> PartGdpl { get; set; } = [];

    [JsonPropertyName("weapon")]
    public string WeaponGdpl { get; set; } = "";

    [JsonPropertyName("nWeaponId")]
    public int WeaponId { get; set; }
}
