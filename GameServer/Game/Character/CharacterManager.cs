using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Game.Character;

public class CharacterManager(PlayerInstance player) : BasePlayerManager(player)
{
    public CharacterData CharacterData { get; } = DatabaseHelper.GetInstanceOrCreateNew<CharacterData>(player.Uid);
    public async ValueTask<CharacterInfo?> AddCharacter(ItemTypeEnum genre, uint detail, uint particular, uint level = 1, uint star = 1, bool sendPacket = true)
    {
        var characterId = GameResourceTemplateId.FromGdpl((uint)genre,detail,particular,1);
        if (CharacterData.Characters.Any(a => a.TemplateId == characterId)) return null;
        var CharacterExcel = GameData.CardData.Values.FirstOrDefault(x => x.Genre == (int)genre && x.Detail == detail && x.Particular == particular);
        if (CharacterExcel == null) return null;

        var character = new CharacterInfo
        {
            Guid = CharacterData.NextCharacterGuid++,
            TemplateId = characterId,
            Level = level,
            Break = star,
            Timestamp = Extensions.GetUnixSec()
        };

        Player.RikiManager.UnlockItem(genre, detail, particular, level);

        var weaponInfo = await Player.InventoryManager!.AddWeaponItem((ItemTypeEnum)CharacterExcel.DefaultWeaponGPDL[0], CharacterExcel.DefaultWeaponGPDL[1], CharacterExcel.DefaultWeaponGPDL[2], CharacterExcel.DefaultWeaponGPDL[3],sendPacket: true);
        if (weaponInfo != null) character.WeaponUniqueId = weaponInfo.UniqueId;

        var skinInfo = Player.InventoryManager!.GetSkinItemGDPL(ItemTypeEnum.TYPE_CARD_SKIN, detail, particular, level)
              ?? await Player.InventoryManager!.AddSkinItem(ItemTypeEnum.TYPE_CARD_SKIN, detail, particular, level, true);
        if (skinInfo != null) character.SkinId = skinInfo.UniqueId;

        if (sendPacket) await Player.SendPacket(new PacketNtfCallScript([character]));

        CharacterData.Characters.Add(character);
        return character;
    }

    public CharacterInfo? GetCharacter(ulong TemplateId)
    {
        return CharacterData.Characters.Find(Character => Character.TemplateId == TemplateId);
    }

    public CharacterInfo? GetCharacterByGUID(uint guid)
    {
        return CharacterData.Characters.Find(Character => Character.Guid == guid);
    }

    public CharacterInfo? GetCharacterGDPL(ItemTypeEnum genre, int detail, int particular)
    {
        var templateId = GameResourceTemplateId.FromGdpl((uint)genre,(uint)detail,(uint)particular,1);
        return CharacterData.Characters.Find(Character => Character.TemplateId == templateId);
    }

    public void AddExperienceToLineup(uint amount, NtfSyncPlayer sync, uint lineupId = 0)
    {
        if (amount == 0)
            return;

        if (lineupId == 0 || !Player.LineupManager.LineupData.LineupInfo.TryGetValue((int)lineupId, out var formation))
            return;

        var memberIds = new[] { formation.Member1, formation.Member2, formation.Member3 }
            .Where(x => x > 0)
            .Distinct();

        foreach (var memberId in memberIds)
        {
            var character = GetCharacterByGUID(memberId);
            if (character == null)
                continue;

            var cardData = GameData.CardData.Values.FirstOrDefault(x =>
                GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == character.TemplateId);
            if (cardData == null)
                continue;

            var totalExp = (ulong)Math.Max(0, character.Exp) + amount;
            while (character.Level < 80 && GameData.UpgradeExpData.TryGetValue((int)character.Level, out var levelExp))
            {
                var requiredExp = cardData.Color >= 5 ? levelExp.SSRCardNeedExp : levelExp.CardNeedExp;
                if (requiredExp == 0 || totalExp < requiredExp)
                    break;

                totalExp -= requiredExp;
                character.Level++;
            }

            character.Exp = (int)Math.Min(int.MaxValue, totalExp);
            sync.Items.Add(character.ToProto());
        }
    }

    public async ValueTask RepairCharacterWeapons()
    {
        var changed = false;
        var equippedWeaponIds = new HashSet<uint>();

        foreach (var character in CharacterData.Characters)
        {
            var weapon = Player.InventoryManager.GetWeaponItem(character.WeaponUniqueId);
            if (weapon == null)
            {
                var cardData = GameData.CardData.Values.FirstOrDefault(x =>
                    GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == character.TemplateId);
                if (cardData?.DefaultWeaponGPDL.Count >= 4)
                {
                    weapon = await Player.InventoryManager.AddWeaponItem(
                        (ItemTypeEnum)cardData.DefaultWeaponGPDL[0],
                        cardData.DefaultWeaponGPDL[1],
                        cardData.DefaultWeaponGPDL[2],
                        cardData.DefaultWeaponGPDL[3],
                        sendPacket:false);
                    if (weapon != null)
                    {
                        character.WeaponUniqueId = weapon.UniqueId;
                        changed = true;
                    }
                }
            }

            if (weapon == null)
                continue;

            if (weapon.EquipAvatarId != character.Guid)
            {
                weapon.EquipAvatarId = character.Guid;
                changed = true;
            }

            equippedWeaponIds.Add(weapon.UniqueId);
        }

        foreach (var weapon in Player.InventoryManager.InventoryData.Weapons.Values)
        {
            if (!equippedWeaponIds.Contains(weapon.UniqueId) && weapon.EquipAvatarId != 0)
            {
                weapon.EquipAvatarId = 0;
                changed = true;
            }
        }

        if (!changed)
            return;

        DatabaseHelper.SaveDatabaseType(CharacterData);
        DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
    }
}
