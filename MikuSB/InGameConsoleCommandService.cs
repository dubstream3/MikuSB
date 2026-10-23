using MikuSB.Enums.Player;
using MikuSB.GameServer.Command;
using MikuSB.Util;

namespace MikuSB.MikuSB.Program;

public static class InGameConsoleCommandService
{
    public static async Task<InGameConsoleCommandResponse> ExecuteAsync(
        string command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command = command.Trim();
        if (command.StartsWith('/'))
            command = command[1..].TrimStart();

        var sender = new InGameConsoleCommandSender();
        await CommandManager.HandleCommand(command, sender);
        var messages = sender.Messages.Count == 0
            ? ["Command completed."]
            : sender.Messages.ToArray();
        return new InGameConsoleCommandResponse(true, messages);
    }

    private sealed class InGameConsoleCommandSender : ICommandSender
    {
        public List<string> Messages { get; } = [];

        public ValueTask SendMsg(string msg)
        {
            Messages.Add(msg);
            return ValueTask.CompletedTask;
        }

        public int GetSender()
        {
            return (int)ServerEnum.Console;
        }
    }
}
