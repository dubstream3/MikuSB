using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 发一件指定道具（材料、消耗品等 supplies 模板）。/giveall 只有固定几个子命令，之前想发任意 supplies 条目
/// 只能走 /attr 或 /lua，控制台命令表里补这一个：
///
///   /item 1001        一件，默认 genre 5 / particular 1 / level 1
///   /item 1001 -c99   99 件
///   /item 4 -p4       换 particular（-g/-l 同理）
/// </summary>
/// <remarks>
/// 选项从 <see cref="CommandArg.Attributes"/> 取（形如 "c99"，'-' 已被 CommandManager 剥掉）。
/// <see cref="CommandArg.GetOption"/> 是从 Args 里按首字母找，而 Args 里仍留着 "-c99"，所以带 '-' 的写法它
/// 永远拿不到值——新命令不要再依赖它。
/// </remarks>
[CommandInfo("item", "Game.Command.Item.Desc", "Game.Command.Item.Usage", ["it"], [PermEnum.Admin, PermEnum.Support])]
public class CommandItem : ICommands
{
    [CommandDefault]
    public async ValueTask Give(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;

        var detail = arg.GetInt(0);
        if (detail <= 0)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Item.Usage"));
            return;
        }

        var genre = Option(arg, 'g', 5);
        var particular = Option(arg, 'p', 1);
        var level = Option(arg, 'l', 1);
        var count = Option(arg, 'c', 1);

        var templateId = GameResourceTemplateId.FromGdpl(genre, (uint)detail, particular, level);
        if (!GameData.SuppliesData.TryGetValue(templateId, out var config))
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Item.NotFound", templateId.ToString()));
            return;
        }

        var player = arg.Target!.Player!;
        var item = await player.InventoryManager!.AddSuppliesItem(config, count, false);
        if (item == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Item.NotFound", templateId.ToString()));
            return;
        }

        List<BaseGameItemInfo> items = [item];
        await player.SendPacket(new PacketNtfCallScript(items));
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        await arg.SendMsg(I18NManager.Translate("Game.Command.Item.Added", count.ToString(), templateId.ToString()));
    }

    /// <summary>-c99 这类选项；CommandManager 填 Attributes 时已去掉前导 '-'。</summary>
    private static uint Option(CommandArg arg, char pre, uint fallback)
    {
        foreach (var attribute in arg.Attributes)
            if (attribute.Length > 1 && attribute[0] == pre &&
                uint.TryParse(attribute[1..], out var value) && value > 0)
                return value;
        return fallback;
    }
}
