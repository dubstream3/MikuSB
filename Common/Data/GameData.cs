using MikuSB.Data.Excel;

namespace MikuSB.Data;

public static class GameData
{
    public static Dictionary<uint, CardExcel> CardData { get; private set; } = [];
    public static Dictionary<uint, WeaponExcel> WeaponData { get; private set; } = [];
    public static Dictionary<uint, CardSkinExcel> CardSkinData { get; private set; } = [];
    // Keyed by the full 64-bit template id (GameResourceTemplateId.FromGdpl), like OtherItemData:
    // a uint key collapses every row that shares (Detail, Genre) into one entry, because
    // FromGdpl puts Level in bits 48-63 and Particular in bits 32-47.
    public static Dictionary<ulong, SuppliesExcel> SuppliesData { get; private set; } = [];
    public static List<SuppliesExcel> AllSuppliesData { get; private set; } = [];
    public static Dictionary<int, UpgradeExpExcel> UpgradeExpData { get; private set; } = [];
    public static Dictionary<int, BreakLevelLimitExcel> BreakLevelLimitData { get; private set; } = [];
    public static Dictionary<int, RecycleExcel> RecycleData { get; private set; } = [];
    public static Dictionary<uint, ChapterLevelExcel> ChapterLevelData { get; private set; } = [];
    public static Dictionary<ulong, ChapterExcel> ChapterData { get; private set; } = [];
    public static Dictionary<uint, RoleLevelExcel> RoleLevelData { get; private set; } = [];
    public static Dictionary<uint, PlayerLevelExcel> PlayerLevelData { get; private set; } = [];
    public static Dictionary<uint, DropExcel> DropData { get; private set; } = [];
    public static Dictionary<uint, DropGroupExcel> DropGroupData { get; private set; } = [];
    public static Dictionary<uint, ArItemExcel> ArItemData { get; private set; } = [];
    public static Dictionary<uint, ManifestationExcel> ManifestationData { get; private set; } = [];
    public static Dictionary<uint, Rogue3DDifficultExcel> Rogue3DDifficultData { get; private set; } = [];
    public static Dictionary<uint, Rogue3DSeasonExcel> Rogue3DSeasonData { get; private set; } = [];
    public static Dictionary<uint, Rogue3DScienceExcel> Rogue3DScienceData { get; private set; } = [];
    public static Dictionary<uint, Rogue3DScienceLevelExcel> Rogue3DScienceLevelData { get; private set; } = [];
    public static Dictionary<uint, Rogue3DTalentExcel> Rogue3DTalentData { get; private set; } = [];
    public static Dictionary<uint, Rogue3DDailyBuffExcel> Rogue3DDailyBuffData { get; private set; } = [];
    public static Dictionary<int, BreakExcel> BreakData { get; private set; } = [];
    public static Dictionary<int, SpecialBreakExcel> SpecialBreakData { get; private set; } = [];
    public static Dictionary<uint, SpineExcel> SpineData { get; private set; } = [];
    public static Dictionary<uint, NodeConditionExcel> NodeConditionData { get; private set; } = [];
    // Keyed by the full 64-bit card GDPL — the same packing the client uses for its
    // "g-d-p-l" pro-level key (UMG/Role/RoleCard.lua:1298).
    public static Dictionary<ulong, ProLevelExcel> ProLevelData { get; private set; } = [];
    public static List<SupportCardExcel> SupportCardData { get; private set; } = [];
    public static Dictionary<int, SupportAffixExcel> SupportAffixData { get; private set; } = [];
    public static Dictionary<int, SupportAffixPoolExcel> SupportAffixPoolData { get; private set; } = [];
    public static Dictionary<int, SupportFixedExcel> SupportFixedData { get; private set; } = [];
    public static Dictionary<uint, WeaponSkinExcel> WeaponSkinData { get; private set; } = [];
    public static Dictionary<uint, DailyLevelExcel> DailyLevelData { get; private set; } = [];
    public static Dictionary<uint, BossPvpBossChallengeExcel> BossPvpBossChallengeData { get; private set; } = [];
    public static Dictionary<uint, BossPvpBossExcel> BossPvpBossData { get; private set; } = [];
    public static Dictionary<uint, BossPvpNumExcel> BossPvpNumData { get; private set; } = [];
    public static Dictionary<uint, ClimbTowerTimeExcel> ClimbTowerTimeData { get; private set; } = [];
    public static Dictionary<uint, ClimbTowerDiffExcel> ClimbTowerDiffData { get; private set; } = [];
    public static Dictionary<uint, Dictionary<int, ClimbTowerAwardExcel>> ClimbTowerAwardData { get; private set; } = [];
    public static Dictionary<uint, ClimbTowerLevelOrderExcel> ClimbTowerLevelOrderData { get; private set; } = [];
    public static Dictionary<uint, DlcClimbTowerSeasonExcel> ClimbTowerSeasonData { get; private set; } = [];
    public static Dictionary<uint, DlcClimbTowerBattleExcel> ClimbTowerBattleData { get; private set; } = [];
    public static Dictionary<uint, DlcClimbTowerRewardExcel> ClimbTowerRewardData { get; private set; } = [];
    public static Dictionary<uint, TowerLevelExcel> TowerLevelData { get; private set; } = [];
    public static Dictionary<uint, TowerEventLevelExcel> TowerEventLevelData { get; private set; } = [];
    public static Dictionary<ulong, OtherItemExcel> OtherItemData { get; private set; } = [];
    public static Dictionary<uint, ProfileExcel> ProfileData { get; private set; } = [];
    public static Dictionary<uint, CardSkinPartsExcel> CardSkinPartsData { get; private set; } = [];
    public static Dictionary<uint, CallItemExcel> CallItemData { get; private set; } = [];
    public static Dictionary<uint, WeaponPartsExcel> WeaponPartsData { get; private set; } = [];
    public static Dictionary<uint, GuideExcel> GuideData { get; private set; } = [];
    public static Dictionary<uint, DormGiftExcel> DormGiftData { get; private set; } = [];
    public static Dictionary<uint, HouseFurniturePosExcel> HouseFurniturePosData { get; private set; } = [];
    public static Dictionary<uint, GachaExcel> GachaData { get; private set; } = [];
    public static Dictionary<uint, GachaProbabilityExcel> GachaProbabilityData { get; private set; } = [];
    public static Dictionary<string, List<GachaPoolItem>> GachaPoolData { get; private set; } = [];
    public static Dictionary<uint, VirCaptureTimeExcel> VirCaptureTimeData { get; private set; } = [];
    public static Dictionary<uint, VirCaptureSeasonExcel> VirCaptureSeasonData { get; private set; } = [];
    public static Dictionary<uint, VirCaptureTrialTimeExcel> VirCaptureTrialTimeData { get; private set; } = [];
    public static Dictionary<uint, VirCaptureCaptureRegionExcel> VirCaptureCaptureRegionData { get; private set; } = [];
    public static Dictionary<uint, VirCaptureLevelListExcel> VirCaptureLevelListData { get; private set; } = [];
    public static Dictionary<ulong, MonsterCardExcel> MonsterCardData { get; private set; } = [];
    public static Dictionary<uint, FishingFoodExcel> FishingFoodData { get; private set; } = [];
    public static Dictionary<uint, VirCaptureTowerExcel> VirCaptureTowerData { get; private set; } = [];
    public static Dictionary<uint, DreamCardActivityExcel> DreamCardActivityData { get; private set; } = [];
    public static Dictionary<uint, DlcActivityExcel> DlcActivityData { get; private set; } = [];
    public static Dictionary<uint, ActivityExcel> ActivityData { get; private set; } = [];
    public static Dictionary<uint, DailyExcel> DailyData { get; private set; } = [];
    public static Dictionary<uint, DlcShopExcel> DlcShopData { get; private set; } = [];
    public static Dictionary<uint, DefendActExcel> DefendActData { get; private set; } = [];
    public static Dictionary<uint, DefendLevelAwardExcel> DefendLevelAwardData { get; private set; } = [];
    public static Dictionary<uint, DefendTargetExcel> DefendTargetData { get; private set; } = [];
    public static Dictionary<uint, DefendLevelExcel> DefendLevelData { get; private set; } = [];
    public static Dictionary<uint, BattlePassTimeExcel> BattlePassTimeData { get; private set; } = [];
    public static Dictionary<uint, IbGoodsExcel> IbGoodsData { get; private set; } = [];
    public static Dictionary<uint, RikiExcel> RikiData { get; private set; } = [];

    /// <summary>
    /// monster template id -&gt; riki (图鉴) entry id, built from the Type=4 rows of riki/Riki.json whose
    /// Condition column lists the monsters the entry covers (4001 -&gt; [6001003, 6002003]). The
    /// settlement packets report killed monsters (RikiLogic:GetMonsterData) and the riki page reads its
    /// unlock bit from attribute (103, taskId), so this is the reverse map that turns a report into the
    /// entry to unlock.
    /// </summary>
    public static Dictionary<uint, uint> RikiMonsterData { get; private set; } = [];
    public static Dictionary<uint, OnlineExcel> OnlineData { get; private set; } = [];
    public static Dictionary<uint, SeasonBossExcel> SeasonBossData { get; private set; } = [];
    public static Dictionary<uint, RoleFestivalMessageExcel> RoleFestivalMessageData { get; private set; } = [];
    public static Dictionary<uint, RoleFestivalContentExcel> RoleFestivalContentData { get; private set; } = [];
    public static Dictionary<uint, RoleFestivalRewardExcel> RoleFestivalRewardData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelSeasonExcel> DonoDuelSeasonData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelStoryTaskExcel> DonoDuelStoryTaskData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelArenaBattleExcel> DonoDuelArenaBattleData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelHeroCardExcel> DonoDuelHeroCardData { get; private set; } = [];
    // The card growth tables (dlc/DonoDuel/donoduel_config.json keyed by ConfigID, plus the skill
    // tree / leader skill / hero skill lists the growth requests write into).
    public static Dictionary<uint, DonoDuelConfigExcel> DonoDuelConfigData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelSkillTreeExcel> DonoDuelSkillTreeData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelLeaderSkillExcel> DonoDuelLeaderSkillData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelHeroCardSkillExcel> DonoDuelHeroCardSkillData { get; private set; } = [];
    // The rogue turntable reward pool (dlc/DonoDuel/donoduel_catch_room_minigame.json), keyed by ID and
    // rolled by weight; the wheel slot shown to the player is the row's Sort.
    public static Dictionary<uint, DonoDuelCatchRoomMiniGameExcel> DonoDuelMiniGameData { get; private set; } = [];
    // The rogue run graph: catch rooms (donoduel_catch_battle.json) keyed by CatchID, the
    // per-room card pools (donoduel_catch_room_heropool.json) keyed by PoolID and matched by
    // (Phase, RoomType), and the exploration rooms (DonoRogue/donorogue_level.json) keyed by
    // LevelID joining the two. The room settlement rolls the catch itself from these tables.
    public static Dictionary<uint, DonoDuelCatchBattleExcel> DonoDuelCatchBattleData { get; private set; } = [];
    public static Dictionary<uint, DonoDuelCatchRoomHeropoolExcel> DonoDuelHeropoolData { get; private set; } = [];
    public static Dictionary<uint, DonoRogueLevelExcel> DonoRogueLevelData { get; private set; } = [];
    public static Dictionary<uint, DlcChapterLevelExcel> DlcChapterLevelData { get; private set; } = [];
    public static Dictionary<uint, DlcChapterExcel> DlcChapterData { get; private set; } = [];
    public static Dictionary<uint, DlcGoodsExcel> DlcGoodsData { get; private set; } = [];
    public static Dictionary<uint, List<DlcGoodsExcel>> DlcGoodsByShop { get; private set; } = [];
    public static Dictionary<uint, DlcMissionExcel> DlcMissionData { get; private set; } = [];
    public static Dictionary<uint, List<DlcMissionExcel>> DlcMissionsByType { get; private set; } = [];
    public static Dictionary<uint, DlcMissionTimeExcel> DlcMissionTimeData { get; private set; } = [];
    public static Dictionary<(uint ActivityId, uint DayId), SignDayExcel> SignDayData { get; private set; } = [];
    public static Dictionary<(uint ActivityId, uint DayId), SignShortExcel> SignShortData { get; private set; } = [];
    public static Dictionary<uint, SevenDayInfoExcel> SevenDayInfoData { get; private set; } = [];
    public static Dictionary<(uint ActId, uint DayId), SevenDayAchievementExcel> SevenDayAchievementData { get; private set; } = [];
    public static Dictionary<(uint ActId, uint AwardId), SevenDayAwardExcel> SevenDayAwardData { get; private set; } = [];
    public static Dictionary<uint, VigourSupplyExcel> VigourSupplyData { get; private set; } = [];
    public static Dictionary<uint, PlayerChargeExcel> PlayerChargeData { get; private set; } = [];
    public static Dictionary<uint, CoinPusherRewardExcel> CoinPusherRewardData { get; private set; } = [];
    public static Dictionary<uint, SeasonBossLevelExcel> SeasonBossLevelData { get; private set; } = [];
    public static Dictionary<uint, ActivityQuestExcel> ActivityQuestData { get; private set; } = [];

    /// <summary>achievement/achievement.json keyed by ID (attribute group AttrIds.Achievement.GroupId).</summary>
    public static Dictionary<uint, AchievementExcel> AchievementData { get; private set; } = [];

    /// <summary>house/coinpusher/coinpusher_achv.json keyed by ID (the arcade task panel's list).</summary>
    public static Dictionary<uint, CoinPusherAchvExcel> CoinPusherAchvData { get; private set; } = [];
    public static Dictionary<uint, AlienDefenseExcel> AlienDefenseData { get; private set; } = [];
    public static Dictionary<uint, Dictionary<uint, AlienDefenseAwardExcel>> AlienDefenseAwardData { get; private set; } = [];

    // Alien defence companion / support-girl rosters. SeasonID is the act id (AlienDefenseConfig.lua:78),
    // so both are nested per season and the inner key is the roster id the client sends back in
    // tbUnlockPal / tbUnlockGirl (uw_mow_team_unlock.lua:12, umg_mow_girl_unlock.lua:15).
    public static Dictionary<uint, Dictionary<uint, AlienDefensePalExcel>> AlienDefensePalData { get; private set; } = [];
    public static Dictionary<uint, Dictionary<uint, AlienDefenseGirlExcel>> AlienDefenseGirlData { get; private set; } = [];
    public static Dictionary<uint, PlayerReturnExcel> PlayerReturnData { get; private set; } = [];
    public static Dictionary<uint, PlayerReturnRewardExcel> PlayerReturnRewardData { get; private set; } = [];
    public static Dictionary<(uint ActId, uint DayId), PlayerReturnSignExcel> PlayerReturnSignData { get; private set; } = [];
    public static Dictionary<uint, GachaTryExcel> GachaTryData { get; private set; } = [];
    public static Dictionary<uint, TryGirlExcel> TryGirlData { get; private set; } = [];
    public static Dictionary<uint, CashExcel> CashData { get; private set; } = [];
    public static Dictionary<uint, ShopTabExcel> ShopTabData { get; private set; } = [];
    public static Dictionary<uint, ShopGoodsItem> ShopGoodsData { get; private set; } = [];
    public static Dictionary<ulong, ItemExchangeExcel> ItemExchangeData { get; private set; } = [];
    public static Dictionary<uint, VigorExchangeExcel> VigorExchangeData { get; private set; } = [];
    public static Dictionary<uint, SilverExchangeExcel> SilverExchangeData { get; private set; } = [];

    /// <summary>
    /// Item box contents (item/box.json), grouped by box index: one index really does carry several
    /// rows, so this is a list per index rather than a row per id. Populated by
    /// <c>BoxExcel.Loaded()</c>; see that class for the client-side semantics of the award types.
    /// </summary>
    public static Dictionary<uint, List<BoxExcel>> BoxData { get; private set; } = [];

    /// <summary>
    /// 妮塔大富翁 tree house rows (activity/nita_monopoly/tree.json), in EX order. The client
    /// addresses them by ipairs index and uses that index as the attribute sid offset
    /// (TreeRewardGot + index), so a list is the only usable shape here; see
    /// <c>NitaTreeExcel</c> for the table layout.
    /// </summary>
    public static List<NitaTreeExcel> NitaTreeData { get; private set; } = [];

    /// <summary>
    /// 抢滩登陆 seasons (fun_timed/fun_timed.json), keyed by <c>Session</c>. A DlcFuncSevendays button
    /// carries that number in <c>DlcFuncConfId.Sevendays</c> and the client refuses to open the panel
    /// when GetSession returns nil (DLC/DLCFunc/DlcFuncSevendays.lua:18-21),
    /// </summary>
    public static Dictionary<uint, FunTimedSessionExcel> FunTimedSessionData { get; private set; } = [];

    /// <summary>
    /// 抢滩登陆 levels (fun_timed/level.json). The settlement branch needs the per star reward tiers;
    /// the entered level id itself is not part of any chapter table.
    /// </summary>
    public static Dictionary<uint, FunTimedLevelExcel> FunTimedLevelData { get; private set; } = [];

    /// <summary>
    /// 世界BOSS acts (challenge/worldboss/worldbosslist.json). Keyed by the (Id, ActID) pair - the same
    /// Id carries one row per act, so a dictionary on Id alone would collapse them.
    /// </summary>
    public static List<WorldBossActExcel> WorldBossActData { get; private set; } = [];

    /// <summary>世界BOSS stage id -> battle level id (challenge/worldboss/worldbossleveldes.json).</summary>
    public static Dictionary<uint, WorldBossLevelDesExcel> WorldBossLevelDesData { get; private set; } = [];

    /// <summary>世界BOSS battle rows (challenge/worldboss/worldbosslevel.json), keyed by LevelID.</summary>
    public static Dictionary<uint, WorldBossLevelExcel> WorldBossLevelData { get; private set; } = [];

    /// <summary>蔬菜大战 periods (farmwar/farmwarout.json), keyed by ID.</summary>
    public static Dictionary<uint, FarmWarActExcel> FarmWarActData { get; private set; } = [];

    /// <summary>蔬菜大战 combat stages (farmwar/farmwarout_level.json), keyed by ID.</summary>
    public static Dictionary<uint, FarmWarLevelExcel> FarmWarLevelData { get; private set; } = [];

    /// <summary>星祈心愿 periods (dlc/starwish/main.json), keyed by ID (= period; only 1 exists).</summary>
    public static Dictionary<uint, StarWishMainExcel> StarWishMainData { get; private set; } = [];

    /// <summary>星祈心愿 story quests (dlc/starwish/storyquest.json), keyed by ID (101..704).</summary>
    public static Dictionary<uint, StarWishStoryQuestExcel> StarWishStoryQuestData { get; private set; } = [];

    /// <summary>星祈心愿 story pages (dlc/starwish/storyquest_page.json), keyed by page id 1..7.</summary>
    public static Dictionary<uint, StarWishStoryPageExcel> StarWishStoryPageData { get; private set; } = [];

    /// <summary>星祈心愿 score award tiers (dlc/starwish/score_award.json), keyed by ID 1..10.</summary>
    public static Dictionary<uint, StarWishScoreAwardExcel> StarWishScoreAwardData { get; private set; } = [];

    /// <summary>星祈心愿 tree parts (dlc/starwish/jigsaw.json), keyed by ID 1..21.</summary>
    public static Dictionary<uint, StarWishJigsawExcel> StarWishJigsawData { get; private set; } = [];

    /// <summary>适格启示录 schedules (activity/jigsaw_puzzle/jigsawpuzzle.json), keyed by ScheduleID.</summary>
    public static Dictionary<uint, JigsawScheduleExcel> JigsawScheduleData { get; private set; } = [];

    /// <summary>适格启示录 pieces (jigsawpuzzle_piece.json), keyed by ScheduleID*1000+PieceId.</summary>
    public static Dictionary<uint, JigsawPieceExcel> JigsawPieceData { get; private set; } = [];

    /// <summary>背包肉鸽 periods (dlc/bag_rogue/timelist.json), keyed by Id 1..3.</summary>
    public static Dictionary<uint, BagRogueScheduleExcel> BagRogueScheduleData { get; private set; } = [];

    /// <summary>背包肉鸽 route nodes (dlc/bag_rogue/mapnode.json), keyed by the globally unique NodeId.</summary>
    public static Dictionary<uint, BagRogueNodeExcel> BagRogueNodeData { get; private set; } = [];

    /// <summary>背包肉鸽 battle stages (dlc/bag_rogue/level.json), keyed by ID.</summary>
    public static Dictionary<uint, BagRogueLevelExcel> BagRogueLevelData { get; private set; } = [];

    /// <summary>背包肉鸽 endings (dlc/bag_rogue/story.json), keyed by Id.</summary>
    public static Dictionary<uint, BagRogueStoryExcel> BagRogueStoryData { get; private set; } = [];

    /// <summary>背包肉鸽 adventure options (dlc/bag_rogue/adventure.json), keyed by option Id.</summary>
    public static Dictionary<uint, BagRogueAdventureExcel> BagRogueAdventureData { get; private set; } = [];

    /// <summary>大富翁棋盘期号 (challenge/chess/chess.json), keyed by ID (1..4).</summary>
    public static Dictionary<uint, ChessActivityExcel> ChessActivityData { get; private set; } = [];

    /// <summary>大富翁每期每图的配置与名次奖励 (challenge/chess/chess_map.json), keyed by ID*100+MapId.</summary>
    public static Dictionary<uint, ChessMapExcel> ChessMapData { get; private set; } = [];

    /// <summary>应援票档位奖励 (challenge/chess/chess_richmanawards.json), keyed by Id 1..8.</summary>
    public static Dictionary<uint, ChessRichmanAwardExcel> ChessRichmanAwardData { get; private set; } = [];

    /// <summary>大富翁四张棋盘定义 (dlc/richman/def/map_def.json), keyed by board id 1..4.</summary>
    public static Dictionary<uint, RichmanMapDefExcel> RichmanMapDefData { get; private set; } = [];

    /// <summary>恋爱线地图定义 (dlc/richman_love/def/map_def.json), keyed by id (1).</summary>
    public static Dictionary<uint, RichmanLoveMapDefExcel> RichmanLoveMapDefData { get; private set; } = [];

    /// <summary>恋爱线结局表 (dlc/richman_love/def/12_end.json), keyed by ending id.</summary>
    public static Dictionary<uint, RichmanLoveEndExcel> RichmanLoveEndData { get; private set; } = [];

    /// <summary>2D 肉鸽期号表 (dlc/dlc1/rogue/activities.json), keyed by period id (1/2/3).</summary>
    public static Dictionary<uint, RogueActivityExcel> RogueActivityData { get; private set; } = [];

    /// <summary>2D 肉鸽节点表 (dlc/dlc1/rogue/map1.json), keyed by node id.</summary>
    public static Dictionary<uint, RogueMapExcel> RogueMapData { get; private set; } = [];

    /// <summary>2D 肉鸽关卡表 (dlc/dlc1/rogue/level.json), keyed by level id.</summary>
    public static Dictionary<uint, RogueLevelExcel> RogueLevelData { get; private set; } = [];

    /// <summary>2D 肉鸽商店池 (dlc/dlc1/rogue/shop.json), keyed by Periods (= activities.ShopID).</summary>
    public static Dictionary<uint, RogueShopExcel> RogueShopData { get; private set; } = [];

    /// <summary>异绪消解期号表 (girlrush/girlrushlist.json). One row today, keyed by <c>Id</c>;
    /// DlcFuncGirlrush carries it in <c>DlcFuncConfId.Girlrush</c>.</summary>
    public static Dictionary<uint, GirlRushSeasonExcel> GirlRushSeasonData { get; private set; } = [];

    /// <summary>异绪消解关卡表 (girlrush/girlrushlevel.json), keyed by stage id.</summary>
    public static Dictionary<uint, GirlRushLevelExcel> GirlRushLevelData { get; private set; } = [];

    /// <summary>异绪消解任务板 (girlrush/girlrushaward.json), keyed by SeasonId*1000+Index.</summary>
    public static Dictionary<uint, GirlRushAwardExcel> GirlRushAwardData { get; private set; } = [];
}

public static class GameResourceTemplateId
{
    public static ulong FromGdpl(uint genre, uint detail, uint particular, uint level) =>
        ((ulong)level << 48) | ((ulong)particular << 32) | ((ulong)detail << 16) | genre;

    public static ulong FromGdpl(IReadOnlyList<uint> gdpl) =>
        gdpl.Count >= 4 ? FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]) : 0;
}
