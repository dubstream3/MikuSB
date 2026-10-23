using MikuSB.Data;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

// Direct lever for the in-game currencies the client reads through Cash.GetMoneyCount, i.e. attribute
// group 1 (sid = moneyType * 2 + 1), tid 4 being the Vigor player field instead. Tokens are accepted
// as the numeric id or as a name from cash/cash.json: /money get silver, /money add 3 100000 @4,
// /money set gold 5000 @4, /money list, /money spend -a 1000 @4.
[CommandInfo("money", "Read, set or hand out in-game currencies.", "Usage: /money <get|set|add|spend> <id|name> [value] [-a] [@uid] | /money list [@uid] | /money seed [@uid] | /money reset [@uid]", ["cash", "m"], [PermEnum.Admin, PermEnum.Support])]
public class CommandMoney : ICommands
{
    /// <summary>Cash.GetMoneyCount can address money ids 1..200; 4 is the Vigor player field.</summary>
    private const uint MaxMoneyType = 200;

    [CommandMethod("get")]
    public async ValueTask Get(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(1))
            return;
        if (!await arg.CheckOnlineTarget()) return;

        if (!CurrencyService.TryResolve(arg.Args[0], out var moneyType))
        {
            await arg.SendMsg($"Unknown currency '{arg.Args[0]}'. Use /money list to see the known ids.");
            return;
        }

        var player = arg.Target!.Player!;
        await arg.SendMsg($"uid={player.Data.Uid} {CurrencyService.Describe(moneyType)} = {CurrencyService.GetBalance(player, moneyType)}.");
    }

    [CommandMethod("set")]
    public async ValueTask Set(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(2))
            return;
        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var value = (uint)Math.Max(0, arg.GetInt(1));
        var sync = new NtfSyncPlayer();

        if (IsAll(arg, "set"))
        {
            var touched = 0;
            // Only the currencies this account already carries: "set all to N" has no meaning for a
            // money id that does not exist yet, and creating all 200 would re-inflate the wallet.
            foreach (var (moneyType, _) in ExistingCurrencies(player))
            {
                CurrencyService.SetBalance(player, moneyType, value, sync);
                touched++;
            }

            if (touched == 0)
            {
                await arg.SendMsg("No currency is seeded yet; run /money seed first.");
                return;
            }

            await player.SendPacket(CmdIds.NtfSyncAttr, sync);
            CurrencyService.Persist(player);
            await arg.SendMsg($"Set {touched} currency balance(s) to {value} for uid={player.Data.Uid}.");
            return;
        }

        if (!CurrencyService.TryResolve(arg.Args[0], out var single))
        {
            await arg.SendMsg($"Unknown currency '{arg.Args[0]}'. Use /money list to see the known ids.");
            return;
        }

        CurrencyService.SetBalance(player, single, value, sync);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        CurrencyService.Persist(player);
        await arg.SendMsg($"{CurrencyService.Describe(single)} = {value} for uid={player.Data.Uid}.");
    }

    [CommandMethod("add")]
    public async ValueTask Add(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(2))
            return;
        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var amount = (uint)Math.Max(0, arg.GetInt(1));
        var sync = new NtfSyncPlayer();

        if (IsAll(arg, "add"))
        {
            for (uint moneyType = 1; moneyType <= MaxMoneyType; moneyType++)
                player.AddCurrency(moneyType, amount, sync);

            await player.SendPacket(CmdIds.NtfSyncAttr, sync);
            CurrencyService.Persist(player);
            await arg.SendMsg($"Added {amount} to every currency id (1..{MaxMoneyType}) for uid={player.Data.Uid}.");
            return;
        }

        if (!CurrencyService.TryResolve(arg.Args[0], out var single))
        {
            await arg.SendMsg($"Unknown currency '{arg.Args[0]}'. Use /money list to see the known ids.");
            return;
        }

        player.AddCurrency(single, amount, sync);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        CurrencyService.Persist(player);
        await arg.SendMsg($"{CurrencyService.Describe(single)} + {amount} = {CurrencyService.GetBalance(player, single)} for uid={player.Data.Uid}.");
    }

    [CommandMethod("spend")]
    public async ValueTask Spend(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(2))
            return;
        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var amount = (uint)Math.Max(0, arg.GetInt(1));
        var sync = new NtfSyncPlayer();

        if (IsAll(arg, "spend"))
        {
            var charged = 0;
            var skipped = 0;
            for (uint moneyType = 1; moneyType <= MaxMoneyType; moneyType++)
            {
                if (moneyType == AttrIds.Currency.Vigor)
                    continue; // Vigor is a player field and needs a declared value; use /money spend 4 <n>.

                if (CurrencyService.GetBalance(player, moneyType) == 0)
                {
                    skipped++;
                    continue;
                }

                var attr = player.Attributes.Get(AttrIds.Currency.GroupId, AttrIds.Currency.GetSid(moneyType));
                if (attr == null)
                {
                    skipped++;
                    continue;
                }

                attr.Val = attr.Val > amount ? attr.Val - amount : 0;
                player.Attributes.SyncTo(sync, attr);
                charged++;
            }

            player.SyncMoneyChannels(sync);
            await player.SendPacket(CmdIds.NtfSyncAttr, sync);
            CurrencyService.Persist(player);
            await arg.SendMsg($"Drained {amount} from {charged} currency balance(s) ({skipped} untouched) for uid={player.Data.Uid}.");
            return;
        }

        if (!CurrencyService.TryResolve(arg.Args[0], out var single))
        {
            await arg.SendMsg($"Unknown currency '{arg.Args[0]}'. Use /money list to see the known ids.");
            return;
        }

        if (!CurrencyService.TrySpend(player, single, amount, sync, out var error))
        {
            await arg.SendMsg($"{CurrencyService.Describe(single)} = {CurrencyService.GetBalance(player, single)}, cannot spend {amount} ({error}).");
            return;
        }

        await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        CurrencyService.Persist(player);
        await arg.SendMsg($"Spent {amount} of {CurrencyService.Describe(single)}, left {CurrencyService.GetBalance(player, single)} for uid={player.Data.Uid}.");
    }

    [CommandMethod("list")]
    public async ValueTask List(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var lines = ExistingCurrencies(player)
            .OrderBy(x => x.MoneyType)
            .Select(x => $"{CurrencyService.Describe(x.MoneyType)} = {x.Value}")
            .ToList();

        lines.Insert(0, $"uid={player.Data.Uid} holds {lines.Count} currency balance(s) (Vigor = {player.Data.Vigor}):");
        await arg.SendMsg(string.Join(Environment.NewLine, lines));
    }

    [CommandMethod("seed")]
    public async ValueTask Seed(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        player.Data.CurrencySeeded = false;
        var seeded = CurrencyBootstrap.Seed(player);
        if (seeded > 0)
            CurrencyService.Persist(player);

        await arg.SendMsg($"Seed pass for uid={player.Data.Uid}: {seeded} balance(s) created, flag now {player.Data.CurrencySeeded}.");
    }

    /// <summary>
    /// Drops every group-1 currency so the next login re-seeds from CurrencyBootstrap. This is the
    /// only way out for an account that was inflated to 999_999_999 by the old bootstrap: the new
    /// seed only fills money ids that do not exist yet, so a legacy wallet would stay at the old cap.
    /// </summary>
    [CommandMethod("reset")]
    public async ValueTask Reset(CommandArg arg)
    {
        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var removed = 0;
        for (uint moneyType = 1; moneyType <= MaxMoneyType; moneyType++)
        {
            if (moneyType == AttrIds.Currency.Vigor)
                continue;

            if (player.Attributes.Remove(AttrIds.Currency.GroupId, AttrIds.Currency.GetSid(moneyType)))
                removed++;
        }

        player.Data.CurrencySeeded = false;
        var sync = new NtfSyncPlayer();
        player.SyncMoneyChannels(sync);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        CurrencyService.Persist(player);
        await arg.SendMsg($"Cleared {removed} currency balance(s) for uid={player.Data.Uid}; balances are re-seeded on the next login (or run /money seed).");
    }

    private static bool IsAll(CommandArg arg, string method) =>
        arg.Attributes.Contains("a") || arg.Attributes.Contains("all") ||
        arg.Args.Skip(1).Any(x => string.Equals(x, method, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(uint MoneyType, uint Value)> ExistingCurrencies(PlayerInstance player)
    {
        for (uint moneyType = 1; moneyType <= MaxMoneyType; moneyType++)
        {
            if (moneyType == AttrIds.Currency.Vigor)
                continue;

            var attr = player.Attributes.Get(AttrIds.Currency.GroupId, AttrIds.Currency.GetSid(moneyType));
            if (attr != null)
                yield return (moneyType, attr.Val);
        }
    }
}
