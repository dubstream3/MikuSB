using System.Numerics;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Quest;

public enum QuestLevelType
{
    Chapter,
    Daily,
    Role
}

public readonly record struct QuestSettlementResult(JsonArray Rewards, NtfSyncPlayer Sync);
public readonly record struct ChapterStarAwardResult(JsonObject Response, NtfSyncPlayer Sync);
public readonly record struct QuestCompletionResult(int LevelCount, NtfSyncPlayer Sync);

public class QuestManager(PlayerInstance player) : BasePlayerManager(player)
{
    /// <summary>
    /// A level id namespace for the shared (21, levelId) / (22, levelId) pair. The client reads both
    /// groups through the same two constants (Launch.GID / Launch.GPASSID), so every mode that
    /// settles a level ends up in the same id space and two modes can claim the same id.
    /// </summary>
    public enum SeedLevelType
    {
        Chapter,
        Daily,
        Role,
        Tower,
        TowerEvent,
        VirCaptureTower,
        DlcChapter,
        SeasonBoss,
        Defend,
        DonoDuelArena
    }

    private const uint LevelStateGroupId = AttrIds.Quest.LevelStateGid;
    private const uint LevelPassGroupId = AttrIds.Quest.LevelPassGid;
    private const uint SettlementSeedGroupId = AttrIds.Quest.SettlementSeedGid;
    private const uint ChapterStarAwardGroupId = AttrIds.Quest.ChapterStarAwardGid;
    private const uint ChapterStarAwardMaskVersionSid = AttrIds.Quest.ChapterStarAwardMaskVersionSid;
    private const uint ChapterStarAwardMaskVersion = 1;
    private const uint LevelStarMask = 0b111;
    private const int MaxChapterStarAwardIndex = 31;
    private const uint LegacyUnlockedLevelPassTime = 1_700_000_000;
    private static readonly Logger Logger = new("Quest");
    private readonly SemaphoreSlim settlementLock = new(1, 1);
    private bool allLevelsCompletedForTesting = player.Data.CompleteAllQuestLevels;

    public void RemoveLegacyLevelUnlocks()
    {
        var levelIds = GameData.ChapterLevelData.Keys
            .Concat(GameData.DailyLevelData.Keys)
            .Concat(GameData.RoleLevelData.Keys)
            .Distinct();

        foreach (var levelId in levelIds)
        {
            var passAttr = Player.Attributes.Get(LevelPassGroupId, levelId);
            if (passAttr?.Val != LegacyUnlockedLevelPassTime)
                continue;

            Player.Attributes.Remove(LevelPassGroupId, levelId);

            Player.Attributes.Remove(LevelStateGroupId, levelId);
        }
    }

    /// <summary>
    /// Levels that an id can be claimed by when two modes share it, highest priority first. The id
    /// spaces genuinely overlap in the shipped tables (chapter 1011-1495 against rogue3d science
    /// levels, tower levels 3011-3035 against season boss, defend 7001-7027 against the timed
    /// mode), and the client reads one attribute pair per id, so a single type has to own the value.
    /// The types listed here are the ones the server can actually settle; the unmodelled modes win
    /// nothing and are left alone rather than overwritten with a state the client never expects.
    /// </summary>
    private static readonly SeedLevelType[] SeedPriority =
    [
        SeedLevelType.DlcChapter,
        SeedLevelType.SeasonBoss,
        SeedLevelType.Tower,
        SeedLevelType.TowerEvent,
        SeedLevelType.VirCaptureTower,
        SeedLevelType.DonoDuelArena,
        SeedLevelType.Daily,
        SeedLevelType.Role,
        SeedLevelType.Defend,
        SeedLevelType.Chapter
    ];

    /// <summary>
    /// Every level id the server knows how to describe, tagged with the mode that claims it. Only
    /// the tables the server has a resource class for are listed: the other modes the client reads
    /// (rogue, repression, online boss, timed mode, alien defense, bag rogue, element tower,
    /// girl rush, farm war...) have no [ResourceEntity] class yet, so their ids cannot be recovered
    /// here and their state stays whatever the client last reported.
    /// </summary>
    private static IEnumerable<(SeedLevelType LevelType, uint LevelId)> GetSeedableLevels()
    {
        foreach (var levelId in GameData.ChapterLevelData.Keys)
            yield return (SeedLevelType.Chapter, levelId);

        foreach (var levelId in GameData.DailyLevelData.Keys)
            yield return (SeedLevelType.Daily, levelId);

        foreach (var levelId in GameData.RoleLevelData.Keys)
            yield return (SeedLevelType.Role, levelId);

        foreach (var levelId in GameData.TowerLevelData.Keys)
            yield return (SeedLevelType.Tower, levelId);

        foreach (var levelId in GameData.TowerEventLevelData.Keys)
            yield return (SeedLevelType.TowerEvent, levelId);

        foreach (var levelId in GameData.VirCaptureTowerData.Keys)
            yield return (SeedLevelType.VirCaptureTower, levelId);

        foreach (var levelId in GameData.DlcChapterLevelData.Keys)
            yield return (SeedLevelType.DlcChapter, levelId);

        foreach (var levelId in GameData.SeasonBossLevelData.Keys)
            yield return (SeedLevelType.SeasonBoss, levelId);

        foreach (var levelId in GameData.DefendLevelData.Keys)
            yield return (SeedLevelType.Defend, levelId);

        // DonoDuel arena levels (donoduel_arena_battle.json, 5505..5518) are deliberately NOT seeded.
        // The story tab draws its "cleared" tick from me:GetAttribute(Launch.GPASSID = 22, levelId)
        // (uw_donoduel_arena_level_st.lua:75 -> DonoDuelArena.lua:80 IsPass), so seeding the pass count
        // marked every story level as cleared - including the ones the player had never fought. Their
        // real clears are written by DonoDuelArenaSettlement.cs, and access to a story level is gated by
        // its DonoDuel story mission condition, not by the pass count.
    }

    /// <summary>
    /// Resolves each shared level id to the mode that owns it. Rogue3D science levels are excluded
    /// on purpose: they collide with 40 chapter ids and the client has no (21, id) consumer for
    /// them, so letting them win would blank out real chapter stars.
    /// </summary>
    private static IReadOnlyDictionary<uint, SeedLevelType> ResolveSeedOwners()
    {
        var rank = new Dictionary<SeedLevelType, int>();
        for (var i = 0; i < SeedPriority.Length; i++)
            rank[SeedPriority[i]] = i;

        var owners = new Dictionary<uint, SeedLevelType>();
        foreach (var (levelType, levelId) in GetSeedableLevels())
        {
            // AlienDefenseExcel registers activity ids rather than level ids, and a handful of rows
            // convert to id 0; neither is a level the client can look up.
            if (levelId == 0)
                continue;

            if (!owners.TryGetValue(levelId, out var current) || rank[levelType] < rank[current])
                owners[levelId] = levelType;
        }

        return owners;
    }

    /// <summary>
    /// Bit pattern the client expects for a cleared level. The star bits are per-level flags read
    /// through GetBits (RoleLevel.lua:38-82 DidGotStar / CountGotStar); bit 8 is the "story level
    /// watched" / "daily cleared" flag the settlement handlers set as well
    /// (DlcChapterSettlement.cs, TowerEventChapter_LevelSettlement.cs).
    /// </summary>
    private static uint SeedState(SeedLevelType levelType) => levelType switch
    {
        SeedLevelType.Daily => LevelStarMask | 0b1111_1000u,
        SeedLevelType.Tower => LevelStarMask | 0b0011_1000u,
        SeedLevelType.TowerEvent or SeedLevelType.VirCaptureTower => LevelStarMask | 0b0001_1000u,
        SeedLevelType.DlcChapter => LevelStarMask | 0b1111_1000u,
        _ => LevelStarMask
    };

    /// <summary>
    /// Writes a real, persisted clear state for every level the server models. This is what makes
    /// the completion UI correct on the first frame of a login instead of after the player happens
    /// to clear something: PlayerAttributes.SyncTo(Proto.Player) only walks the stored attributes,
    /// so a value that is merely injected into a sync packet never reaches the saved player data.
    /// Lower existing values are raised, never lowered, and an existing pass count is kept.
    /// </summary>
    public int SeedAllLevelStates()
    {
        var owners = ResolveSeedOwners();
        var seeded = 0;

        foreach (var (levelId, levelType) in owners)
        {
            var state = Player.Attributes.Get(LevelStateGroupId, levelId);
            if (state == null)
            {
                Player.Attributes.Set(LevelStateGroupId, levelId, SeedState(levelType));
                seeded++;
            }
            else if (state.Val < SeedState(levelType))
            {
                state.Val = SeedState(levelType);
                seeded++;
            }

            var pass = Player.Attributes.Get(LevelPassGroupId, levelId);
            if (pass == null)
            {
                Player.Attributes.Set(LevelPassGroupId, levelId, 1);
                seeded++;
            }
        }

        return seeded;
    }

    public void MigrateChapterStarAwardMasks()
    {
        var versionAttr = Player.Attributes.Get(
            ChapterStarAwardGroupId,
            ChapterStarAwardMaskVersionSid);
        if (versionAttr?.Val >= ChapterStarAwardMaskVersion)
            return;

        foreach (var attr in Player.Attributes.All.Where(x =>
                     x.Gid == ChapterStarAwardGroupId && x.Sid != ChapterStarAwardMaskVersionSid))
        {
            attr.Val <<= 1;
        }

        if (versionAttr == null)
            versionAttr = Player.Attributes.GetOrCreate(
                ChapterStarAwardGroupId,
                ChapterStarAwardMaskVersionSid);

        versionAttr.Val = ChapterStarAwardMaskVersion;
        DatabaseHelper.SaveDatabaseType(Player.Data);
    }

    public async ValueTask<QuestCompletionResult> SetAllLevelsForTestingAsync(bool completed)
    {
        await settlementLock.WaitAsync();
        try
        {
            var sync = new NtfSyncPlayer();
            var levelConfigs = GetAllLevelConfigs().ToArray();
            var levelCount = levelConfigs
                .Select(x => x.LevelConfig.ID)
                .Distinct()
                .Count();

            allLevelsCompletedForTesting = completed;
            Player.Data.CompleteAllQuestLevels = completed;
            if (completed)
            {
                // Turn the flag into real stored attributes as well: the login snapshot only
                // serialises PlayerGameData.Attrs, so a sync-only override was lost the moment the
                // player reconnected (and the UI came back locked until some level was cleared).
                var seeded = SeedAllLevelStates();
                if (seeded > 0)
                    DatabaseHelper.SaveDatabaseType(Player.Data);

                SyncTestingStateTo(sync);
            }
            else
            {
                foreach (var levelId in levelConfigs.Select(x => x.LevelConfig.ID).Distinct())
                {
                    SyncStoredLevelState(LevelStateGroupId, levelId, sync);
                    SyncStoredLevelState(LevelPassGroupId, levelId, sync);
                }
            }

            DatabaseHelper.SaveCompleteAllQuestLevels(Player.Data);
            Logger.Info($"All quest levels updated for testing. uid={Player.Uid} completed={completed} levelCount={levelCount}");
            return new QuestCompletionResult(levelCount, sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    public void SyncTestingStateTo(NtfSyncPlayer sync)
    {
        if (!allLevelsCompletedForTesting)
            return;

        var states = new Dictionary<uint, uint>();
        foreach (var (levelType, levelConfig) in GetAllLevelConfigs())
            states[levelConfig.ID] = states.GetValueOrDefault(levelConfig.ID) |
                                     GetCompletedState(levelType, levelConfig);

        foreach (var (levelId, state) in states)
        {
            Player.Attributes.SyncTo(sync, LevelStateGroupId, levelId, state);
            Player.Attributes.SyncTo(sync, LevelPassGroupId, levelId, 1);
        }
    }

    private void SyncStoredLevelState(uint groupId, uint levelId, NtfSyncPlayer sync)
    {
        Player.Attributes.SyncTo(sync, groupId, levelId, Player.Attributes.GetValue(groupId, levelId));
    }

    public async ValueTask<QuestSettlementResult?> SettleLevelAsync(
        QuestLevelType levelType,
        uint levelId,
        int starMask,
        uint seed)
    {
        if (seed == 0 || starMask < 0 || starMask > LevelStarMask ||
            !Player.IsLevelSession(levelType, levelId, seed))
            return null;

        var levelConfig = ResolveLevelConfig(levelType, levelId);
        if (levelConfig == null)
            return null;

        await settlementLock.WaitAsync();
        try
        {
            var levelPass = Player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
            var settlementSeed = Player.Attributes.GetOrCreate(SettlementSeedGroupId, levelId);
            var levelState = Player.Attributes.GetOrCreate(LevelStateGroupId, levelId);
            var isFirstClear = GetPassCount(levelId) == 0 &&
                               (levelType != QuestLevelType.Daily || (GetLevelState(levelType, levelId) & (1u << 8)) == 0);

            if (settlementSeed.Val == seed)
            {
                var duplicateSync = Player.RewardManager.BuildFullSync();
                var duplicateRewards = Player.RewardManager.ResolveLevelRewards(levelConfig, levelPass.Val <= 1, seed);
                return new QuestSettlementResult(duplicateRewards, duplicateSync);
            }

            var sync = new NtfSyncPlayer();
            await Player.RewardManager.GrantLevelRewardsAsync(levelConfig, isFirstClear, seed, sync);

            levelState.Val |= (uint)starMask & LevelStarMask;
            if (levelType == QuestLevelType.Daily)
                levelState.Val |= 1u << 8;
            levelPass.Val = levelPass.Val == uint.MaxValue ? uint.MaxValue : levelPass.Val + 1;
            Player.Attributes.SyncTo(sync, levelState);
            Player.Attributes.SyncTo(sync, levelPass);
            settlementSeed.Val = seed;
            Player.Attributes.SyncTo(sync, settlementSeed);

            Logger.Info($"Level settlement saved. uid={Player.Uid} levelType={levelType} levelId={levelId} " +
                        $"starMask={starMask} stateVal={levelState.Val} passVal={levelPass.Val}");

            DatabaseHelper.SaveDatabaseType(Player.Data);
            DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(Player.CharacterManager.CharacterData);
            return new QuestSettlementResult(
                Player.RewardManager.ResolveLevelRewards(levelConfig, isFirstClear, seed),
                sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    public bool IsPlotLevel(uint levelId) =>
        GameData.ChapterLevelData.TryGetValue(levelId, out var levelConfig) && levelConfig.IsPlot();

    public async ValueTask<QuestSettlementResult?> SettlePlotLevelAsync(uint levelId)
    {
        if (!GameData.ChapterLevelData.TryGetValue(levelId, out var levelConfig) || !levelConfig.IsPlot())
            return null;

        await settlementLock.WaitAsync();
        try
        {
            var levelState = Player.Attributes.GetOrCreate(LevelStateGroupId, levelId);
            if ((GetLevelState(QuestLevelType.Chapter, levelId) & (1u << 8)) != 0)
                return new QuestSettlementResult(new JsonArray(), Player.RewardManager.BuildFullSync());

            var levelPass = Player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
            var sync = new NtfSyncPlayer();
            await Player.RewardManager.GrantLevelRewardsAsync(levelConfig, true, levelId, sync);

            levelState.Val |= 1u << 8;
            if (levelPass.Val == 0)
                levelPass.Val = 1;

            Player.Attributes.SyncTo(sync, levelState);
            Player.Attributes.SyncTo(sync, levelPass);

            Logger.Info($"Plot settlement saved. uid={Player.Uid} levelId={levelId} " +
                        $"stateVal={levelState.Val} passVal={levelPass.Val}");

            DatabaseHelper.SaveDatabaseType(Player.Data);
            DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(Player.CharacterManager.CharacterData);
            return new QuestSettlementResult(
                Player.RewardManager.ResolveLevelRewards(levelConfig, true, levelId),
                sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    private static ILevelRewardConfig? ResolveLevelConfig(QuestLevelType levelType, uint levelId) =>
        levelType switch
        {
            QuestLevelType.Chapter => GameData.ChapterLevelData.GetValueOrDefault(levelId),
            QuestLevelType.Daily => GameData.DailyLevelData.GetValueOrDefault(levelId),
            QuestLevelType.Role => GameData.RoleLevelData.GetValueOrDefault(levelId),
            _ => null
        };

    public bool CanEnterLevel(QuestLevelType levelType, uint levelId)
    {
        if (ResolveLevelConfig(levelType, levelId) == null)
            return false;

        if (allLevelsCompletedForTesting)
            return true;

        var predecessorIds = GetLevelConfigs(levelType)
            .Where(level => level.NextId() == levelId)
            .Select(level => level.ID)
            .ToArray();

        return predecessorIds.Length == 0 || predecessorIds.Any(id => GetPassCount(id) > 0);
    }

    public bool SyncGuideLevelPassData(JsonNode? payload)
    {
        if (payload is not JsonObject root || root["tbData"] is not JsonArray rows)
            return false;

        if (allLevelsCompletedForTesting)
            return true;

        var updates = new List<(uint LevelId, uint PassCount)>();
        foreach (var rowNode in rows)
        {
            if (rowNode is not JsonArray row || row.Count < 2 ||
                !TryGetUInt(row[0], out var levelId) ||
                !TryGetUInt(row[1], out var passCount) ||
                levelId == 0 || !GameData.ChapterLevelData.ContainsKey(levelId))
                return false;

            updates.Add((levelId, passCount));
        }

        var changed = false;
        foreach (var (levelId, passCount) in updates)
        {
            var attr = Player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
            if (passCount <= attr.Val)
                continue;

            attr.Val = passCount;
            changed = true;
        }

        if (changed)
            DatabaseHelper.SaveDatabaseType(Player.Data);

        return true;
    }

    public async ValueTask<ChapterStarAwardResult?> ClaimChapterStarAwardsAsync(
        bool isMain,
        uint difficult,
        uint chapterId,
        int awardIndex)
    {
        if (!GameData.ChapterData.TryGetValue(ChapterExcel.GetKey(isMain, difficult, chapterId), out var chapter))
            return null;

        await settlementLock.WaitAsync();
        try
        {
            var awards = chapter.StarAwards;
            var claimedAttr = Player.Attributes.GetOrCreate(
                ChapterStarAwardGroupId,
                (chapterId << 8) | difficult);
            var starCount = GetChapterStarCount(chapter);
            var selected = new List<(int Index, ChapterStarAward Award)>();

            if (awardIndex == -1)
            {
                for (var i = 0; i < awards.Count && i < MaxChapterStarAwardIndex; i++)
                {
                    var index = i + 1;
                    var claimBit = 1u << index;
                    if ((claimedAttr.Val & claimBit) == 0 && starCount >= awards[i].RequiredStars)
                        selected.Add((index, awards[i]));
                }
            }
            else
            {
                if (awardIndex < 1 || awardIndex > awards.Count || awardIndex > MaxChapterStarAwardIndex)
                    return null;

                var index = awardIndex - 1;
                var claimBit = 1u << awardIndex;
                if ((claimedAttr.Val & claimBit) != 0 || starCount < awards[index].RequiredStars)
                    return null;

                selected.Add((awardIndex, awards[index]));
            }

            if (selected.Count == 0)
                return null;

            var sync = new NtfSyncPlayer();
            var rewardRows = selected.SelectMany(x => x.Award.Rewards).ToArray();
            var grantedRewards = await Player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);
            foreach (var (index, _) in selected)
                claimedAttr.Val |= 1u << index;

            Player.Attributes.SyncTo(sync, claimedAttr);
            DatabaseHelper.SaveDatabaseType(Player.Data);
            DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(Player.CharacterManager.CharacterData);

            return new ChapterStarAwardResult(
                new JsonObject { ["tbAward"] = grantedRewards },
                sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    private IEnumerable<ILevelRewardConfig> GetLevelConfigs(QuestLevelType levelType) =>
        levelType switch
        {
            QuestLevelType.Chapter => GameData.ChapterLevelData.Values,
            QuestLevelType.Daily => GameData.DailyLevelData.Values,
            QuestLevelType.Role => GameData.RoleLevelData.Values,
            _ => []
        };

    private IEnumerable<(QuestLevelType LevelType, ILevelRewardConfig LevelConfig)> GetAllLevelConfigs()
    {
        foreach (var levelType in Enum.GetValues<QuestLevelType>())
        {
            foreach (var levelConfig in GetLevelConfigs(levelType))
                yield return (levelType, levelConfig);
        }
    }

    private static uint GetCompletedState(QuestLevelType levelType, ILevelRewardConfig levelConfig)
    {
        if (levelType == QuestLevelType.Chapter && levelConfig.IsPlot())
            return 1u << 8;

        return levelType == QuestLevelType.Daily
            ? LevelStarMask | (1u << 8)
            : LevelStarMask;
    }

    private uint GetPassCount(uint levelId)
    {
        var stored = Player.Attributes.GetValue(LevelPassGroupId, levelId);

        // The testing flag still forces "cleared" for the levels whose stored value has not been
        // seeded yet, so /quest complete_all keeps working while seeding takes over the rest.
        return allLevelsCompletedForTesting && stored == 0 ? 1 : stored;
    }

    private uint GetLevelState(QuestLevelType levelType, uint levelId)
    {
        if (allLevelsCompletedForTesting && ResolveLevelConfig(levelType, levelId) is { } levelConfig)
            return GetCompletedState(levelType, levelConfig);

        return Player.Attributes.GetValue(LevelStateGroupId, levelId);
    }

    private uint GetChapterStarCount(ChapterExcel chapter)
    {
        ulong total = 0;
        foreach (var levelId in chapter.Level)
        {
            var value = GetLevelState(QuestLevelType.Chapter, levelId);
            total += (uint)BitOperations.PopCount(value & LevelStarMask);
        }

        return (uint)Math.Min(uint.MaxValue, total);
    }

    private static bool TryGetUInt(JsonNode? node, out uint value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var parsed))
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }
}
