using MikuSB.Database.Account;
using MikuSB.GameServer.Server;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command;

public class CommandArg
{
    public string RawArg { get; } = "";
    public List<string> Args { get; } = [];
    public List<string> Attributes { get; } = [];
    public ICommandSender Sender { get; }
    public int TargetUid { get; set; } = 0;
    public Connection? Target { get; set; }

    public CommandArg(string rawArg, ICommandSender sender)
    {
        Sender = sender;
        RawArg = rawArg;
        foreach (var arg in rawArg.Split(' '))
        {
            if (string.IsNullOrEmpty(arg)) continue;
            Args.Add(arg);
        }
    }

    public async ValueTask SendMsg(string msg)
    {
        await Sender.SendMsg(msg);
    }

    public int GetInt(int index)
    {
        if (Args.Count <= index) return 0;
        if (int.TryParse(Args[index], out var res))
            return res;
        return 0;
    }

    public async ValueTask<int?> GetOption(char pre, string def = "1")
    {
        var opStr = Args.FirstOrDefault(x => x[0] == pre)?[1..] ?? def;
        if (!int.TryParse(opStr, out var op))
        {
            await SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return null;
        }
        return op;
    }

    public async ValueTask<bool> CheckArgCnt(int start, int? end = null)
    {
        end ??= start;
        if (Args.Count >= start && Args.Count <= end) return true;
        await SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
        return false;
    }

    public async ValueTask<bool> CheckTarget()
    {
        if (AccountData.GetAccountByUid(TargetUid) == null)
        {
            await SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return false;
        }
        return true;
    }

    public async ValueTask<bool> CheckOnlineTarget(bool sendMsg = true)
    {
        if (Target == null)
        {
            if (sendMsg)
                await SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return false;
        }
        return true;
    }
}