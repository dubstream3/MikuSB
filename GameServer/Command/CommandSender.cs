using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Command;

public interface ICommandSender
{
    public ValueTask SendMsg(string msg);

    public int GetSender();
}

public class ConsoleCommandSender(Logger logger) : ICommandSender
{
    public async ValueTask SendMsg(string msg)
    {
        logger.Info(msg);
        await Task.CompletedTask;
    }

    public int GetSender()
    {
        return (int)ServerEnum.Console;
    }
}

/// <summary>
/// Collects the command output instead of logging it or pushing it into a chat channel. The admin panel has
/// no game socket, so it uses this sender: <see cref="ServerEnum.Console"/> (0) makes AccountData.HasPerm
/// grant every command, leaving the HTTP bearer token as the only gate.
/// </summary>
public class CaptureCommandSender : ICommandSender
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

public class PlayerCommandSender(PlayerInstance player) : ICommandSender
{
    public PlayerInstance Player = player;

    public async ValueTask SendMsg(string msg)
    {
        var data = new ChatMsg
        {
            Type = ChatType.Friend,
            Sender = (uint)ConfigManager.Config.ServerOption.ServerProfile.Uid,
            Recver = (uint)Player.Uid,
            Text = ChatMessageHelper.NormalizeForClient(msg),
            Profile = Player.ToServerFriendProto(),
            TimeStamp = ChatMessageHelper.BuildClientTimestamp()
        };
        await Player.SendPacket(CmdIds.NtfFriendChat, data);
    }

    public int GetSender()
    {
        return Player.Uid;
    }
}