using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Send.Misc;

public class PacketNtfCallScript : BasePacket
{
    // Every constructor below is a pure sync packet: the payload lives in ExtraSync and there is no
    // script to run. The client's native NtfScript handler still calls s2c.Dispatch(Api, Arg), so an
    // empty Api made Misc/S2C.lua:32 raise "s2c.Dispatch error. No command named: " on every
    // login/reconnect. Answer with a name the client registers as an empty no-op
    // (Achievement/Achievement_Extend.lua:347) so the dispatch succeeds and ExtraSync is still
    // applied.
    private const string SyncApi = "AchievementExtend_RefreshPopFlag";

    public PacketNtfCallScript(List<CharacterInfo> characters) : base(CmdIds.NtfScript)
    {
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}",
            ExtraSync = new NtfSyncPlayer
            {
                Items = { characters.Select(x => x.ToProto()) }
            }
        };

        SetData(proto);
    }

    public PacketNtfCallScript(List<GameWeaponInfo> weapons) : base(CmdIds.NtfScript)
    {
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}",
            ExtraSync = new NtfSyncPlayer
            {
                Items = { weapons.Select(x => x.ToProto()) }
            }
        };

        SetData(proto);
    }

    public PacketNtfCallScript(List<BaseGameItemInfo> items) : base(CmdIds.NtfScript)
    {
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}",
            ExtraSync = new NtfSyncPlayer
            {
                Items = { items.Select(x => x.ToProto()) }
            }
        };

        SetData(proto);
    }

    public PacketNtfCallScript(List<GameSupportCardInfo> cards) : base(CmdIds.NtfScript)
    {
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}",
            ExtraSync = new NtfSyncPlayer
            {
                Items = { cards.Select(ToSupportCardProto) }
            }
        };

        SetData(proto);
    }

    public PacketNtfCallScript(List<GameSkinInfo> skins) : base(CmdIds.NtfScript)
    {
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}",
            ExtraSync = new NtfSyncPlayer
            {
                Items = { skins.Select(x => x.ToProto()) }
            }
        };

        SetData(proto);
    }

    public PacketNtfCallScript(InventoryData inventory) : base(CmdIds.NtfScript)
    {
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}"
        };

        var extraSync = new NtfSyncPlayer();
        foreach (var item in inventory.Items.Values) extraSync.Items.Add(item.ToProto());
        foreach (var skin in inventory.Skins.Values) extraSync.Items.Add(skin.ToProto());
        foreach (var weapon in inventory.Weapons.Values) extraSync.Items.Add(weapon.ToProto());
        foreach (var supportCard in inventory.SupportCards.Values) extraSync.Items.Add(ToSupportCardProto(supportCard));
        proto.ExtraSync = extraSync;
        SetData(proto);
    }

    public PacketNtfCallScript(PlayerInstance Player) : base(CmdIds.NtfScript)
    {
        Player.BuildPlayerAttr(true);
        var proto = new NtfCallScript
        {
            Api = SyncApi,
            Arg = "{}"
        };
        var sync = new NtfSyncPlayer();
        foreach (var attr in Player.Attributes.All)
            Player.Attributes.SyncTo(sync, attr);
        Player.QuestManager.SyncTestingStateTo(sync);
        foreach (var (key, value) in Player.BuildMoneySync())
            sync.Money[key] = value;
        proto.ExtraSync = sync;

        SetData(proto);
    }

    private static Item ToSupportCardProto(GameSupportCardInfo card)
    {
        SupportAffixStateService.NormalizePendingState(card);
        var proto = card.ToProto();
        proto.Slots[(uint)ItemSupportCardSlotTypeEnum.SLOT_AFFIXINDEX] = SupportAffixStateService.GetVisibleInitialAffixIndex(card);
        return proto;
    }
}
