using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Game.Rogue3D;

internal static class Rogue3DTechHelper
{
    public static bool TryGetScience(uint techId, out Rogue3DScienceExcel science)
    {
        if (techId <= AttrIds.Rogue3D.TechLevelStartSid ||
            techId > AttrIds.Rogue3D.TechLevelEndSid)
        {
            science = null!;
            return false;
        }

        return GameData.Rogue3DScienceData.TryGetValue(techId, out science!);
    }

    public static uint GetRestrictionSid(uint techId)
    {
        return checked(techId + AttrIds.Rogue3D.TechRestrictStartSid - AttrIds.Rogue3D.TechLevelStartSid);
    }

    public static bool IsUnlocked(PlayerInstance player, Rogue3DScienceExcel science)
    {
        return science.UnlockCondition.Count == 0 ||
               science.UnlockCondition.Any(id => player.Attributes.GetValue(AttrIds.Rogue3D.Gid, id) > 0);
    }

    public static bool IsRestricted(PlayerInstance player, uint techId)
    {
        return player.Attributes.GetValue(AttrIds.Rogue3D.Gid, GetRestrictionSid(techId)) != 0;
    }
}
