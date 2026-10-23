
namespace MikuSB.GameServer.Command;

public static class CommandExecutor
{
    public delegate void RunCommand(ICommandSender sender, string cmd);

    public static event RunCommand? OnRunCommand;

    public static void ExecuteCommand(ICommandSender sender, string cmd)
    {
        OnRunCommand?.Invoke(sender, cmd);
    }

    public static void ConsoleExcuteCommand(string input)
    {
        _ = CommandManager.HandleCommand(input, new ConsoleCommandSender(CommandManager.Logger));
    }
}