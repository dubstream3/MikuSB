using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.GameServer.Server;
using System.Text;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("account", "Game.Command.Account.Desc", "Game.Command.Account.Usage", [], [PermEnum.Admin, PermEnum.Support])]
public class CommandAccount : ICommands
{
    [CommandMethod("create")]
    public async ValueTask Create(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(2))
            return;

        var email = arg.Args[0].Trim();
        if (!int.TryParse(arg.Args[1], out var uid) || uid <= 0)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        try
        {
            var account = AccountData.CreateAccount(email, uid, "");
            await arg.SendMsg(I18NManager.Translate("Game.Command.Account.Created", account.Username, account.Uid.ToString()));
        }
        catch (InvalidOperationException ex)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Account.CreateFailed", ex.Message));
        }
        catch (ArgumentException ex)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Account.CreateFailed", ex.Message));
        }
    }

    [CommandMethod("delete")]
    public async ValueTask Delete(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(1))
            return;

        var identifier = arg.Args[0].Trim();
        var account = int.TryParse(identifier, out var uid) && uid > 0
            ? AccountData.GetAccountByUid(uid)
            : AccountData.GetAccountByUserName(identifier);

        if (account == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Account.NotFound", identifier));
            return;
        }

        try
        {
            if (Listener.GetActiveConnection(account.Uid) != null)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Account.DeleteOnline", account.Username,
                    account.Uid.ToString()));
                return;
            }

            AccountData.DeleteAccount(account.Uid);
            await arg.SendMsg(I18NManager.Translate("Game.Command.Account.Deleted", account.Username,
                account.Uid.ToString()));
        }
        catch (Exception ex)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Account.DeleteFailed", ex.Message));
        }
    }

    [CommandMethod("list")]
    public async ValueTask List(CommandArg arg)
    {
        var accounts = DatabaseHelper.GetAllInstance<AccountData>()?
            .OrderBy(account => account.Uid)
            .ToList();

        if (accounts == null || accounts.Count == 0)
        {
            await arg.SendMsg("No accounts found.");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Accounts:");
        foreach (var account in accounts)
            builder.AppendLine($"{account.Username} -> UID {account.Uid}");

        await arg.SendMsg(builder.ToString().TrimEnd());
    }
}
