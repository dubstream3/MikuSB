using MikuSB.Database.Account;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.Internationalization;
using MikuSB.TcpSharp;
using MikuSB.Util;
using System.Reflection;

namespace MikuSB.GameServer.Command;

public class CommandManager
{
    public static Logger Logger { get; } = new("CommandManager");

    public static Dictionary<string, ICommands> Commands { get; } = [];
    public static Dictionary<string, CommandInfoAttribute> CommandInfo { get; } = [];
    public static Dictionary<string, string> CommandAlias { get; } = []; // <aliaName, fullName>

    public static void RegisterCommands()
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            if (typeof(ICommands).IsAssignableFrom(type) && !type.IsAbstract)
                RegisterCommand(type);

        Logger.Info(I18NManager.Translate("Server.ServerInfo.RegisterItem", Commands.Count.ToString(),
            I18NManager.Translate("Word.Command")));
    }

    public static void RegisterCommand(Type type)
    {
        var attr = type.GetCustomAttribute<CommandInfoAttribute>();
        if (attr == null) return;
        var instance = Activator.CreateInstance(type);
        if (instance is not ICommands command) return;
        Commands.Add(attr.Name, command);
        CommandInfo.Add(attr.Name, attr);

        // register alias
        foreach (var alias in attr.Alias) // add alias
            CommandAlias.Add(alias, attr.Name);
    }

    public static async Task HandleCommand(string input, ICommandSender sender)
    {
        try
        {
            var argInfo = new CommandArg(input, sender);
            var target = sender.GetSender();

            foreach (var arg in argInfo.Args.ToList()) // Copy
            {
                switch (arg[0])
                {
                    case '-':
                        argInfo.Attributes.Add(arg[1..]);
                        break;
                    case '@':
                        _ = int.TryParse(arg[1..], out target);
                        argInfo.Args.Remove(arg);
                        break;
                }
            }
            argInfo.TargetUid = target;
            if (SocketListener.Connections.Values.ToList().Find(item =>
                (item as Connection)?.Player?.Uid == target) is Connection con)
                argInfo.Target = con;

            // find register cmd
            var cmdName = argInfo.Args[0];
            if (CommandAlias.TryGetValue(cmdName, out var fullName)) cmdName = fullName;
            if (!Commands.TryGetValue(cmdName, out var command))
            {
                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.CommandNotFound"));
                return;
            }
            argInfo.Args.RemoveAt(0);
            var cmdInfo = CommandInfo[cmdName];

            // Check cmd perms
            if (!AccountData.HasPerm(cmdInfo.Perm, sender.GetSender()))
            {
                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.NoPermission"));
                return;
            }
            if (argInfo.Target?.Player?.Uid != sender.GetSender() && !AccountData.HasPerm([PermEnum.Other], sender.GetSender()))
            {
                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.NoPermission"));
                return;
            }

            // find CommandMethodAttribute
            var isFound = false;
            foreach (var methodInfo in command.GetType().GetMethods())
            {
                var attr = methodInfo.GetCustomAttribute<CommandMethodAttribute>();
                if (attr == null) continue;
                if (argInfo.Args.Count > 0 && attr.MethodName == argInfo.Args[0])
                {
                    argInfo.Args.RemoveAt(0);
                    isFound = true;
                    await AwaitCommandResultAsync(methodInfo.Invoke(command, [argInfo]));
                    break;
                }
            }
            if (isFound) return;

            // find CommandDefaultAttribute
            foreach (var methodInfo in command.GetType().GetMethods())
            {
                var attr = methodInfo.GetCustomAttribute<CommandDefaultAttribute>();
                if (attr == null) continue;
                isFound = true;
                await AwaitCommandResultAsync(methodInfo.Invoke(command, [argInfo]));
                break;
            }
            if (isFound) return;

            // failed to find method
            await sender.SendMsg(I18NManager.Translate(cmdInfo.Usage));
        }
        catch (Exception ex)
        {
            Logger.Error(I18NManager.Translate("Game.Command.Notice.InternalError", ex.ToString()));
        }
    }

    private static async Task AwaitCommandResultAsync(object? result)
    {
        switch (result)
        {
            case ValueTask valueTask:
                await valueTask;
                break;
            case Task task:
                await task;
                break;
        }
    }
}
