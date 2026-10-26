using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.AlienDefense;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.ClimbTower;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.GameServer.Server.CallGS.Handlers.Dlc;
using MikuSB.GameServer.Server.CallGS.Handlers.Girl;
using MikuSB.GameServer.Server.CallGS.Handlers.Online;
using MikuSB.GameServer.Server.CallGS.Handlers.Weapon;
using MikuSB.GameServer.Server.Packet.Send.Friend;
using MikuSB.GameServer.Server.Packet.Send.Login;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(CmdIds.ReqLogin)]
public class HandlerReqLogin : Handler
{
    private static readonly Logger Logger = new("ReqLogin");
    private const int SupportCardLoginSplitThreshold = 2000;
    private const int ResourceWaitMilliseconds = 60_000;

    // Attribute slot of the 荣誉冲突 (Defend) group (AttrIds.Defend.Gid) that has no client writer,
    // see Defend.lua:10-31. The wave counter (sid 2) and the adventure score (sid 5) used to be
    // bootstrapped here with test values; DefendSettlement now records both from real clears, so the
    // login-time seed was removed (it masked real progress behind a permanent 999).
    private const uint DefendActIdSid = 0;

    private static string? ExtractSdkAuthToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var normalized = Uri.UnescapeDataString(token).Trim();
            var padding = normalized.Length % 4;
            if (padding > 0)
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("authToken", out var authToken)
                ? authToken.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// uid carried by the same SDK blob <see cref="ExtractSdkAuthToken"/> reads its token from. The CN
    /// client quotes the whole <c>authInfo</c> object back in <c>ReqLogin.token</c>, so when the stored
    /// <c>ComboToken</c> has drifted (see <c>AccountData.SyncTokenCache</c>) this is the only identity
    /// left in the packet. Returns 0 when the payload is not one of those blobs.
    /// </summary>
    private static int ExtractSdkAuthUid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return 0;

        try
        {
            var normalized = Uri.UnescapeDataString(token).Trim();
            var padding = normalized.Length % 4;
            if (padding > 0)
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("uid", out var uid))
                return 0;

            var raw = uid.ValueKind == JsonValueKind.Number ? uid.GetRawText() : uid.GetString();
            return int.TryParse(raw, out var parsed) ? parsed : 0;
        }
        catch
        {
            return 0;
        }
    }

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = ReqLogin.Parser.ParseFrom(data);
        var sdkAuthToken = ExtractSdkAuthToken(req.Token);
        var account = AccountData.GetAccountByComboToken(req.Token)
                      ?? AccountData.GetAccountByDispatchToken(req.Token)
                      ?? AccountData.GetAccountByComboToken(sdkAuthToken ?? "")
                      ?? AccountData.GetAccountByDispatchToken(sdkAuthToken ?? "");
        if (account == null && ConfigManager.Config.ServerOption.AllowStaleTokenLogin)
        {
            // Token desync fallback: the client is quoting an SDK blob we minted for a known uid, but
            // the stored ComboToken moved on (another login rotated it, or a cached account row was
            // written back over it). Rejecting here only strands the client on "识别中", and the SDK
            // blob is not a credential worth protecting on a private server - so log it loudly and
            // let the uid decide. See docs/progress for the incident that introduced this.
            var staleUid = ExtractSdkAuthUid(req.Token);
            if (staleUid != 0)
            {
                account = AccountData.GetAccountByUid(staleUid);
                if (account != null)
                    Logger.Warn($"Stale token for uid={staleUid} ({account.Username}): accepted via " +
                                $"authInfo uid fallback. stored={account.ComboToken}, " +
                                $"clientAuthToken={sdkAuthToken}. " +
                                "Set ServerOption.AllowStaleTokenLogin=false to reject instead.");
            }
        }

        if (account == null)
        {
            Logger.Warn($"Rejected login: provider={req.Provider}, token={req.Token}, authToken={sdkAuthToken}");
            await connection.SendPacket(CmdIds.NtfLogout);
            return;
        }
        if (!ResourceManager.IsLoaded)
            // resource manager not loaded, return
            return;
        var prev = Listener.GetActiveConnection(account.Uid);
        if (prev != null)
        {
            await connection.SendPacket(CmdIds.NtfLogout);
            prev.Stop();
        }

        await CompleteLogin(connection, account, false);
    }

    /// <summary>
    /// Shared login sequence for ReqLogin and ReqReconnect. Reconnect answers with its own response
    /// packet, but the player load, attribute pinning and follow-up notifications are identical.
    /// </summary>
    public static async Task CompleteLogin(Connection connection, AccountData account, bool reconnect)
    {
        // The client connects (and reconnects) as soon as the TCP listener is up, which is before the
        // resource manager has finished loading. Returning early here used to leave the connection
        // accepted but without a player: every later packet then failed with a NullReferenceException
        // and the client looked permanently stuck on "reconnecting". Wait for the tables instead.
        for (var waited = 0; !ResourceManager.IsLoaded && waited < ResourceWaitMilliseconds; waited += 250)
            await Task.Delay(250);

        if (!ResourceManager.IsLoaded)
        {
            Logger.Warn($"Resources still not loaded after {ResourceWaitMilliseconds} ms, " +
                        $"dropping the connection for uid={account.Uid}; the client will retry.");
            connection.Stop();
            return;
        }

        var prevConnection = Listener.GetActiveConnection(account.Uid);
        if (prevConnection != null && prevConnection != connection)
            prevConnection.Stop();

        connection.State = SessionStateEnum.WAITING_FOR_LOGIN;
        var pd = DatabaseHelper.GetInstance<PlayerGameData>(account.Uid);
        if (pd != null)
            pd.CompleteAllQuestLevels = DatabaseHelper.LoadCompleteAllQuestLevels(account.Uid);
        connection.Player = pd == null ? new PlayerInstance(account.Uid) : new PlayerInstance(pd);
        if (connection.Player.Data.EnsureDisplayName())
            DatabaseHelper.UpdateInstance(connection.Player.Data);

        // Level progress: BuildPlayerAttr() (and with it RemoveLegacyLevelUnlocks()) only runs while
        // PacketRspLogin serialises the player, which is later than this point - so the legacy sweep
        // has to happen explicitly first, or it would delete the very attributes seeded below.
        // Seeding writes real rows into PlayerGameData.Attrs (the only source
        // PlayerAttributes.SyncTo(Proto.Player) reads) and persists them, so the completion UI is
        // correct from the first frame instead of after the player clears some level by hand.
        // A player restored from the database is still half-built at this point: its managers are
        // created inside OnEnterGame() a few lines below, so anything touching QuestManager has to
        // build them first. Without this the sweep on the next line threw NullReferenceException at
        // exactly this spot for every returning account, which aborted CompleteLogin before the login
        // response was sent - the client just sat on its permission check forever.
        await connection.Player.EnsureManagersReady();
        connection.Player.QuestManager.RemoveLegacyLevelUnlocks();
        var seededLevels = connection.Player.QuestManager.SeedAllLevelStates();
        if (seededLevels > 0)
        {
            DatabaseHelper.SaveDatabaseType(connection.Player.Data);
            Logger.Info($"Seeded {seededLevels} cleared level state(s) for uid={account.Uid}.");
        }

        // Economy rework: the lobby bootstrap used to pin every currency id to 999_999_999 with a
        // raise-only merge, so nothing could ever be spent. Hand out the starting balances once per
        // account instead (CurrencyBootstrap) and let the shop/gacha/box handlers charge for real.
        var seededCurrencies = CurrencyBootstrap.Seed(connection.Player);
        if (seededCurrencies > 0)
        {
            DatabaseHelper.SaveDatabaseType(connection.Player.Data);
            Logger.Info($"Seeded {seededCurrencies} starting currency balance(s) for uid={account.Uid}.");
        }

        // Stamina recovers in real time (360 s per point, cap from player/levels.json). The value and
        // its clock are persisted, so an offline player keeps accruing between logins; the first login
        // only starts the clock.
        var recoveredVigor = VigorService.Regen(connection.Player);
        if (recoveredVigor > 0)
        {
            DatabaseHelper.SaveDatabaseType(connection.Player.Data);
            Logger.Info($"Recovered {recoveredVigor} vigor for uid={account.Uid} (now {connection.Player.Data.Vigor}).");
        }

        connection.DebugFile = Path.Combine(ConfigManager.Config.Path.LogPath, "Debug/", $"{account.Uid}/",
            $"Debug-{DateTime.Now:yyyy-MM-dd HH-mm-ss}.log");
        await connection.Player.OnEnterGame();
        // Every mode a DLC era adds keeps its "which period am I" id in an attribute slot. Seed all of
        // them from the era being served (and clear the ones that era does not have, so switching to
        // an older period cannot leave a later module's entry behind).
        ApplyPinnedEra(connection.Player, null);
        // 多诺-巅峰对决: the mode keeps its deck in attribute group 185 and the card slots are only
        // ever written by the server (the client has no "add card" request at all - see DonoDuelBag).
        // Accounts that already claimed the story missions before that reward path existed have an
        // empty deck and cannot start a single battle, so repair them before the login snapshot is
        // serialized.
        DonoDuelBag.EnsureStarterDeck(connection.Player);
        // 进击行动 (assault ops, DLC35 "Assaultops" = online id 106): the season id, the formation in
        // use and the whole chip bag live in attribute group 183 and are only ever written by the
        // server (see AssaultOpsBag). An account without them sees no mission list and cannot assemble
        // a formation, because the level entry is gated on a non-empty formation. Seeded before the
        // login snapshot is serialized so the first attribute sync already carries the bag.
        AssaultOpsBag.EnsureSeeded(connection.Player);
        // Roll the monthly sign-in status back to "pending" when the stored stamp is not today.
        // Sign.CheckSignPop (Activity/Sign.lua:278) only pops the window while the status is below 2,
        // and the client never writes these attributes itself, so without this a player could sign in
        // exactly once and the window would stop appearing. Done before PacketRspLogin serializes the
        // player so the first attribute sync already carries the reset value.
        ActivitySignService.RollDailyState(connection.Player);
        // 回归活动 (activities.txt row 560, Class "player_return"): the whole module hangs off
        // attributes the client never writes (act id at (0, 15), start stamp and Sign bits in group
        // 50), so seed the era's return activity, advance the signed-day counter and mark the open
        // achievement stages before the login snapshot is serialized.
        PlayerReturnService.RollDailyState(connection.Player);
        // DLC 任务板 (水城观光 / 荣誉冲突): the client only lists a refreshing row when its mission id
        // sits in the day/week activation slots of attribute group 15 (DLC_Logic.lua:155-164), which no
        // other part of the server writes. Fill them before the login snapshot so the board shows the
        // daily/weekly rows (including the 每日登录 task) instead of only the one-shot ones.
        DlcMissionActiveService.RollDailyState(connection.Player);
        // 武器配件解锁: the unlock bit of a weapon's part award lives in attribute group 112, which
        // the client never writes, and the arms panel only draws the claim button once that bit is
        // set (UMG/Arms/Widgets/uw_arms_partslock.lua:15). Weapons levelled before that line existed
        // have no bit at all, so grant it here - also before the login snapshot is serialized.
        if (WeaponPartAward.RepairUnlocked(connection.Player))
        {
            DatabaseHelper.SaveDatabaseType(connection.Player.Data);
            Logger.Info($"Repaired weapon part award unlock bits for uid={account.Uid}.");
        }
        // 多诺任务链 (donoduel_story_task.json): the missions whose objective is an action this server
        // cannot observe ("前往进行潜能提升", "和安卡希雅聊聊", "激活1个探索加速包" - none of those has a
        // request of its own, the DonoDuel_Upgrade* / DonoDuel_StudySkillTree apis do not exist) used to
        // leave the story stuck at mission 104. They are completed here once the previous story mission
        // has been rewarded, which keeps the chapter order intact.
        if (DonoDuelTaskState.AdvanceStoryActions(connection.Player, new NtfSyncPlayer()) > 0)
        {
            DonoDuelTaskState.Save(connection.Player);
            Logger.Info($"Advanced the Donoduel story mission chain for uid={account.Uid}.");
        }
        // DLC 任务板 - 登录型任务 (dlc_mission.json Type 11, e.g. 21244 "昨日回首"): the client never
        // reports that trigger - its Event.Logined hook (Achievement_Function.lua:609) only fires types
        // 137 and 350 - so the progress slot in attribute group 14 stayed at zero and the row could never
        // be claimed. Tick it here, before the login snapshot carries the attributes to the client.
        if (DlcMissionActiveService.AdvanceLoginMissions(connection.Player, new NtfSyncPlayer()) > 0)
        {
            DatabaseHelper.SaveDatabaseType(connection.Player.Data);
            Logger.Info($"Advanced the DLC login missions for uid={account.Uid}.");
        }
        // 异星守护 伙伴手册 / 助战少女: the handbook only reads bit0 of (143, 251 + Id) / (143, 551 + Id)
        // and nothing on either side ever wrote it, so every companion and support girl stayed locked
        // however often the player cleared a level. The roster conditions are re-evaluated here so old
        // saves heal on the next login (every clear runs the same repair from the settlement handler).
        if (AlienDefenseUnlockService.GrantUnlocks(connection.Player, null).Changed)
            Logger.Info($"Repaired the alien defense companion unlocks for uid={account.Uid}.");
        connection.Player.Connection = connection;
        var splitSupportCards = connection.Player.InventoryManager.InventoryData.SupportCards.Count > SupportCardLoginSplitThreshold;
        await connection.SendPacket(reconnect
            ? new PacketRspReconnect(connection.Player!, !splitSupportCards)
            : new PacketRspLogin(connection.Player!, !splitSupportCards));
        if (splitSupportCards)
            await SendSupportCardsOnLogin(connection);
        await connection.SendPacket(new PacketNtfCallScript(connection.Player!));
        // Reconnect never re-issues DLCLogic_CheckOpenAct, which is the only other caller of
        // PushDlcWindowOverrides. Shipping just DlcSyncTime + the patches here left every mode that
        // gates on its own local CSV time table (rush / chess / donoduel / festival message / alien
        // defense / season boss) reading the expired dates from its own config after a reconnect.
        // Season boss is the visible casualty: SeasonBossLogic.IsTermOpen returns false, so
        // SeasonBossLogic.OnSettlement (Launch/Classes/SeasonBossLogic.lua:108) silently bails out
        // through a "BeOverdue" message box and the run is never reported to the server at all.
        // The whole override set is idempotent (see PushDlcWindowOverrides), so sending it on a fresh
        // login too -- where DLCLogic_CheckOpenAct will repeat it -- is harmless.
        await PushDlcWindowOverrides(connection);
        await SendDebugLoginState(connection);

        await connection.Player.OnHeartBeat();
        await connection.SendPacket(new PacketNtfUpdateFriend(connection.Player!));
        ApplySavedGirlSkinTypes(connection.Player!);
        await SendGirlSkinTypeOnLogin(connection);
    }

    /// <summary>
    /// Seeds every era-bound module attribute from the DLC era being served. <paramref name="sync"/>
    /// is null at login (the values ride the serialized player) and non-null when the era is switched
    /// on an already connected player, where the caller ships the deltas as a NtfSyncAttr.
    /// </summary>
    internal static void ApplyPinnedEra(PlayerInstance player, NtfSyncPlayer? sync)
    {
        ApplyPinnedDlcActivity(player, sync);
        ApplyPinnedDefendAct(player, sync);
        ApplyPinnedRoleFestivalAct(player, sync);
        ApplyPinnedDonoDuelAct(player, sync);
        ApplyPinnedSeasonBossAct(player, sync);
        ApplyPinnedAlienDefenseAct(player, sync);
        WorldBossService.Seed(player, sync);
        FarmWarActivityService.Seed(player, sync);
        StarWishService.Seed(player, sync);
        BagRogueService.Seed(player, sync);
        ChessActivityService.Seed(player, sync);
        ClimbTowerService.Seed(player, sync);
        RogueService.SeedPinnedOpenStory(player, sync);
        SevenDayService.Seed(player, sync);
        VigourSupplyService.Seed(player, sync);
    }

    /// <summary>
    /// Writes one era attribute, creating it only when the value is non-zero (a module the served era
    /// does not have must not leave a fresh empty row behind) and mirroring a real change into
    /// <paramref name="sync"/>. Returns true when the stored value changed.
    /// </summary>
    private static bool SeedPinnedAttr(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Get(gid, sid);
        if (attr == null)
        {
            if (value == 0)
                return false;

            attr = player.Attributes.GetOrCreate(gid, sid);
        }
        else if (attr.Val == value)
        {
            return false;
        }

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);

        return true;
    }

    /// <summary>
    /// The client resolves the current DLC activity from the attribute (gid 15, sid 1) and only
    /// falls back to the local time window when that attribute is zero. Pinning the activity here
    /// (before PacketRspLogin serializes the player) makes sure the very first attribute sync the
    /// client receives already carries the pinned id, otherwise the expired time windows of every
    /// DLC entry leave the client without a DLC entry point. With no era being served at all (id 0,
    /// neither in Config.json nor from the runtime switch) the slot is cleared instead, so an account
    /// that last logged in under a pin cannot keep a stale era on a server that no longer serves one.
    /// </summary>
    private static void ApplyPinnedDlcActivity(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var pinnedId = DlcRuntimeOverrides.EffectiveDlcId;
        if (!SeedPinnedAttr(player, AttrIds.Dlc.Gid, AttrIds.Dlc.ActIdSid, pinnedId, sync))
            return;

        Logger.Info(pinnedId != 0
            ? $"Pinned DLC activity {pinnedId} for uid={player.Uid}."
            : $"Cleared pinned DLC activity for uid={player.Uid} (pinning off).");
    }

    /// <summary>
    /// 荣誉冲突 (Defend) needs the current act id in attribute (gid 10, sid 0): the client only reads
    /// that slot (DefendLogic.ActId, Defend.lua:313/382/1484/2108) and uses it to index its local
    /// levelaward_list table, so without it the battle pass and target reward pages stay empty.
    /// The wave counter (sid 2, read by DefendLogic.GetMaxWave / GetTargetState) and the max level
    /// score (sid 7) are NOT seeded here any more: DefendSettlement writes them from the real clear
    /// (Defend.lua:1003-1017 sends nWave), and a login-time 999 made every wave-gated target reward
    /// look permanently unlocked.
    /// </summary>
    private static void ApplyPinnedDefendAct(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var actId = DlcRuntimeOverrides.SelectDefendActId();
        if (!SeedPinnedAttr(player, AttrIds.Defend.Gid, DefendActIdSid, actId, sync) || actId == 0)
            return;

        Logger.Info($"Pinned defend act {actId} for uid={player.Uid}.");
    }

    /// <summary>
    /// 心愿寄语 (Dorm / RoleFestival) needs a non-zero act id even though its window is supplied by
    /// the GMRoleFestivalSetId push: RoleFestivalMessage.GetIsInActTime() is just
    /// "GetActId() ~= 0", and the act id lives in attribute (gid 72, sid 1). Without it the entry
    /// stays at FuncState.UnOpen even when the window is current, so pin it at login like the DLC
    /// activity itself.
    /// </summary>
    private static void ApplyPinnedRoleFestivalAct(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var actId = DlcRuntimeOverrides.SelectRoleFestivalActId();
        if (SeedPinnedAttr(player, AttrIds.Activity.RoleFestivalGroupId, AttrIds.Activity.RoleFestivalActSid, actId, sync) && actId != 0)
            Logger.Info($"Pinned role festival act {actId} for uid={player.Uid}.");

        if (actId == 0)
            return;

        // The letter list is driven by (72, 1000 + NpcID) and no client request ever sets that flag
        // (RoleFestivalMessage.GetMessageOpenId only reads it), so an account would keep seeing an
        // empty board. Seeding here - not only when the act id changes - also repairs accounts that
        // logged in before the flag existed.
        var opened = RoleFestivalService.OpenMessages(player, actId);
        if (opened > 0)
            Logger.Info($"Opened {opened} role festival letter(s) of act {actId} for uid={player.Uid}.");
    }

    /// <summary>
    /// 多诺-巅峰对决 (Donoduel) is gated by DlcFuncDonoduel.OnGetTimeInfo ->
    /// DonoDuelConfig:GetSeasonConfig(cfg), which only works while DonoDuel:GetActId() is non-zero
    /// (attribute gid 185, sid 1). Donoduel_ClientCheck also writes the season window, but the gate
    /// is evaluated before that CallGS returns, so seed the season id here as well.
    /// </summary>
    private static void ApplyPinnedDonoDuelAct(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var actId = DlcRuntimeOverrides.SelectDonoDuelSeasonId();
        if (!SeedPinnedAttr(player, AttrIds.DonoDuel.GroupId, AttrIds.DonoDuel.ActivitySid, actId, sync) || actId == 0)
            return;

        Logger.Info($"Pinned Donoduel season {actId} for uid={player.Uid}.");
    }

    /// <summary>
    /// 弑神斗场 (Teamboss) keeps its act id and current term in attributes (gid 18, sid 1 and 2).
    /// SeasonBossLogic.GetCurTimeCfg() returns nil while the act id is zero and
    /// CheckFirstEnter() then indexes that nil, so both are seeded here - the time window pushed by
    /// SeasonBoss_GMChangeTime is useless until the client knows which period it belongs to.
    /// </summary>
    private static void ApplyPinnedSeasonBossAct(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var actId = DlcRuntimeOverrides.SelectSeasonBossActId();
        if (SeedPinnedAttr(player, AttrIds.SeasonBoss.Gid, AttrIds.SeasonBoss.ActivitySid, actId, sync) && actId != 0)
            Logger.Info($"Pinned season boss act {actId} for uid={player.Uid}.");

        // The current term only means something while an act is served; dropping the act has to drop
        // the term too, otherwise the client keeps asking for a period of an era that is gone.
        SeedPinnedAttr(player, AttrIds.SeasonBoss.Gid, AttrIds.SeasonBoss.TermSid, actId == 0 ? 0u : 1u, sync);
    }

    /// <summary>
    /// 异星守护 (Mow) keeps its act id in attribute (gid 143, sid 1). AlienDefense.GetActId() reads
    /// that slot and GetCurTimeConfig() returns nil while it is zero, which dead-ends the whole mode
    /// (entry, level type lookup, score awards) before the AlienDefense_CheckOpenAct push can have
    /// any effect -- the same pin-at-login shape as 弑神斗场 above. The pinned DLC's Mow entry
    /// (DlcFuncConfId Mow=4) doubles as the act id to seed.
    /// </summary>
    private static void ApplyPinnedAlienDefenseAct(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        var actId = DlcRuntimeOverrides.SelectAlienDefenseActId();
        if (!SeedPinnedAttr(player, AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid, actId, sync) || actId == 0)
            return;

        Logger.Info($"Pinned alien defense act {actId} for uid={player.Uid}.");
    }

    /// <summary>
    /// The DLC button is only shown while the client's local config time window is current
    /// (umg_main:ShowBtnDlc -> IsInTime(nEnterStartTime, nCloseEndTime)). Every shipped DLC row has
    /// long expired, so the pinned activity is also pushed through the DLC_SyncTime s2c callback,
    /// which overwrites those four timestamps on the client and re-runs ShowBtnDlc.
    /// </summary>
    public static async Task PushDlcSyncTime(Connection connection)
    {
        var pinnedId = DlcRuntimeOverrides.EffectiveDlcId;
        if (pinnedId == 0)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new JsonArray
        {
            new JsonObject
            {
                ["nId"] = pinnedId,
                ["nStartTime"] = now - 3600,
                ["nEndTime"] = now + 2592000,
                ["nEnterStartTime"] = now - 3600,
                ["nCloseEndTime"] = now + 2592000
            }
        }.ToJsonString();

        Logger.Info($"Pushing DLC_SyncTime for pinned activity {pinnedId}.");
        await CallGSRouter.SendScript(connection, "DLC_SyncTime", payload);
    }

    /// <summary>
    /// Pushes every window override the served DLC needs on top of DLC_SyncTime and re-runs the DLC
    /// entry refresh afterwards. Called when the client asks about the DLC (DLCLogic_CheckOpenAct),
    /// i.e. right before it builds the DLC UI, so all sub features are evaluated against fresh
    /// windows.
    /// </summary>
    public static async Task PushDlcWindowOverrides(Connection connection)
    {
        try
        {
            await PushDlcWindowTimes(connection);
            // And finally the client-side table patches, which no s2c callback can reach at all.
            await PushClientPatches(connection);
        }
        catch (Exception ex)
        {
            // Never let an auxiliary override break the client request it piggybacks on: an
            // unanswered DLCLogic_CheckOpenAct leaves the client on its connection spinner.
            Logger.Warn($"Failed to push DLC window overrides: {ex}");
        }
    }

    /// <summary>
    /// <see cref="PushDlcWindowOverrides"/> for a runtime era switch (see CommandDlc): the same s2c time
    /// tables, then only the client patches whose content is bound to the era. Everything else in
    /// <see cref="PushClientPatches"/> is era-independent: the zbug/online/plot fixes and the idempotent
    /// table stomps (AlienDefenseLevelWindows, DonoDuelArenaWindows, SupplyShop) already ran for this
    /// session and re-sending them only costs the client a large download.
    /// </summary>
    internal static async Task PushDlcEraSwitch(Connection connection)
    {
        try
        {
            await PushDlcWindowTimes(connection);
            await PushDlcEraPatches(connection);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to push DLC era switch for uid={connection.Player?.Uid}: {ex}");
        }
    }

    /// <summary>
    /// The s2c half of <see cref="PushDlcWindowOverrides"/>: every table the client keeps in its own Lua
    /// state and can only learn about through a callback it already registers.
    /// </summary>
    private static async Task PushDlcWindowTimes(Connection connection)
    {
        await PushDlcChapterSyncTime(connection);
        await PushDefendOpenAct(connection);
        await PushShopOpenTime(connection);
        // Modes whose own entry gate reads a local CSV time table: each of these rides an s2c
        // callback the client already registers, so the button stops reporting ui.TxtDLC1Over.
        //
        // Only callbacks that *just* rewrite a time table may be pushed unsolicited - Defend.lua
        // and RoleFestivalMessage.lua do exactly that. Three others do not, and each of them ends
        // by walking the client into the module: AlienDefense_CheckOpenAct calls
        // UI.SafeOpen('MowMain') (AlienDefense.lua:813/818), DonoDuel_ClientCheck calls
        // DonoDuel:EnterBase(true) (DonoDuel.lua:1925) and ChessLogic_CheckOpenAct calls
        // ChessLogic.EntryMainUI() -> UI.Open('ChessActivity') (ChessLogic.lua:156/190). Pushing
        // those here (and at login, see CompleteLogin) stacked three module UIs on top of the DLC
        // page on every entry, which the player had to close one by one - reported 2026-10-06.
        // 异星守护 and 多诺 now get their windows from the client patches
        // (AlienDefenseLevelWindows / DonoDuelArenaWindows), which write the same tables with no
        // UI side effect, and the chess push is dropped outright: DLC 35 has no Richmen feature
        // and every chess period ended long before this DLC era.
        await PushRushTime(connection);
        await PushFestivalMessageTime(connection);
        await PushSeasonBossTime(connection);
        // Last: the DLC_SyncTime callback re-runs ShowBtnDlc, so the sub feature windows above must
        // already be in place when the DLC UI is rebuilt.
        await PushDlcSyncTime(connection);
    }

    /// <summary>
    /// Re-ships only the patches whose content is bound to the DLC era being served, for a runtime era
    /// switch (see CommandDlc). Everything else in <see cref="PushClientPatches"/> is era-independent:
    /// the zbug/online/plot fixes and the idempotent table stomps (AlienDefenseLevelWindows,
    /// DonoDuelArenaWindows, SupplyShop) already ran for this session and re-sending them only costs
    /// the client a large download.
    ///
    /// The three that do depend on the era are the ones that either bake the activation floor into the
    /// payload (GlobalTimeWindows, GachaTryWindows - both must be able to run a second time for a new
    /// era, see their era guards) or read the *current* DLC row at execution time (DlcShopWindows walks
    /// DLC_Logic.GetCurConf().tbShopId). DlcMissionBoard is cheap and rebuilds the mission board windows
    /// from the shipped table, so it rides along to keep the board in step with the new era.
    /// </summary>
    internal static async Task PushDlcEraPatches(Connection connection)
    {
        var floor = DlcRuntimeOverrides.ActivationFloor();
        var era = DlcRuntimeOverrides.EffectiveDlcId;

        await SendClientPatch(
            connection,
            nameof(DlcClientPatches.GlobalTimeWindows),
            DlcClientPatches.GlobalTimeWindowsFor(floor, era));
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcMissionBoard), DlcClientPatches.DlcMissionBoard);
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcShopWindows), DlcClientPatches.DlcShopWindows);
        // Every DlcFunc button reads its window straight from the table of its own module, and
        // DlcFuncBase.lua:52 decides "已结束" from that table before it ever asks about unlock state -
        // so the keys of the NEW era row (DlcFuncConfId) need their tables re-stamped as soon as the era
        // changes, not only at login. The patch reads the era row itself, so there is nothing to bake in.
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcFuncWindowFix), DlcClientPatches.DlcFuncWindowFix);
        // Before DLC 35 the era main screens (Dlc1Main..Dlc34Main) compare their button windows themselves
        // instead of going through DlcFuncBase/IsInTime, so a pinned DLC 6/7 still answers 已结束 on
        // every entry - see DlcLegacyMainWindows. Era-independent (it only rewrites windows that already
        // ended), but re-sent here because the modules it wraps may load after the login push.
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcLegacyMainWindows), DlcClientPatches.DlcLegacyMainWindows);

        var tryActIds = GachaTryService.SelectEraActIds();
        if (tryActIds.Count == 0)
            return;

        // Same pre-stamp as the login push: each try row takes its nStartTime back from the (50,
        // actId * 10) attribute through Activity.GetOpenTime, and the rows of the new era have not been
        // stamped yet (the login pass stamped the rows of the era that was current then).
        var player = connection.Player;
        if (player != null)
        {
            var sync = new NtfSyncPlayer();
            foreach (var actId in tryActIds)
                Activity_Refresh.StampOpenWindow(player, actId, sync);

            await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        }

        await SendClientPatch(
            connection,
            nameof(DlcClientPatches.GachaTryWindows),
            DlcClientPatches.GachaTryWindowsFor(floor, era));
    }

    /// <summary>
    /// Ships the client-side Lua patches (see DlcClientPatches) over the GM script channel. They
    /// rewrite local CSV-derived tables that no s2c callback can reach, and have to be re-sent after
    /// every (re)login because the client re-runs AchievementDLC.LoadConfig() on its Logined event.
    /// </summary>
    private static async Task PushClientPatches(Connection connection)
    {
        // Infrastructure first: Misc/EventSystem.lua dispatches every listener of an event in a bare
        // loop with no pcall, so a single released node (决战哈姆's kill node keeps its death listener)
        // aborts the whole dispatch and the run can no longer be cleared. Install the error isolation
        // before any other patch registers or fires an event.
        await SendClientPatch(connection, nameof(DlcClientPatches.EventSystemGuard), DlcClientPatches.EventSystemGuard);
        // First: every "activity has ended" check in the client funnels through the global IsInTime
        // helper, so relaxing it up front also covers the modules that have no patch of their own
        // (弑神斗场 levels, 先行试用 / 角色试玩, gacha pools).
        await SendClientPatch(
            connection,
            nameof(DlcClientPatches.GlobalTimeWindows),
            DlcClientPatches.GlobalTimeWindowsFor(
                DlcRuntimeOverrides.ActivationFloor(),
                DlcRuntimeOverrides.EffectiveDlcId));
        // Depends on GlobalTimeWindows (it reuses MikuRawIsInTime), and must run before the client builds
        // its login popup queue - otherwise every expired activity splash pops at once.
        await SendClientPatch(connection, nameof(DlcClientPatches.ActivityFaceQuiet), DlcClientPatches.ActivityFaceQuiet);
        // Main-screen DLC button: its countdown widget re-enters ShowBtnDlc as soon as the deadline it
        // is handed has passed, which recursed into a 363591 frame stack overflow once the relaxed
        // IsInTime let the expired DLC window through (2026-10-06 11:25).
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcWindowFix), DlcClientPatches.DlcWindowFix);
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcMissionBoard), DlcClientPatches.DlcMissionBoard);
        // 补给商店: the test server's flat-rate shop tables, under fresh ids so they cannot shadow the
        // release rows they were copied from. Self-contained: the payload carries both CSVs verbatim
        // and re-runs ShopLogic's own loaders against them, so no pak and no client file is involved.
        // Merged into the tables Shop.lua:1080-1081 built at startup, so it has to run after the client
        // required Shop.Shop (Preload.lua:180) - i.e. anywhere in this list.
        // [!] It must stay BEFORE DlcShopWindows: merging rebuilds tbShopTabConf from scratch, which
        // would throw away the nBegin/nEnd overrides and the string-key metatable that patch installs
        // on the very same table (DlcShopWindows.lua:3).
        await SendClientPatch(connection, nameof(DlcClientPatches.SupplyShop), DlcClientPatches.SupplyShop);
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcShopWindows), DlcClientPatches.DlcShopWindows);
        await SendClientPatch(connection, nameof(DlcClientPatches.DonoDuelArenaWindows), DlcClientPatches.DonoDuelArenaWindows);
        // Same module: a base position persisted by a previous broken entry makes the player drop
        // through the floor on every later entry, so it is dropped once (DonoDuel_ClientCheck pushes
        // this again where DonoDuel is guaranteed to be loaded).
        await SendClientPatch(connection, nameof(DlcClientPatches.DonoDuelBaseSpawn), DlcClientPatches.DonoDuelBaseSpawn);
        // 异星守护: the Mow level table is a plain global that the client may not have required yet at
        // login time, in which case this push is a harmless no-op - AlienDefense_CheckOpenAct re-pushes
        // it once the module is guaranteed to be loaded.
        await SendClientPatch(connection, nameof(DlcClientPatches.AlienDefenseLevelWindows), DlcClientPatches.AlienDefenseLevelWindows);
        // 抢滩登陆 (Sevendays): same story as 异星守护 - FunTimed is a plain global that may not be
        // required yet at login time (this push is then a no-op), so the patch also re-stamps from
        // FunTimed.Load and from DLC_Logic.InitFuncObj.
        await SendClientPatch(connection, nameof(DlcClientPatches.FunTimedWindows), DlcClientPatches.FunTimedWindows);
        // 异绪消解 (GirlRush): its single season shipped with a 2025-05 window that the era gate keeps
        // closed forever, and the client compares the parsed nStartTime/nEndTime itself. Same re-stamp
        // strategy as 抢滩登陆 - no-op when GirlRushConfig is not loaded yet, plus wrappers on the two
        // config loaders and on DLC_Logic.InitFuncObj.
        await SendClientPatch(connection, nameof(DlcClientPatches.GirlRushWindows), DlcClientPatches.GirlRushWindows);
        // Window of every DLC page button that has no patch of its own (Worldboss / Farmwar / Celebrate /
        // Puzzle / Former / Event / Onlinesea / Onlinepurge / Rush): DlcFuncBase.lua:52 reads each one
        // from that module's shipped table and reports Over before unlock state is consulted, so nothing
        // but re-stamping those tables re-opens them. Driven by the pinned era row, so it also has to be
        // re-sent on an era switch - see PushDlcEraPatches.
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcFuncWindowFix), DlcClientPatches.DlcFuncWindowFix);
        // The pre-common era main screens (Dlc1Main..Dlc34Main) never ask DlcFuncBase or IsInTime: they
        // compare each button's table window with now themselves (umg_dlc6_main.lua:187-190), which is why
        // a pinned DLC 6/7 still shows 已结束 everywhere. Wraps the six accessors those screens read and
        // rewrites only windows that have already ended.
        await SendClientPatch(connection, nameof(DlcClientPatches.DlcLegacyMainWindows), DlcClientPatches.DlcLegacyMainWindows);
        // Cartoons: the framework helper hides only the top Stack UI, so a cartoon played from a panel
        // that sits on top of the dorm lets the dorm HUD poke through its four corners (entering 心意寄语
        // stacks Dlc35Main -&gt; Dorm -&gt; message panel). Misc/UE4.lua is required by Preload.lua:17, so
        // PlayPlot is already defined here.
        await SendClientPatch(connection, nameof(DlcClientPatches.PlotHideAllWindows), DlcClientPatches.PlotHideAllWindows);
        // 先行试用: the DLC page's Try button takes its window from GachaTry.GetTimeConf over the DLC's
        // TryID groups, and DLC 35's groups (561/576) hold 38 try activities whose windows all ended in
        // March 2026 - so DlcFuncBase.lua:52 reads the button as FuncState.Over before it ever asks
        // about unlock state, and the page itself would list a single entry. The patch re-opens every
        // row of those groups that belongs to the pinned era. Sent at login too, where the stamping of
        // (50, actId * 10) matters but the table patch itself is a no-op because Activity is not loaded
        // yet; PushDlcWindowOverrides re-pushes it on every DLC page entry.
        var tryActIds = GachaTryService.SelectEraActIds();
        if (tryActIds.Count > 0)
        {
            // Stamp the attributes first: the patch reads each one back with Activity.GetOpenTime and
            // uses it as that row's nStartTime, so every activity is already "in sync" the first time
            // the player opens the page (Quest_Refresh otherwise reports each one closed once, before
            // its refresh lands, and the page then hides it).
            var tryPlayer = connection.Player;
            if (tryPlayer != null)
            {
                var trySync = new NtfSyncPlayer();
                foreach (var tryActId in tryActIds)
                    Activity_Refresh.StampOpenWindow(tryPlayer, tryActId, trySync);

                await tryPlayer.SendPacket(CmdIds.NtfSyncAttr, trySync);
            }

            await SendClientPatch(
                connection,
                nameof(DlcClientPatches.GachaTryWindows),
                DlcClientPatches.GachaTryWindowsFor(
                    DlcRuntimeOverrides.ActivationFloor(),
                    DlcRuntimeOverrides.EffectiveDlcId));
        }

        // 决战哈姆 / 冲关啦: BP_WPZBugRunMgr.lua:11-23 gates the mode's whole client preload block
        // behind "if not self:HasAuthority()", which is always false in this standalone-hosted world, so
        // the patch replays it for every PartyGo_ level. WPInterface is a startup global (Preload.lua:254
        // -> WaterPark/WaterParkLoad.lua:38 -> GameBaseInit.lua:16), so the hook is in place here.
        await SendClientPatch(connection, nameof(DlcClientPatches.PartyZBugPreload), DlcClientPatches.PartyZBugPreload);
        // 决战哈姆: BP_WPItemPoint.lua:249 calls a self:OnRep_Item() that exists neither in Lua nor in
        // the blueprint, but only when the world is standalone - so crates are never recycled after a
        // pickup here (the exception aborts the rest of BP_WP_Item.lua:268 PlayerCollision_MultiCast).
        // Reuses the same PartyGo_ level hook; the module is only in package.loaded once the item point
        // blueprint has been created, hence the retry loop.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugItemFix), DlcClientPatches.ZBugItemFix);
        // 决战哈姆 host fix: the party level's challenge scaling reads gameMode.RealMaxPlayerCount,
        // which only the C++ room-data callback would fill - and ReqRoomStart(2001)/RspRoomStart(2002)
        // are never answered by this server, so it stays 0 and ZBugMgr:SetMonsterStrengthLevel(0) is
        // called when the 7s pre-ready countdown ends (monsters show 999/99999 HP instead of 2000).
        // The patch writes the real player count before that countdown fires.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostFix), DlcClientPatches.ZBugHostFix);
        // 决战哈姆 listen-host character fix: BP_WPCharacter.lua:138 initialises the Q character only
        // on the non-authority side ("真实玩家和怪物客户端在BeginPlay的这里初始化属性"), so in a
        // listen-server match the host's own pawn - which IS the authority - never runs InitZBugChar()
        // and plays uninitialised (cannot attack, HP 999/99999) while the guest is fine.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugCharFix), DlcClientPatches.ZBugCharFix);
        // Listen host treated as standalone (2026-10-08, experimental): the game only ever wrote the
        // standalone and dedicated-server world states, so every "not HasAuthority() or
        // WorldIIsStandalone()" client-initialisation gate is skipped on a listen host - including
        // BP_WPCharacter.lua:62-65 which fires PlayerClientBeginPlay. WPGameMgr.WorldIIsStandalone is
        // pure Lua and therefore the one place this can be corrected from a patch. No-op for
        // standalone worlds, pure guests and real dedicated servers.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugStandaloneFix), DlcClientPatches.ZBugStandaloneFix);
        // Listen-host weapon init: OnRep_PlayerWeapon() is the weapon's client-side init (it is what
        // fills the magazine at BP_WPCharacter.lua:2592) and it is only ever reached through replica
        // paths - the RepNotify OnPsRepTeamID at BP_WPCharacter.lua:3081 and the
        // "if not self:HasAuthority()" drain loop at BP_WPZBugRunMgr.lua:387. The listen host is the
        // authority, so it never runs any of it, which is why only the host cannot shoot. This patch
        // replays those calls on the host itself.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostInitFix), DlcClientPatches.ZBugHostInitFix);
        // 进击行动 (and every other BP_GameOnline level): the "?listen" host runs InitNewPlayer on the
        // player's own machine, and the native team array that it reads is only filled by
        // UE4.UUMGLibrary.SetTeamCharacters() - which no online/room UI ever calls, because on the
        // official service the dedicated server is handed the lineup instead. Without it every entry
        // ends in "AGameBaseMode::InitNewPlayer :Team is Empty" -> "Couldn't spawn player: Invalid
        // Character ID!" -> StaticShutdownAfterError. The patch pushes the lineup just before the
        // ready packet is sent.
        await SendClientPatch(connection, nameof(DlcClientPatches.OnlineTeamFix), DlcClientPatches.OnlineTeamFix);
        // 进击行动: the native online travel leaves UMapManager's map id on the PREVIOUS map (the T13
        // world object is named "Assaultops_60200" while map.txt spells the package "AssaultOps_60200",
        // so the case-sensitive PostLoadLogic lookup misses). The stale id makes GoToMainLevel()'s
        // Map.Open(2) a silent no-op - 放弃 / 退出 cannot leave the level at all. This patch re-opens the
        // level through the one Lua-visible write path (UMapManager.Open) so the id, the Lua map class
        // and the fight hooks all match. Runs after OnlineTeamFix because the re-entry goes through the
        // Map.Open wrapper that OnlineTeamFix installs.
        await SendClientPatch(connection, nameof(DlcClientPatches.OnlineMapFix), DlcClientPatches.OnlineMapFix);
        // 进击行动: the ONLINE launch class has no OnSettlement, so the level end (which fires
        // Event.OnLevelFinish for everyone, authority or not) opens no panel and the session aborts two
        // minutes later. This patch opens the mode's own settlement UI, whose 退出 button is what
        // returns the player to the lobby.
        await SendClientPatch(connection, nameof(DlcClientPatches.OnlineSettleFix), DlcClientPatches.OnlineSettleFix);
        // 决战哈姆 wave bookkeeping: WP.EventMgr (a Lua singleton) keeps its listener list across level
        // changes, but the logic-tree nodes it points at are released when a match ends, and leaving a
        // match never runs their BP_TaskNode_Finish. EventMgr:SendEvent calls each callback unguarded,
        // so the released node throws "attempt to read property 'KillTag' on released object" inside the
        // delivery loop and aborts it - the live node for the new match never learns the wave's spawn
        // total, so CheckFinish() is true on the first kill and the wave is declared cleared (the
        // 剩余敌人 0 / one kill clears the wave / next wave spawns early family). This patch wraps
        // SendEvent so one stale listener can no longer silence the rest.
        await SendClientPatch(connection, nameof(DlcClientPatches.WPEventGuard), DlcClientPatches.WPEventGuard);
        // 决战哈姆 / 冲关啦 listen host, part two. ZBugHostInitFix above satisfies the LUA callers by
        // writing CheckIsLocallyControlled on the pawn instance, but UnLua only routes a UFunction
        // through Lua when the name exists on the class table at bind time - so every gate the game
        // evaluates inside a BP graph (the fire input handler, TryAttack, the wave and score
        // bookkeeping) still answered false on the host. This patch requires the character module at
        // login, i.e. before the first pawn is bound, and hooks the class table instead; the
        // replacement delegates to the original and only widens the answer on a listen host.
        // Same patch replays the two host-invisible RepNotify feeds (the ZBugChallenge countdown to
        // the HUD clock event 104, and ChallengeScore to its own OnRep) and bridges Event.PartyBtnPress
        // to TryAttack / TryReload / TryUseSkill - the press events DO arrive on the host, unlike
        // Event.PCKeyboardEvent, which never carries the mouse. Runs last so it wins over the older ids.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostPlayFix), DlcClientPatches.ZBugHostPlayFix);

        // ZBugHostViewFix (v7). Two findings from the 18:00 static pass.
        // (1) The whole client-side presentation layer is written as
        //     `if self:HasAuthority() and not self.IsStandalone then return end` (or the mirror form
        //     `if not self:HasAuthority() or self.IsStandalone then ... end`). A listen host is both
        //     the authority and the local player: HasAuthority() is true while IsStandalone is false,
        //     so every one of those blocks is skipped - PlayEffectByIdNew (all multicast hit /
        //     monster VFX), OnGameStateChange(Progress), ZBugRun_Restart_RPC, the standalone-only
        //     OnRep_CacheLevelUpBuffs, the hit-effect half of LuaImpl_GetDamage_MultiCast,
        //     LuaImpl_SmokeBomb_Multicast and NotifyBossSkillTips_RPC. IsStandalone is a plain Lua
        //     field cached on the character (BP_WPCharacter.lua:31), so setting it changes nothing
        //     about engine replication - only which presentation branch runs.
        // (2) v5/v6 replay the HUD feeds only when a value changes, so a rebuilt or reset HUD stays
        //     wrong forever (observed: HP bar back to 999/99999, buff list no longer opening). This
        //     patch refreshes HP / skill power / weapon panel on a 2s cadence with pure display calls,
        //     and deliberately does NOT re-run CheckInitFinish or OnRep_PlayerWeapon, which carry
        //     StopAttack / ForceFinishReload side effects.
        // It also stubs the v5/v6 polling bodies (keeping only their CheckIsLocallyControlled class
        // hook, now extended to the player controller and player state classes) and stops v6 from
        // spamming GetCharacter errors while no pawn exists.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostViewFix), DlcClientPatches.ZBugHostViewFix);

        // v8 ZBugHostHudFix - completes the two HUD feeds that a one-shot replay can never deliver on
        // a listen host, both measured live in-match:
        //   (1) Buff selection. Character:OnRep_CacheLevelUpBuffs (BP_WPCharacter.lua:2729) is what
        //       calls UI.Call2("PartyZBugRunFight","ZBugRun_OpenBuffList", CacheLevelUpBuffs), and the
        //       plain call plus the widget were verified to work on the host (card ids 21/2/12, list
        //       visibility 4). But v7 only fired it when CacheLevelUpBuffs:Num() changed, which happens
        //       once, seconds into the match - usually before the HUD exists - leaving
        //       WaitingClientSelectBuff true with 15 unspent buffs. v8 re-asserts the prompt while the
        //       selection is pending and no list is open, on a 4s cooldown.
        //   (2) Skill key icons. umg_zbugrun_fight.lua:812 NotifyPlayerSkillsChanged fills
        //       SkillPanel["Skill"..i] from playerstate.Skills, and is only reached through
        //       BP_WPZBugRunMgr.lua:497 inside CheckInitFinish (host: runs once, too early) or the
        //       client-only LuaImpl_TickCheckInitForPlayer replay. The log confirms it never ran here
        //       (grep "本地玩家技能变化" = 0), which is exactly the missing skill row. v8 re-sends the
        //       skill and attached-item notifications on change and every 8s otherwise.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostHudFix), DlcClientPatches.ZBugHostHudFix);

        // v9 ZBugHostScopeFix - v7/v8 ran on the LAN guest too, which is how the guest ended up able to
        // pick two buffs at match start while the host still could not pick any: v7 set IsStandalone on
        // the guest's own pawn, and BP_WPCharacter.lua:2679 ("if self.IsStandalone then
        // self:OnRep_CacheLevelUpBuffs() end") then opened the guest's list a second time on top of the
        // normal client prompt. v9 makes the whole stage host-only, restores the real
        // WorldIIsStandalone value elsewhere, and paces the buff prompt to one attempt per 8s, capped at
        // six per pawn, each followed by a state read (MIKU_H9 buffstate csl= vis=).
        // It also documents the 21:21:42 crash: the client died of DXGI_ERROR_DEVICE_REMOVED on the
        // render thread (AMD Radeon 610M iGPU), with zero Lua errors in the session and unrelated
        // minidumps on the same machine at 02:07 and 04:34, long before these patches existed.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostScopeFix), DlcClientPatches.ZBugHostScopeFix);

        // v10 ZBugHostBuffFix - v9 stopped the guest from opening a second buff list, but the host's
        // list still never appears: the patch's own replay prints "MIKU_H9 buffprompt bf=3 try=N"
        // followed every single time by "MIKU_H9 buffstate csl=false vis=1", i.e. the widget stayed
        // Collapsed and ui.CurSelectList stayed nil, while the identical UI.Call2 issued from the live
        // Lua console DID open it (csl=true vis=4). umg_zbugrun_fight.lua:1022-1061
        // ZBugRun_OpenBuffList has exactly one silent early return - "if not IsValid(char) then return
        // end" - and only calls WidgetUtils.SelfHitTestInvisible(self.BuffList) + OnOpen inside the
        // "self.CurSelectList == nil" branch, so vis=1 proves that branch never ran. v9's replay was
        // wrapped in a pcall that printed nothing and never called ZBugRun_OpenBuffList directly, which
        // is why the failing link was invisible. v10 prints every pcall error on the buff path plus the
        // character/IsValid state, and if the widget is still closed it opens the list itself with the
        // exact call sequence the game uses, so the host can actually pick while the log names the
        // broken link. Guard variable is new because MikuH9Ver is already 1 in the running client.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostBuffFix), DlcClientPatches.ZBugHostBuffFix);

        // v11 ZBugHostLocalFix - the reason the host could never pick a buff, caught by v10's own
        // logging: "MIKU_HB prompt bf=3 try=1 cilc=false ok=true e=nil" means the replay raised no
        // error but returned immediately at its own gate "if self:CheckIsLocallyControlled() and
        // self.CacheLevelUpBuffs:Num() > 0 then". The host's own pawn answered false there.
        // v6's CILC hook is the only thing still forcing that UFunction true (v7 stubbed v5's
        // instance-level override), and it asks MikuHPIsListenHost, which v6 wrote as
        // "pcall(function() return IsServer(w) and not IsDedicatedServer(w) and not IsStandalone(w) end)".
        // Those three are global helpers that do not exist in the client script environment - a live
        // console payload died with "attempt to call a nil value (global 'IsServer')" - so the pcall
        // failed, the helper always returned false, and the hook always fell back to the blueprint's
        // own false answer. v11 switches it to the pcall-wrapped engine calls and was verified live on
        // the host pawn ("CIL fix host=true" then "CIL fix eng=true cilc=true", previously
        // cilc=false). This also unblocks every other client-side branch gated on
        // CheckIsLocallyControlled, and v11 now calls OnRep_PlayerWeapon once per weapon change, which
        // reaches AnimIns:InitZBugWeaponAsset().
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostLocalFix), DlcClientPatches.ZBugHostLocalFix);

        // v12 ZBugHostWeaponFix - three things the host still misses, none of them gated on anything the
        // server can detect from its own side.
        // 1) BP_WPCharacter:OnRep_WeaponList (BP_WPCharacter.lua:3275-3294) is another RepNotify that
        //    never reaches the host's own client. Its gated half calls ui:OnLocalCharWeaponListRefresh,
        //    and uw_party_fight_challenge.lua:168-173 is where the top-right weapon widget is switched
        //    to SelfHitTestInvisible - before that call arrives the widget stays Collapsed, which is
        //    exactly the "no weapon icon and no airdrop weapon slot" report. Its ungated half is the
        //    only code that hides and Lua_Tick()s a picked up airdrop weapon on a client
        //    ("the client weapon does not follow the server hidden state, hide it by hand"). Replaying
        //    it whenever WeaponList:Num() changes restores both.
        // 2) UWaterParkQAnimIns::UpdateZBugRunInfo keeps printing isInitFinish? 0 and
        //    InitZBugWeaponAsset Failed!!! NoResPath CharPath: 0 for most actors. The bootstrap
        //    (InitAssets / InitZBugRunInfo) runs from C++ client paths that never execute for the
        //    host's own copies, and InZBugShootAnim has never once been true on the host, so the
        //    shooting state machine hangs off that same animation layer. This patch calls the three
        //    anim instance entry points once per actor and prints every result, so the next log tells
        //    us both whether the call is accepted and whether the NoResPath rate drops.
        // 3) Every client-presentation branch in BP_WPCharacter is written as
        //    "if self:HasAuthority() and not self.IsStandalone then ... end" / "if not self:HasAuthority()
        //    or self.IsStandalone then ... end". v9/v11 only forced IsStandalone on the local pawn, so
        //    monsters still took the authority path; v12 sweeps every character and monster.
        //    It also adds NotifyLocalPlayerItemsChanged, which v11 was missing (item slots).
        // Guard variable MikuKVer is new, because MikuHLVer is already 1 in the running client.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostWeaponFix), DlcClientPatches.ZBugHostWeaponFix);

        // v13 ZBugHostFireFix - the host's gun, driven directly instead of through the animation layer.
        // v12's own probe answered the question this pipeline was waiting on: on the host
        // PlayerController:Server_ShootEntityBullet exists as a callable UFunction (pcShoot=function)
        // while CanPlayerWeaponFire and Server_ZBugChangeFireState are C++ internals Lua cannot see,
        // and the anim-instance bootstrap calls (InitAssets / InitZBugRunInfo / InitZBugWeaponAsset)
        // are accepted but change nothing - the NoResPath / isInitFinish? 0 rate stayed at about 4.5/s
        // across v12. The guest's shots appear server side as
        //   LogTemp: Server_ShootEntityBullet, BulletLoc: X=.., FireLoc: X=.., TargetLoc: X=..
        // and that line has never once been logged on the host, so the input -> fire-state -> shot
        // chain dies between InZBugFireState (which does go true) and the animation notify that would
        // spawn the entity bullet.
        // The patch therefore stops depending on that notify: Event.PartyBtnPress Fire(5) held-state
        // drives a 0.1s timer that calls TryAttack() plus Server_ShootEntityBullet(muzzle, muzzle, aim)
        // with the aim point taken from the player camera and GetForwardVector() at 10000 units, so the
        // bullet flies down the crosshair exactly like the guest's. The host pawn also gets the
        // InitZBugChar() that clients run in BeginPlay and the authority branch skips - monsters are
        // deliberately excluded, because the earlier monster pump made both machines damage-immune.
        // The same 0.1s timer carries the HP / SkillPower / ammunition HUD feeds, which also removes
        // the chunky skill-bar drain that v12's 2s sampling introduced. Every shot result is printed
        // (MIKU_FX shot ok=.. e=..) and the server side line is the independent confirmation.
        // Guard variable MikuFXVer is new, because MikuKVer is already 1 in the running client.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostFireFix), DlcClientPatches.ZBugHostFireFix);

        // v14 ZBugHostAssetFix - BP_WPZBugRunMgr.lua:7-23 ReceiveBeginPlay wraps the whole animation
        // asset preload (eight PreLoadGameAssetByConfig calls plus PreloadOtherAsset, which is what
        // UWaterParkQAnimIns resolves its CharPath/WeaponPath from) in "if not self:HasAuthority()".
        // On a listen host that is false, so the assets are never preloaded and every anim instance
        // stays uninitialised: InitZBugWeaponAsset Failed!!! NoResPath CharPath: 0 at a steady 4.5/s,
        // isInitFinish? 0, no monster leg animation, no hit effects, and no fire-state notify to
        // spawn the entity bullet. This patch runs the same block on the host once per ZBugRunMgr
        // (once per level) and then sweeps every character and monster anim instance once.
        // Guard variable MikuAVer is new.
        await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostAssetFix), DlcClientPatches.ZBugHostAssetFix);

        // v15 ZBugHostClarityFix - three small items. (a) Diagnostic: the game's own
        // CheckIsLocallyControlled() is printed once per character (name / class / engine value /
        // original UFunction value / hooked value / is-local-pawn), because the suspicion is that on
        // a listen host it also returns true for monsters and for the guest's proxy pawn.
        // (b) LuaImpl_DamageOther (BP_WPCharacter.lua:2869-2881) feeds the hit marker, the damage
        // number and the red enemy bar over the victim, gated on that same function; if monsters
        // count as "local" on the host, every monster hit on the guest paints the host's own HUD,
        // which is what the host reported. The wrapper only forwards when the launcher really is the
        // engine-local pawn. (c) LuaImpl_ZBugStartReborn hides Mesh and the weapon mesh for all
        // modes, but only the ZBugRun reborn branch restores them (BP_WPCharacter.lua:784-795);
        // the ZBugChallenge branch of LuaImpl_ZBugRebornFinish (:2973-2995) never does, so a reborn
        // character stays invisible - the vanished model the host reported. The wrapper restores the
        // meshes after the original runs, with a 2s poll as a safety net.
        // Guard variable MikuCBVer is new.
        // PUSHED AS PART OF ZBugHostFinalFix (see the consolidated push below).

        // ZBugHostCoreFix v16: every earlier host stage died of timer-budget exhaustion (v5/v15 30min,
        // v7/v8/v12 60min), so from ~23:21 on nothing fed the host HUD any more and the next match -
        // which starts with a freshly reset HUD - showed no skill / item / weapon UI at all (MikuKStage
        // MK count in the 00:15 match was zero). This patch owns an unbounded 0.5s stage that
        // repopulates the HUD whenever the local pawn / HUD widget / game state changes, keeps the
        // IsStandalone sweep running for the client-visual branches, replays WeaponList and
        // ChallengeScore, and re-prompts the buff list. It also replaces the v6
        // CheckIsLocallyControlled wrapper with an identity-based one (the old one returned true for AI
        // monsters on the listen host, which is what painted the red enemy bar over the guest), and it
        // fixes the fire injection that v13 never actually performed: an older v13 handler is still
        // registered in the same client and reset MikuFXFire on the same frame the fixed handler set it
        // ("MIKU_FX key5 p=1 fire=true" then "MIKU_FX fire=false", every heartbeat shots=0). The new
        // bridge writes its own MikuCVDown flag and drives a 0.1s pump that calls TryAttack,
        // CacheFireInput and PlayerController:Server_ShootEntityBullet (the same RPC the guest's client
        // calls, verified in Game-backup-2026.10.08-13.16.39.log).
        // PUSHED AS PART OF ZBugHostFinalFix (see the consolidated push below).
                // v17: the ZBugHostCoreFix fire pump resolved its muzzle through
                // c:GetActorLocation(), which does not exist in this game's Lua bindings (the
                // shipped Script tree uses K2_GetActorLocation 957 times and the bare name never),
                // so MikuCVDoShot always returned early and not one shot RPC was ever sent.
                // Proven live with /lua: `CVM loc ok=false` versus `CVM k2 ok=true`, and after
                // repointing the global on the fly the first real call succeeded
                // (`MIKU_CV shot n=1 ok=true e=nil`). This patch repoints the muzzle/aim globals
                // the deployed pump calls at runtime, adds a redundant fire-release listener and
                // reports muzzle/aim resolution every two seconds.
                //
                // 2026-10-09 freeze: the three most recent host layers (ClarityFix v15 + CoreFix v16 +
                // ShotFix v17) are now delivered as ONE payload, composed in DlcClientPatches.cs by
                // concatenating the three constants (const string concat is C#-level only - the wire
                // payload still contains no forbidden characters, checked by compose_final.py). Same
                // bytes, same order, same guards, one push instead of three. The older host layers
                // (v5..v14) keep their own pushes: they are superseded in behaviour but unverified for
                // removal, and this line is frozen - see docs/progress/035 for the slim-down checklist.
                await SendClientPatch(connection, nameof(DlcClientPatches.ZBugHostFinalFix), DlcClientPatches.ZBugHostFinalFix);
    }

    /// <summary>
    /// Characters System.Text.Json escapes to \uXXXX. The client decodes JSON string values with
    /// load("return \"...\"") in Misc/Json.lua, and Lua 5.1 rejects \u escapes, so one of these
    /// silently kills the entire payload ("Misc/Json.lua:387: attempt to call a nil value") and the
    /// patch becomes a client-side no-op with no server-side symptom. Fail loudly here instead.
    /// </summary>
    private static readonly char[] ForbiddenPatchChars = ['+', '<', '>', '&', '\'', '"'];

    public static async Task SendClientPatch(Connection connection, string name, string lua)
    {
        foreach (var bad in ForbiddenPatchChars)
        {
            if (!lua.Contains(bad))
                continue;

            Logger.GetByClassName().Error(
                $"Client patch '{name}' contains '{bad}', which the serializer escapes to \\uXXXX and " +
                "the client cannot decode; dropping it instead of shipping a payload that silently fails.");
            return;
        }

        var payload = new JsonObject { ["code"] = lua };
        Logger.Info($"Pushing client patch '{name}' ({lua.Length} chars).");
        await CallGSRouter.SendScript(connection, "GM", payload.ToJsonString());
    }

    /// <summary>
    /// 暮色广场 + 主线: the client's DLC_Chapter.lua:593 callback overwrites OpenTime/CloseTime of
    /// each matching chapter row. Two rows matter for DLC35:
    ///   * the Refresh chapter (DlcFuncConfId.Refresh = 35) behind 暮色广场/dlc35refresh;
    ///   * the Story chapter (UnlockChapter = 28, i.e. "main_28") behind 前往主线.
    /// Without the Story entry the client's DLC_Chapter.CheckChapterOpen(28) compares against the
    /// shipped 2025 window, fails, and only shows the "ui.TxtDLC1Over" tip instead of opening.
    /// </summary>
    private static async Task PushDlcChapterSyncTime(Connection connection)
    {
        var act = DlcRuntimeOverrides.GetPinnedActivity();
        if (act == null)
            return;

        var (start, end) = DlcRuntimeOverrides.Window();
        var payload = new JsonArray();
        var chapterIds = new List<uint>();

        var refreshId = DlcRuntimeOverrides.ResolveFuncConfigId(act.DlcFuncConfId, "Refresh");
        if (refreshId != 0)
            chapterIds.Add(refreshId);

        var storyId = DlcRuntimeOverrides.ResolveStoryChapterId(act);
        if (storyId != 0 && !chapterIds.Contains(storyId))
            chapterIds.Add(storyId);

        if (chapterIds.Count == 0)
            return;

        foreach (var chapterId in chapterIds)
            payload.Add(new JsonObject
            {
                ["nID"] = chapterId,
                ["OpenTime"] = start,
                ["CloseTime"] = end
            });

        Logger.Info($"Pushing DLCChapter_SyncTime for chapter(s) {string.Join(", ", chapterIds)}.");
        await CallGSRouter.SendScript(connection, "DLCChapter_SyncTime", payload.ToJsonString());
    }

    /// <summary>
    /// 荣誉冲突: the client's Defend.lua:634 callback overwrites DefendLogic.tbActConf[nId] times.
    /// Pushed unsolicited it only refreshes the times (pCheckCallBack is nil unless the client itself
    /// asked), so the entry stops reporting the expired window.
    /// </summary>
    private static async Task PushDefendOpenAct(Connection connection)
    {
        var pinnedId = DlcRuntimeOverrides.EffectiveDlcId;
        var (start, end) = DlcRuntimeOverrides.Window();

        foreach (var act in DlcRuntimeOverrides.SelectDefendActs(pinnedId))
        {
            var payload = new JsonObject
            {
                ["bOpen"] = true,
                ["nId"] = act.Id,
                ["nBeginTime"] = start,
                ["nEndTime"] = end
            }.ToJsonString();

            Logger.Info($"Pushing DefendLogic_CheckOpenAct for defend act {act.Id} (DLC {pinnedId}).");
            await CallGSRouter.SendScript(connection, "DefendLogic_CheckOpenAct", payload);
        }
    }

    /// <summary>
    /// 商店页签时间: pushed unsolicited so the DLC shop entries (崇福集市 145,146) are already open
    /// when the DLC UI is built instead of waiting for the shop UI to ask for them.
    /// </summary>
    private static async Task PushShopOpenTime(Connection connection)
    {
        if (GameData.DlcShopData.Count == 0)
            return;

        var payload = DlcRuntimeOverrides.BuildShopOpenTimePayload().ToJsonString();
        Logger.Info($"Pushing ShopLogic_GetOpenTime for {GameData.DlcShopData.Count} shop tab(s).");
        await CallGSRouter.SendScript(connection, "ShopLogic_GetOpenTime", payload);
    }

    /// <summary>
    /// 海姆优化作战 (Rush): pushes a fresh Online window for every online row of the Rush group so
    /// DlcFuncRush.OnGetTimeInfo() stops reporting ui.TxtDLC1Over.
    /// </summary>
    private static async Task PushRushTime(Connection connection)
    {
        var act = DlcRuntimeOverrides.GetPinnedActivity();
        if (act == null)
            return;

        var groupId = DlcRuntimeOverrides.ResolveFuncConfigId(act.DlcFuncConfId, "Rush");
        if (groupId == 0)
            return;

        var rows = DlcRuntimeOverrides.SelectOnlineGroupRows(groupId);
        if (rows.Count == 0)
        {
            Logger.Warn($"Rush group {groupId} has no online rows; skipping TimeActivitie override.");
            return;
        }

        var (start, end) = DlcRuntimeOverrides.Window();
        foreach (var rowId in rows)
        {
            var payload = DlcRuntimeOverrides.BuildRushTimePayload(rowId, start, end).ToJsonString();
            Logger.Info($"Pushing Online_GMChangeTime for online row {rowId} (Rush group {groupId}).");
            await CallGSRouter.SendScript(connection, "Online_GMChangeTime", payload);
        }
    }

    /// <summary>心愿寄语 (Dorm): stretches the festival message act window.</summary>
    private static async Task PushFestivalMessageTime(Connection connection)
    {
        var act = DlcRuntimeOverrides.GetPinnedActivity();
        if (act == null)
            return;

        var actId = DlcRuntimeOverrides.ResolveFuncConfigId(act.DlcFuncConfId, "Dorm");
        if (actId == 0)
            return;

        var payload = DlcRuntimeOverrides.BuildFestivalMessagePayload(actId).ToJsonString();
        Logger.Info($"Pushing GMRoleFestivalSetId for act {actId} (Dorm).");
        await CallGSRouter.SendScript(connection, "GMRoleFestivalSetId", payload);
    }

    /// <summary>弑神斗场 (Teamboss): refreshes every seasonboss term row of the act.</summary>
    private static async Task PushSeasonBossTime(Connection connection)
    {
        var act = DlcRuntimeOverrides.GetPinnedActivity();
        if (act == null)
            return;

        var actId = DlcRuntimeOverrides.ResolveFuncConfigId(act.DlcFuncConfId, "Teamboss");
        if (actId == 0)
            return;

        var rows = DlcRuntimeOverrides.SelectSeasonBossRows(actId);
        if (rows.Count == 0)
        {
            Logger.Warn($"Teamboss act {actId} has no seasonboss rows; skipping SeasonBoss override.");
            return;
        }

        var (start, end) = DlcRuntimeOverrides.Window();
        var payload = DlcRuntimeOverrides
            .BuildSeasonBossTimePayload(rows.Select(r => (r.Id, start, end, end)))
            .ToJsonString();

        Logger.Info($"Pushing SeasonBoss_GMChangeTime for act {actId} ({rows.Count} term row(s)).");
        await CallGSRouter.SendScript(connection, "SeasonBoss_GMChangeTime", payload);
    }

    private static async Task SendSupportCardsOnLogin(Connection connection)
    {
        var player = connection.Player;
        if (player == null)
            return;

        var supportCards = player.InventoryManager.InventoryData.SupportCards.Values.ToList();
        Logger.Info($"Split support card sync on login: total={supportCards.Count}, chunkSize={SupportCardLoginSplitThreshold}");

        foreach (var chunk in supportCards.Chunk(SupportCardLoginSplitThreshold))
        {
            var packet = new PacketNtfCallScript(chunk.ToList());
            await connection.SendPacket(packet);
        }
    }

    private static void ApplySavedGirlSkinTypes(PlayerInstance player)
    {
        var inventoryData = player.InventoryManager.InventoryData;
        inventoryData.SkinTypesBySkinId ??= [];
        var changed = false;

        foreach (var (skinId, skinType) in inventoryData.SkinTypesBySkinId.ToArray())
        {
            var clamped = GirlSkin_ChangeSkinType.ClampClientSkinType(skinType);
            if (clamped != skinType)
            {
                inventoryData.SkinTypesBySkinId[skinId] = clamped;
                changed = true;
            }

            var skinData = GirlSkin_ChangeSkinType.GetOrCreateSkinItem(player, skinId);
            if (skinData != null && skinData.SkinType != clamped)
            {
                skinData.SkinType = clamped;
                changed = true;
            }
        }

        if (changed)
            DatabaseHelper.SaveDatabaseType(inventoryData);
    }

    private static async Task SendGirlSkinTypeOnLogin(Connection connection)
    {
        var player = connection.Player;
        if (player == null)
            return;

        var inventoryData = player.InventoryManager.InventoryData;
        inventoryData.SkinTypesBySkinId ??= [];
        foreach (var (skinId, skinType) in inventoryData.SkinTypesBySkinId)
        {
            var clamped = GirlSkin_ChangeSkinType.ClampClientSkinType(skinType);
            var skinData = GirlSkin_ChangeSkinType.GetOrCreateSkinItem(player, skinId);
            var response = new JsonObject
            {
                ["nType"] = clamped,
                ["nSkinId"] = skinId
            };

            if (skinData == null)
            {
                await CallGSRouter.SendScript(connection, "GirlSkin_ChangeSkinType", response.ToJsonString());
                continue;
            }

            await CallGSRouter.SendScript(connection, "GirlSkin_ChangeSkinType", response.ToJsonString(), new NtfSyncPlayer
            {
                Items = { skinData.ToProto() }
            });
        }
    }

    private static async Task SendDebugLoginState(Connection connection)
    {
        var response = new JsonObject
        {
            ["IsDebug"] = ConfigManager.Config.ServerOption.EnableGmMenu
        };

        await CallGSRouter.SendScript(connection, "gm.notifylogin", response.ToJsonString());
    }
}
