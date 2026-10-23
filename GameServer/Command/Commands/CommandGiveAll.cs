using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("giveall", "Game.Command.GiveAll.Desc", "Game.Command.GiveAll.Usage", ["ga"], [PermEnum.Admin, PermEnum.Support])]
public class CommandGiveAll : ICommands
{
    [CommandMethod("weapon")]
    public async ValueTask GiveAllWeapon(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;

        var detail = arg.GetInt(0);
        level = Math.Clamp(level, 1, 90);
        var player = arg.Target!.Player!;
        List<GameWeaponInfo> weapons = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.WeaponData.Values)
            {
                var weapon = await player.InventoryManager!
                    .AddWeaponItem((ItemTypeEnum)config.Genre,config.Detail,config.Particular,config.Level,(uint)level,false);
                if (weapon != null) weapons.Add(weapon);
            }
        }
        else
        {
            var weapon = await player.InventoryManager!.AddWeaponItem(ItemTypeEnum.TYPE_WEAPON, (uint)detail,(uint)particular,1,(uint)level,false);
            if (weapon == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Weapon")));
                return;
            }
            weapons.Add(weapon);
        }
        if (weapons.Count > 0)
        {
            await player.SendPacket(new PacketNtfCallScript(weapons));
            await player.SendPacket(new PacketNtfCallScript(player));
            DatabaseHelper.SaveDatabaseType(player.Data);
        }
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Weapon"), weapons.Count.ToString()));
    }

    [CommandMethod("card")]
    public async ValueTask GiveAllSupportCard(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<GameSupportCardInfo> supportCards = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.SupportCardData)
            {
                var supportCard = await player.InventoryManager!
                    .AddSupportCardItem(config.Detail, config.Particular, config.Level, (uint)level, false);
                if (supportCard != null) supportCards.Add(supportCard);
            }
        }
        else
        {
            var supportCard = await player.InventoryManager!.AddSupportCardItem((uint)detail, (uint)particular, 1, (uint)level, false);
            if (supportCard == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.SupportCard")));
                return;
            }
            supportCards.Add(supportCard);
        }
        if (supportCards.Count > 0) await player.SendPacket(new PacketNtfCallScript(supportCards));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.SupportCard"), supportCards.Count.ToString()));
    }

    [CommandMethod("weaponskin")]
    public async ValueTask GiveAllWeaponSkin(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<BaseGameItemInfo> weaponSkins = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.WeaponSkinData.Values)
            {
                var weaponSkin = await player.InventoryManager!
                    .AddWeaponSkinItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (weaponSkin != null) weaponSkins.Add(weaponSkin);
            }
        }
        else
        {
            var weaponSkin = await player.InventoryManager!.AddWeaponSkinItem(ItemTypeEnum.TYPE_WEAPON, (uint)detail, (uint)particular, 1, false);
            if (weaponSkin == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.WeaponSkin")));
                return;
            }
            weaponSkins.Add(weaponSkin);
        }
        if (weaponSkins.Count > 0) await player.SendPacket(new PacketNtfCallScript(weaponSkins));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.WeaponSkin"), weaponSkins.Count.ToString()));
    }

    [CommandMethod("profile")]
    public async ValueTask GiveAllProfile(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;
        if (await arg.GetOption('g') is not int genre) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<BaseGameItemInfo> profileItems = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.ProfileData.Values)
            {
                var profile = await player.InventoryManager!
                    .AddProfileItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (profile != null) profileItems.Add(profile);
            }
        }
        else
        {
            var profile = await player.InventoryManager!.AddProfileItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, false);
            if (profile == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Profile")));
                return;
            }
            profileItems.Add(profile);
        }
        if (profileItems.Count > 0) await player.SendPacket(new PacketNtfCallScript(profileItems));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Profile"), profileItems.Count.ToString()));
    }

    [CommandMethod("skinpart")]
    public async ValueTask GiveAllSkinPart(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;
        if (await arg.GetOption('g') is not int genre) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<BaseGameItemInfo> skinPartItems = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.CardSkinPartsData.Values)
            {
                var skinPart = await player.InventoryManager!
                    .AddSkinPartItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (skinPart != null) skinPartItems.Add(skinPart);
            }
        }
        else
        {
            var skinPart = await player.InventoryManager!.AddSkinPartItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, false);
            if (skinPart == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.SkinPart")));
                return;
            }
            skinPartItems.Add(skinPart);
        }
        if (skinPartItems.Count > 0) await player.SendPacket(new PacketNtfCallScript(skinPartItems));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.SkinPart"), skinPartItems.Count.ToString()));
    }

    [CommandMethod("call")]
    public async ValueTask GiveAllCallItem(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;
        if (await arg.GetOption('g') is not int genre) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<BaseGameItemInfo> callItems = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.CallItemData.Values)
            {
                var callItem = await player.InventoryManager!
                    .AddCallItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (callItem != null) callItems.Add(callItem);
            }
        }
        else
        {
            var callItem = await player.InventoryManager!.AddCallItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, false);
            if (callItem == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.CallItem")));
                return;
            }
            callItems.Add(callItem);
        }
        if (callItems.Count > 0) await player.SendPacket(new PacketNtfCallScript(callItems));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.CallItem"), callItems.Count.ToString()));
    }

    [CommandMethod("weaponpart")]
    public async ValueTask GiveAllWeaponPart(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;
        if (await arg.GetOption('g') is not int genre) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<BaseGameItemInfo> weaponPartItems = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.WeaponPartsData.Values)
            {
                var weaponPart = await player.InventoryManager!
                    .AddWeaponPartItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (weaponPart != null) weaponPartItems.Add(weaponPart);
            }
        }
        else
        {
            var weaponPart = await player.InventoryManager!.AddWeaponPartItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, false);
            if (weaponPart == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.WeaponPart")));
                return;
            }
            weaponPartItems.Add(weaponPart);
        }
        if (weaponPartItems.Count > 0) await player.SendPacket(new PacketNtfCallScript(weaponPartItems));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.WeaponPart"), weaponPartItems.Count.ToString()));
    }

    [CommandMethod("skin")]
    public async ValueTask GiveAllSkin(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;
        if (await arg.GetOption('g') is not int genre) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<GameSkinInfo> skinItems = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.CardSkinData.Values)
            {
                var skin = await player.InventoryManager!
                    .AddSkinItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (skin != null) skinItems.Add(skin);
            }
        }
        else
        {
            var skin = await player.InventoryManager!.AddSkinItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, false);
            if (skin == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Skin")));
                return;
            }
            skinItems.Add(skin);
        }
        if (skinItems.Count > 0)
        {
            await player.SendPacket(new PacketNtfCallScript(skinItems));
            await player.SendPacket(new PacketNtfCallScript(player));
            DatabaseHelper.SaveDatabaseType(player.Data);
        }
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Skin"), skinItems.Count.ToString()));
    }

    [CommandMethod("furniture")]
    public async ValueTask GiveAllHouseFurniture(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;
        if (await arg.GetOption('p') is not int particular) return;
        if (await arg.GetOption('l') is not int level) return;
        if (await arg.GetOption('g') is not int genre) return;

        var detail = arg.GetInt(0);
        var player = arg.Target!.Player!;
        List<BaseGameItemInfo> furnitureItems = [];
        if (detail == -1)
        {
            // add all
            foreach (var config in GameData.DormGiftData.Values)
            {
                var furniture = await player.InventoryManager!
                    .AddHouseFurnitureItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, false);
                if (furniture != null) furnitureItems.Add(furniture);
            }
        }
        else
        {
            var furniture = await player.InventoryManager!.AddHouseFurnitureItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, false);
            if (furniture == null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Furniture")));
                return;
            }
            furnitureItems.Add(furniture);
        }
        if (furnitureItems.Count > 0) await player.SendPacket(new PacketNtfCallScript(furnitureItems));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems",
            I18NManager.Translate("Word.Furniture"), furnitureItems.Count.ToString()));
    }
}