using Google.Protobuf;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Game.Character;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Lineup;
using MikuSB.GameServer.Game.Quest;
using MikuSB.GameServer.Game.Reward;
using MikuSB.GameServer.Game.Riki;
using MikuSB.GameServer.Game.Rogue3D;
using MikuSB.GameServer.Server;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Game.Player;

public class PlayerInstance(PlayerGameData data)
{
    #region Property
    public Connection? Connection { get; set; }

    public static readonly List<PlayerInstance> _playerInstances = [];
    public int Uid { get; set; }
    public bool Initialized { get; set; }
    public bool IsNewPlayer { get; set; }

    #endregion

    #region Data & Manager

    public PlayerGameData Data { get; } = data;
    public PlayerAttributes Attributes { get; } = new(data);
    public CharacterManager CharacterManager { get; set; } = null!;
    public InventoryManager InventoryManager { get; set; } = null!;
    public LineupManager LineupManager { get; set; } = null!;
    public QuestManager QuestManager { get; set; } = null!;
    public RewardManager RewardManager { get; set; } = null!;
    public RikiManager RikiManager { get; set; } = null!;
    public Rogue3DManager Rogue3DManager { get; set; } = null!;

    private QuestLevelType ActiveLevelType { get; set; }
    private uint ActiveLevelId { get; set; }
    private uint ActiveLevelSeed { get; set; }
    public uint ActiveLevelTeamId { get; private set; }

    #endregion

    #region Initializers
    public PlayerInstance(int uid) : this(new PlayerGameData { Uid = uid })
    {
        // new player
        IsNewPlayer = true;
        Data.Name = PlayerGameData.NormalizeDisplayName(AccountData.GetAccountByUid(uid)?.Username);

        DatabaseHelper.CreateInstance(Data);

        var t = Task.Run(async () =>
        {
            await InitialPlayerManager();
            foreach (var skinCard in GameData.CardSkinData.Values)
            {
                await InventoryManager.AddSkinItem((ItemTypeEnum)skinCard.Genre, skinCard.Detail, skinCard.Particular, skinCard.Level, false);
            }
            foreach (var ar in GameData.ArItemData.Values)
            {
                await InventoryManager.AddArItem((ItemTypeEnum)ar.Genre, ar.Detail, ar.Particular, ar.Level, false);
            }
            foreach (var manifest in GameData.ManifestationData.Values)
            {
                await InventoryManager.AddManifestationItem((ItemTypeEnum)manifest.Genre, manifest.Detail, manifest.Particular, manifest.Level, false);
            }
            foreach (var card in GameData.CardData.Values)
            {
                await CharacterManager.AddCharacter((ItemTypeEnum)card.Genre, card.Detail, card.Particular, card.Level, sendPacket:false);
            }
            foreach (var supplies in GameData.AllSuppliesData)
            {
                await InventoryManager.AddSuppliesItem(supplies, 90000, false);
            }

            var selected = CharacterManager.CharacterData.Characters
                .OrderBy(_ => Guid.NewGuid())
                .Take(3)
                .Select(x => x.Guid)
                .ToList();

            await LineupManager.UpdateLineup(1, selected[0], selected[1], selected[2],false);

        });
        t.Wait();

        Initialized = true;
    }
    private async ValueTask InitialPlayerManager()
    {
        Uid = Data.Uid;
        Data.LastActiveTime = Extensions.GetUnixSec();
        InventoryManager = new InventoryManager(this);
        LineupManager = new LineupManager(this);
        CharacterManager = new CharacterManager(this);
        QuestManager = new QuestManager(this);
        RewardManager = new RewardManager(this);
        RikiManager = new RikiManager(this);
        Rogue3DManager = new Rogue3DManager(this);

        await Task.CompletedTask;
    }
    public T InitializeDatabase<T>() where T : BaseDatabaseDataHelper, new()
    {
        var instance = DatabaseHelper.GetInstanceOrCreateNew<T>(Uid);
        return instance!;
    }

    #endregion

    #region Network
    public async ValueTask OnEnterGame()
    {
        if (!Initialized) await InitialPlayerManager();
        Data.EnsureDisplayName();
        await CharacterManager.RepairCharacterWeapons();
        await EnsureSupplies();
    }

    /// <summary>
    /// Builds the per-player managers on demand.
    /// A player restored from the database is still half-built when it reaches login: the primary
    /// constructor sets nothing but <see cref="Data"/>, and <see cref="InitialPlayerManager"/> only
    /// runs from the brand-new-player constructor or from <see cref="OnEnterGame"/>. Any login-time
    /// work that happens *before* OnEnterGame therefore has to ask for the managers explicitly -
    /// reaching into QuestManager too early threw NullReferenceException at HandlerReqLogin.cs:129 and
    /// left the client stuck on its permission check. Setting <see cref="Initialized"/> also keeps the
    /// later OnEnterGame from tearing the managers down and rebuilding them right after seeding has
    /// written through them.
    /// </summary>
    public async ValueTask EnsureManagersReady()
    {
        if (Initialized) return;
        await InitialPlayerManager();
        Initialized = true;
    }

    public IEnumerable<BaseGameItemInfo> GetSupplyItems() =>
        InventoryManager.InventoryData.Items.Values.Where(x => (x.TemplateId & 0xFFFF) == 5);

    private async ValueTask EnsureSupplies()
    {
        foreach (var supplies in GameData.AllSuppliesData)
        {
            await InventoryManager.AddSuppliesItem(supplies, 90000, false);
        }
    }

    public async ValueTask OnLogin()
    {
        _playerInstances.Add(this);
        await Task.CompletedTask;
    }

    public static PlayerInstance? GetPlayerInstanceByUid(long uid)
        => _playerInstances.FirstOrDefault(player => player.Uid == uid);
    public void OnLogoutAsync()
    {
        _playerInstances.Remove(this);
    }
    public async ValueTask SendPacket(BasePacket packet)
    {
        if (Connection?.IsOnline == true) await Connection.SendPacket(packet);
    }
    public async ValueTask SendPacket(int cmdId, IMessage msg)
    {
        if (Connection?.IsOnline == true) await Connection.SendPacket(cmdId,msg);
    }

    #endregion

    #region Actions
    public async ValueTask OnHeartBeat()
    {
        DatabaseHelper.ToSaveUidList.SafeAdd(Uid);
        await Task.CompletedTask;
    }

    public async ValueTask ReceiveMessage(uint sendUid, uint recvUid, string? message = null, uint? emojiId = null)
    {
        var data = new ChatMsg
        {
            Type = ChatType.Friend,
            Sender = sendUid,
            Recver = recvUid,
            Emoji = emojiId ?? 0,
            Text = ChatMessageHelper.NormalizeForClient(message),
            Profile = Data.ToProfileProto(),
            TimeStamp = ChatMessageHelper.BuildClientTimestamp()
        };

        await SendPacket(CmdIds.NtfFriendChat, data);

        if (recvUid == ConfigManager.Config.ServerOption.ServerProfile.Uid)
        {
            if (message != null)
            {
                if (message.StartsWith("/")) message = message[1..].Trim();
                CommandExecutor.ExecuteCommand(new PlayerCommandSender(this), message);
            }
        }
    }

    #endregion

    #region Serialization

    public PlayerProfile ToServerFriendProto()
    {
        var server = ConfigManager.Config.ServerOption.ServerProfile;
        var proto = new PlayerProfile
        {
            Pid = (uint)server.Uid,
            Account = server.Name,
            Name = server.Name,
            Sex = server.Gender,
            Level = (uint)server.Level,
            Sign = server.Signature
        };
        return proto;
    }

    public Proto.Player ToPlayerProto(bool includeSupportCards = true)
    {
        BuildPlayerAttr();
        var displayName = PlayerGameData.NormalizeDisplayName(Data.Name);
        var proto = new Proto.Player
        {
            Pid = (ulong)Data.Uid,
            Account = displayName,
            Provider = displayName,
            Channel = "gm",
            Subchannel = "gm",
            Name = displayName,
            Level = Data.Level,
            Exp = (uint)Math.Max(0, Data.Exp),
            Sex = Data.Gender,
            Vigor = Data.Vigor,
            LastVigorTime = Data.LastVigorTime,
            Solutions = { LineupManager.LineupData.LineupInfo.Values.Select(x => x.ToProto()) },
            Badges = { InventoryManager.InventoryData.Items.Values.Where(x => x.ItemType == ItemTypeEnum.TYPE_BADGE).Select(x => (ulong)x.UniqueId) }
        };

        foreach (var chara in CharacterManager.CharacterData.Characters) proto.Items.Add(chara.ToProto());
        foreach (var item in InventoryManager.InventoryData.Items.Values) proto.Items.Add(item.ToProto());
        foreach (var skin in InventoryManager.InventoryData.Skins.Values) proto.Items.Add(skin.ToProto());
        foreach (var weapon in InventoryManager.InventoryData.Weapons.Values) proto.Items.Add(weapon.ToProto());
        if (includeSupportCards)
        {
            foreach (var card in InventoryManager.InventoryData.SupportCards.Values) proto.Items.Add(card.ToProto());
        }
        Attributes.SyncTo(proto);

        foreach (var attr in Attributes.AllStrings)
            Attributes.SyncTo(proto, attr);

        foreach (var (key, value) in BuildMoneySync())
        {
            proto.Money[key] = value;
        }

        proto.ShowItems.AddRange(Data.ShowItems);

        return proto;
    }

    public void SetDisplayName(string? name)
    {
        Data.Name = PlayerGameData.NormalizeDisplayName(name);
    }

    public void SetShowItem(int index, ulong itemId)
    {
        if (index <= 0)
            return;

        while (Data.ShowItems.Count < index)
            Data.ShowItems.Add(0);

        Data.ShowItems[index - 1] = itemId;
    }

    public void SetStrAttr(uint gid, uint sid, string value)
    {
        Attributes.SetString(gid, sid, value);
    }

    public Dictionary<string, int> BuildMoneySync()
    {
        var currentMoney = (int)Math.Min(
            int.MaxValue,
            GetAttrValue(AttrIds.Currency.GroupId, AttrIds.Currency.GetSid(AttrIds.Currency.Money)));
        var sync = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["."] = currentMoney,
            ["gm.gm"] = currentMoney,
            ["jinshan.jinshan"] = currentMoney,
            ["pc_jinshan.pc_jinshan"] = currentMoney
        };
        return sync;
    }

    public void BeginLevelSession(QuestLevelType levelType, uint levelId, uint seed, uint teamId = 0)
    {
        ActiveLevelType = levelType;
        ActiveLevelId = levelId;
        ActiveLevelSeed = seed;
        ActiveLevelTeamId = teamId;
    }

    public bool IsLevelSession(QuestLevelType levelType, uint levelId, uint seed) =>
        ActiveLevelType == levelType && ActiveLevelId == levelId && ActiveLevelSeed == seed;

    public IReadOnlyList<PlayerLevelExcel> AddPlayerExperience(uint amount, NtfSyncPlayer sync)
    {
        if (amount == 0)
            return [];

        if (Data.Level == 0)
            Data.Level = 1;

        var leveledUp = new List<PlayerLevelExcel>();
        var totalExp = (ulong)Math.Max(0, Data.Exp) + amount;
        while (GameData.PlayerLevelData.TryGetValue(Data.Level, out var currentLevel) &&
               currentLevel.MaxExp > 0 &&
               totalExp >= currentLevel.MaxExp &&
               GameData.PlayerLevelData.ContainsKey(Data.Level + 1))
        {
            totalExp -= currentLevel.MaxExp;
            Data.Level++;
            if (GameData.PlayerLevelData.TryGetValue(Data.Level, out var newLevel))
                leveledUp.Add(newLevel);
        }

        Data.Exp = (int)Math.Min(int.MaxValue, totalExp);
        sync.Core[(uint)PlayerCoreAttribute.Level] = Data.Level;
        sync.Core[(uint)PlayerCoreAttribute.Exp] = (uint)Data.Exp;
        return leveledUp;
    }

    /// <summary>
    /// Credits a currency. <paramref name="allowVigorOverflow"/> only matters for
    /// <see cref="AttrIds.Currency.Vigor"/> and is used by the 数据金 exchange, where the retail
    /// server lets bought stamina pass the level cap.
    /// </summary>
    public void AddCurrency(uint moneyType, uint amount, NtfSyncPlayer sync, bool allowVigorOverflow = false)
    {
        if (amount == 0)
            return;

        if (moneyType == AttrIds.Currency.Vigor)
        {
            // Vigor lives outside the attribute table; VigorService owns the cap and the recovery
            // clock so the client's countdown stays consistent with the value we hand out.
            VigorService.Add(this, amount, sync, allowVigorOverflow);
            return;
        }

        var sid = AttrIds.Currency.GetSid(moneyType);
        var attr = Attributes.Add(AttrIds.Currency.GroupId, sid, amount);
        Attributes.SyncTo(sync, attr);
        if (moneyType == AttrIds.Currency.Money)
            SyncMoneyChannels(sync);
    }

    /// <summary>
    /// Mirrors the paid-currency balance (group 1, moneyType 1) into every money channel the client
    /// reads. Any currency write that can change that balance has to call this, otherwise the shop
    /// keeps showing the stale amount.
    /// </summary>
    public void SyncMoneyChannels(NtfSyncPlayer sync)
    {
        foreach (var (key, value) in BuildMoneySync())
            sync.Money[key] = value;
    }

    private uint GetAttrValue(uint gid, uint sid)
    {
        return Attributes.GetValue(gid, sid);
    }

    /// <summary>
    /// Re-applies the lobby bootstrap attributes. Currency (group 1) is deliberately excluded from
    /// the "raise only" merge: the balances are owned by <see cref="CurrencyBootstrap"/> and by the
    /// spend paths in <see cref="CurrencyService"/>, so letting the ratchet run over them would undo
    /// every purchase on the next snapshot.
    /// </summary>
    public void BuildPlayerAttr(bool additional = false)
    {
        QuestManager.MigrateChapterStarAwardMasks();
        QuestManager.RemoveLegacyLevelUnlocks();
        RikiManager.EnsureOwnedItemsUnlocked();

        var bootstrapAttrs = BuildLobbyBootstrapAttrs().ToList();
        if (additional) bootstrapAttrs.AddRange(BuildGirlFurnitureAttrs());
        var seenAttrs = new HashSet<(uint Gid, uint Sid)>();

        foreach (var (gid, sid, value) in bootstrapAttrs)
        {
            if (gid == AttrIds.Currency.GroupId)
                continue;

            if (!seenAttrs.Add((gid, sid)))
                continue;

            var attr = Attributes.Get(gid, sid);
            if (attr != null)
            {
                if (attr.Val < value)
                    attr.Val = value;

                continue;
            }

            Attributes.Set(gid, sid, value);
        }
    }

    private static IEnumerable<(uint Gid, uint Sid, uint Value)> BuildGirlFurnitureAttrs()
    {
        const uint furnitureUnlockedValue = 153391689;
        var groupFurnitureByArea = new Dictionary<uint, uint>();
        foreach (var pos in GameData.HouseFurniturePosData.Values)
        {
            var areaId = pos.AreaId;
            var groupId = pos.GroupId;
            uint selectedIndex = 1;
            var shift = (groupId - 1) * 3;
            if (!groupFurnitureByArea.TryGetValue(areaId, out var packed)) packed = 0;
            packed |= (selectedIndex << (int)shift);
            groupFurnitureByArea[areaId] = packed;
        }

        for (uint girlId = 0; girlId <= 50; girlId++)
        {
            var baseSid = girlId * 50;
            for (uint offset = 10; offset <= 19; offset++)
                yield return (101, baseSid + offset, furnitureUnlockedValue);

            if (groupFurnitureByArea.TryGetValue(girlId, out var groupValue))
                yield return (101, baseSid + 20, groupValue);
        }

        // Massage room furniture
        // 10010..10019
        for (uint sid = 10010; sid <= 10019; sid++)
            yield return (101, sid, furnitureUnlockedValue);

        // Massage room group state
        yield return (101, 10020, 1);

        // Hot spring furniture
        // 15001..15010
        for (uint sid = 15001; sid <= 15010; sid++)
            yield return (101, sid, furnitureUnlockedValue);

        // Beach furniture
        // 17101..17110
        for (uint sid = 17101; sid <= 17110; sid++)
            yield return (101, sid, furnitureUnlockedValue);

        for (uint sid = 30000; sid < 31000; sid++)
            yield return (101, sid, furnitureUnlockedValue);
    }

    private static IEnumerable<(uint Gid, uint Sid, uint Value)> BuildLobbyBootstrapAttrs()
    {
        // GuideLogic uses group 4. Value 999 is safely above every configured step count,
        // so the client treats these guides as already completed.
        yield return (4, 0, 5);
        yield return (11, 1, 1);
        yield return (57, 0, 1);
        yield return (99, 3, 30);
        yield return (110, 1, 1);
        yield return (178, 1, 1_700_000_000);
        yield return (187, 1, 2);

        // Currency (group 1, sid = moneyId * 2 + 1) used to be pinned to 999_999_999 here, which
        // made every balance infinite and defeated the shop prices. Starting balances now come from
        // CurrencyBootstrap on first login only, so the player can actually run out.

        for (uint guideId = 1; guideId <= 150; guideId++)
            yield return (4, guideId, 999);

        for (uint guideId = 10_000; guideId <= 10_300; guideId++)
            yield return (4, guideId, 999);

        for (uint guideId = 11_000; guideId <= 11_300; guideId++)
            yield return (4, guideId, 999);

        for (uint guideId = 12_000; guideId <= 12_100; guideId++)
            yield return (4, guideId, 999);

        for (uint guideId = 22_000; guideId <= 22_100; guideId++)
            yield return (4, guideId, 999);

        // Additional guide ids referenced directly by the Lua scripts and observed client logs.
        foreach (var guideId in new uint[] { 10_031, 10_041, 10_061, 10_081, 10_101, 10_224, 11_006, 11_202, 11_210, 22_002 })
            yield return (4, guideId, 999);

        foreach (var guide in GameData.GuideData.Values)
        {
            yield return (4, guide.ID, 999);
        }

        for (uint favor = 0; favor <= 50; favor++)
            yield return (101, favor * 50, 500);

        // Main Scene 0 mean default scene
        yield return (132, 1, 0);
    }
    #endregion
}
