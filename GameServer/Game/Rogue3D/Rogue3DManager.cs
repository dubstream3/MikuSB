using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Rogue3D;

public enum Rogue3DTechError
{
    None,
    BadParam,
    MaxLevel,
    ConditionLimit,
    GoldNotEnough
}

public readonly record struct Rogue3DTechResult(Rogue3DTechError Error, NtfSyncPlayer? Sync)
{
    public bool Succeeded => Error == Rogue3DTechError.None;
}

public sealed class Rogue3DManager(PlayerInstance player) : BasePlayerManager(player)
{
    private const uint GroupId = AttrIds.Rogue3D.Gid;
    private const uint CurDiffSid = AttrIds.Rogue3D.CurDiffSid;
    private const uint GameplayIdSid = AttrIds.Rogue3D.GameplayIdSid;
    private const uint SeasonGameplayIdSid = AttrIds.Rogue3D.SeasonGameplayIdSid;
    private const uint SeasonEnterFlagSid = AttrIds.Rogue3D.SeasonEnterFlagSid;
    private const uint TalentIdSid = AttrIds.Rogue3D.TalentIdSid;
    private const uint SeasonTalentIdSid = AttrIds.Rogue3D.SeasonTalentIdSid;

    public NtfSyncPlayer EnsureUnlockState()
    {
        return Rogue3DStateHelper.EnsureUnlockState(Player);
    }

    public bool TrySelectDifficulty(uint diffId, out NtfSyncPlayer sync)
    {
        sync = new NtfSyncPlayer();
        if (!GameData.Rogue3DDifficultData.TryGetValue(diffId, out var config) || config.GameplayGroup.Count == 0)
        {
            return false;
        }

        SetAttr(CurDiffSid, diffId, sync);
        SetAttr(GameplayIdSid, config.GameplayGroup[0], sync);
        return true;
    }

    public bool TryEnterSeasonLevel(uint diffId, out NtfSyncPlayer sync)
    {
        sync = new NtfSyncPlayer();
        if (!GameData.Rogue3DDifficultData.TryGetValue(diffId, out var config) || config.GameplayGroup.Count == 0)
        {
            return false;
        }

        SetAttr(SeasonGameplayIdSid, config.GameplayGroup[0], sync);
        SetAttr(SeasonEnterFlagSid, 1, sync);
        return true;
    }

    public NtfSyncPlayer SelectTalent(uint talentId)
    {
        return SetAttr(TalentIdSid, talentId);
    }

    public NtfSyncPlayer SelectSeasonTalent(uint talentId)
    {
        return SetAttr(SeasonTalentIdSid, talentId);
    }

    public Rogue3DTechResult UpgradeTech(uint techId)
    {
        if (!Rogue3DTechHelper.TryGetScience(techId, out var science) ||
            science.MaxLevel == 0 ||
            science.LevelList.Count < science.MaxLevel)
        {
            return Fail(Rogue3DTechError.BadParam);
        }

        var techLevel = Player.Attributes.GetOrCreate(GroupId, techId);
        if (techLevel.Val >= science.MaxLevel)
        {
            return Fail(Rogue3DTechError.MaxLevel);
        }

        if (!Rogue3DTechHelper.IsUnlocked(Player, science) ||
            Rogue3DTechHelper.IsRestricted(Player, techId))
        {
            return Fail(Rogue3DTechError.ConditionLimit);
        }

        var nextLevelId = science.LevelList[(int)techLevel.Val];
        if (!GameData.Rogue3DScienceLevelData.TryGetValue(nextLevelId, out var level) ||
            level.Cost.Count < 2 ||
            level.Cost[0] != AttrIds.Rogue3D.TechPointCurrencyType)
        {
            return Fail(Rogue3DTechError.BadParam);
        }

        var currency = Player.Attributes.GetOrCreate(
            AttrIds.Currency.GroupId,
            AttrIds.Currency.GetSid(level.Cost[0]));
        if (currency.Val < level.Cost[1])
        {
            return Fail(Rogue3DTechError.GoldNotEnough);
        }

        currency.Val -= level.Cost[1];
        techLevel.Val++;

        var sync = new NtfSyncPlayer();
        Player.Attributes.SyncTo(sync, currency);
        Player.Attributes.SyncTo(sync, techLevel);
        DatabaseHelper.SaveDatabaseType(Player.Data);

        return new Rogue3DTechResult(Rogue3DTechError.None, sync);
    }

    public Rogue3DTechResult RestrictTech(uint techId, uint restrict)
    {
        if (restrict > 1 || !Rogue3DTechHelper.TryGetScience(techId, out _))
        {
            return Fail(Rogue3DTechError.BadParam);
        }

        if (Player.Attributes.GetValue(GroupId, techId) == 0)
        {
            return Fail(Rogue3DTechError.ConditionLimit);
        }

        var restriction = Player.Attributes.GetOrCreate(
            GroupId,
            Rogue3DTechHelper.GetRestrictionSid(techId));
        if (restriction.Val == restrict)
        {
            return new Rogue3DTechResult(Rogue3DTechError.None, null);
        }

        restriction.Val = restrict;
        var sync = new NtfSyncPlayer();
        Player.Attributes.SyncTo(sync, restriction);
        DatabaseHelper.SaveDatabaseType(Player.Data);

        return new Rogue3DTechResult(Rogue3DTechError.None, sync);
    }

    private NtfSyncPlayer SetAttr(uint sid, uint value)
    {
        var sync = new NtfSyncPlayer();
        SetAttr(sid, value, sync);
        return sync;
    }

    private void SetAttr(uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = Player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val == value)
        {
            return;
        }

        attr.Val = value;
        Player.Attributes.SyncTo(sync, attr);
    }

    private static Rogue3DTechResult Fail(Rogue3DTechError error)
    {
        return new Rogue3DTechResult(error, null);
    }
}
