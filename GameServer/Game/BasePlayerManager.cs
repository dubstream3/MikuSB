using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Game;

public class BasePlayerManager(PlayerInstance player)
{
    public PlayerInstance Player { get; private set; } = player;
}