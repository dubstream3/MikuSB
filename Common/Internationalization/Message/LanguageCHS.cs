namespace MikuSB.Internationalization.Message;

#region Root

public class LanguageCHS
{
    public GameTextCHS Game { get; } = new();
    public ServerTextCHS Server { get; } = new();
    public WordTextCHS Word { get; } = new(); // a placeholder for the actual word text
}

#endregion

#region Layer 1

/// <summary>
///     path: Game
/// </summary>
public class GameTextCHS
{
    public CommandTextCHS Command { get; } = new();
}

/// <summary>
///     path: Server
/// </summary>
public class ServerTextCHS
{
    public WebTextCHS Web { get; } = new();
    public ServerInfoTextCHS ServerInfo { get; } = new();
}

/// <summary>
///     path: Word
/// </summary>
public class WordTextCHS
{
    public string Furniture => "家具";
    public string Skin => "皮肤";
    public string WeaponPart => "武器部件";
    public string CallItem => "召唤道具";
    public string SkinPart => "皮肤部件";
    public string Profile => "个人资料";
    public string WeaponSkin => "武器皮肤";
    public string SupportCard => "支援卡";
    public string Weapon => "武器";
    public string Rank => "星魂";
    public string Avatar => "角色";
    public string Material => "材料";
    public string Pet => "宠物";
    public string Relic => "遗器";
    public string Equipment => "光锥";
    public string Talent => "行迹";
    public string Banner => "卡池";
    public string Activity => "活动";
    public string CdKey => "兑换码";
    public string VideoKey => "过场动画密钥";
    public string Buff => "祝福";
    public string Miracle => "奇物";
    public string Unlock => "奢侈品";
    public string TrainParty => "派对车厢";

    // server info
    public string Config => "配置文件";
    public string Language => "语言";
    public string Log => "日志";
    public string GameData => "游戏数据";
    public string Cache => "资源缓存";
    public string CustomData => "自定义数据";
    public string Database => "数据库";
    public string Command => "命令";
    public string SSL => "SSL";
    public string Ec2b => "Ec2b";
    public string SdkServer => "Web服务器";
    public string Handler => "包处理器";
    public string Dispatch => "全局分发";
    public string Game => "游戏";
    public string Handbook => "手册";
    public string NotFound => "未找到";
    public string Error => "错误";
    public string FloorInfo => "区域文件";
    public string FloorGroupInfo => "区域组文件";
    public string FloorMissingResult => "传送与世界生成";
    public string FloorGroupMissingResult => "传送、怪物战斗与世界生成";
    public string Mission => "任务";
    public string MissionInfo => "任务文件";
    public string SubMission => "子任务";
    public string SubMissionInfo => "子任务文件";
    public string MazeSkill => "角色秘技";
    public string MazeSkillInfo => "角色秘技文件";
    public string Dialogue => "模拟宇宙事件";
    public string DialogueInfo => "模拟宇宙事件文件";
    public string Performance => "剧情操作";
    public string PerformanceInfo => "剧情操作文件";
    public string RogueChestMap => "模拟宇宙地图";
    public string RogueChestMapInfo => "模拟宇宙地图文件";
    public string ChessRogueRoom => "模拟宇宙DLC";
    public string ChessRogueRoomInfo => "模拟宇宙DLC文件";
    public string SummonUnit => "秘技生成";
    public string SummonUnitInfo => "秘技生成文件";
    public string RogueTournRoom => "差分宇宙";
    public string RogueTournRoomInfo => "差分宇宙房间文件";
    public string TypesOfRogue => "类型的模拟宇宙";
    public string RogueMagicRoom => "不可知域";
    public string RogueMagicRoomInfo => "不可知域房间文件";
    public string RogueDiceSurface => "骰面效果";
    public string RogueDiceSurfaceInfo => "骰面效果文件";
    public string AdventureModifier => "AdventureModifier";
    public string AdventureModifierInfo => "AdventureModifier文件";
    public string RogueMapGen => "RogueMapGen文件";
    public string RogueMiracleGroup => "RogueMiracleGroup文件";
    public string RogueMiracleEffectGen => "RogueMiracleEffectGen文件";

    public string DatabaseAccount => "数据库账号";
    public string Tutorial => "教程";
}

#endregion

#region Layer 2

#region GameText

/// <summary>
///     path: Game.Command
/// </summary>
public class CommandTextCHS
{
    public NoticeTextCHS Notice { get; } = new();
    public HelpTextCHS Help { get; } = new();
    public AccountTextCHS Account { get; } = new();
    public GirlTextCHS Girl { get; } = new();
    public GiveAllTextCHS GiveAll { get; } = new();
    public DebugTextCHS Debug { get; } = new();
    public GameCommandTextCHS Game { get; } = new();
    public GameCommandCnTextCHS GameCn { get; } = new();
    public GameCommandHostsCleanTextCHS HostsClean { get; } = new();
}

#endregion

#region ServerText

/// <summary>
///     path: Server.Web
/// </summary>
public class WebTextCHS
{
    public string Maintain => "服务器正在维修, 请稍后尝试。";
}

/// <summary>
///     path: Server.ServerInfo
/// </summary>
public class ServerInfoTextCHS
{
    public string Shutdown => "关闭中…";
    public string CancelKeyPressed => "已按下取消键 (Ctrl + C), 服务器即将关闭…";
    public string StartingServer => "正在启动 MikuSB";
    public string CurrentVersion => "当前服务端支持的版本: {0}";
    public string InvalidVersion => "当前为不受支持的游戏版本 {0}\n请更新游戏到 {1}";
    public string LoadingItem => "正在加载 {0}…";
    public string GeneratingItem => "正在生成 {0}…";
    public string WaitingItem => "正在等待进程 {0} 完成…";
    public string RegisterItem => "注册了 {0} 个 {1}。";
    public string FailedToLoadItem => "加载 {0} 失败。";
    public string NewClientSecretKey => "客户端密钥不存在, 正在生成新的客户端密钥。";
    public string FailedToInitializeItem => "初始化 {0} 失败。";
    public string FailedToReadItem => "读取 {0} 失败, 文件{1}";
    public string GeneratedItem => "已生成 {0}。";
    public string LoadedItem => "已加载 {0}。";
    public string LoadedItems => "已加载 {0} 个 {1}。";
    public string ServerRunning => "{0} 服务器正在监听 {1}";
    public string ServerStarted => "启动完成!用时 {0}s, 击败了99%的用户, 输入 ‘help’ 来获取命令帮助"; // 玩梗, 考虑英语版本将其本土化
    public string MissionEnabled => "任务系统已启用, 此功能仍在开发中, 且可能不会按预期工作, 如果遇见任何bug, 请汇报给开发者。";
    public string KeyStoreError => "SSL证书不存在, 已关闭SSL功能。";
    public string CacheLoadSkip => "已跳过缓存加载。";

    public string ConfigMissing => "{0} 缺失, 请检查你的资源文件夹: {1}, {2} 可能不能使用。";
    public string UnloadedItems => "卸载了所有 {0}。";
    public string SaveDatabase => "已保存数据库, 用时 {0}s";
    public string WaitForAllDone => "现在还不可以进入游戏, 请等待所有项目加载完成后再试";

    public string UnhandledException => "发生未经处理的异常: {0}";

    public string CurrentLanguage => "当前语言: {0}";
    public string BuildVersion => "构建版本: {0}";
    public string RequiredResourcesMissing => "缺少必要的资源文件, 正在下载资源包…";
    public string InstallingResources => "正在安装资源到 {0}…";
    public string ResourcesInstalled => "已安装 {0} 个资源文件到 {1}。";
    public string DownloadSourceDirect => "下载源: {0} (GitHub 直连)";
    public string DownloadSourceMirror => "下载源: {2} (镜像 {0}/{1}), 直连不可用, 已自动切换";
    public string DownloadProgress => "下载进度 {0}% ({1}/{2}), {3}, 剩余 {4}, 来源 {5}";
    public string Unknown => "未知";
    public string DownloadedItem => "已从 {1} 下载 {0}。";
    public string DownloadedThroughMirror => "已通过镜像 {1} 下载 {0} (直连 GitHub 失败)。";
    public string DownloadSourceFailed => "下载源 {0} 失败 ({1}), 正在切换下一个源…";
    public string DownloadSourceFailedAll => "下载源 {0} 失败 ({1}), 已没有可用的源。";
    public string DownloadStalled => "{0} 在 {1} 秒内只收到 {2}, 低于 {3} 的下限";
    public string DownloadNoResponse => "{0} 在 {1} 秒内没有返回响应头";
    public string DownloadTimedOut => "{0} 超过 {1} 秒仍未下载完成";
    public string DownloadNoData => "{0} 没有返回任何数据。";
    public string ConfirmUpdate => "发现新版本: {0} -> {1}。现在更新? [Y/n]: ";
    public string AntiScamSeparator => "============================================================";
    public string AntiScamFree => "MikuSB 完全免费且开源。";
    public string AntiScamScam => "如果你为这个服务端付过钱, 说明你被骗了。";
    public string AntiScamRefund => "请立即申请退款, 并把卖家举报给我们。";
    public string AntiScamDiscord => "Discord: https://discord.gg/aMwCu9JyUR";

    public string HostsDisabled => "已在启动选项中禁用 hosts 维护。";
    public string HostsSkipped => "跳过 hosts 维护: 当前启动渠道不是特定渠道。";
    public string HostsApplied => "已把特定渠道的域名指向本机 ({0})。";
    public string HostsNotElevated => "没有管理员权限, 无法修改 hosts 文件。请以管理员身份运行服务端, 或在其中用 /game-cn 打印要写入的条目。";
    public string HostsFailed => "修改 hosts 文件失败: {0}";
    public string HostsReleased => "已移除托管的 hosts 段落。";
    public string HostsRestoreFailed => "还原 hosts 文件失败: {0}";
    public string HostsNothingToRemove => "hosts 里没有 MikuSB 的托管段落, 无需清理。";
    public string HostsCleanElevated => "清理 hosts 需要管理员权限, 请以管理员身份重新运行。";
    public string HostsCleanUnsupported => "当前系统不支持自动维护 hosts。";

    public string AutoUpdateSkippedNotConfigured => "未配置 GitHub 发布源, 已跳过自动更新。";
    public string AutoUpdateSkippedNoUpdater => "没有找到 MikuSB.Updater.exe, 已跳过自动更新。";
    public string CurrentBuildVersion => "当前构建版本: {0}";
    public string LatestReleaseMissingAsset => "最新版本 {0} 里没有 {1}。";
    public string SkippedUpdateByUser => "已按你的选择跳过更新 {0}。";
    public string DownloadingUpdate => "正在下载更新 {0}。";
    public string DownloadingResourcePackage => "正在下载资源包。";
    public string FailedToStartUpdater => "启动 MikuSB.Updater.exe 失败。";
    public string UpdateHandedOver => "发现新版本 {0}, 已交给更新程序, 本进程即将退出。";
    public string AutoUpdateCheckFailed => "自动更新检查失败, 继续正常启动。";
    public string LatestReleaseNotAccessible => "无法访问 GitHub 上的最新发布 (仓库仍为私有, 属正常现象)。";
    public string ReleaseLookupFailed => "通过 {0} 查询发布信息失败 ({1}), 正在尝试下一个源。";
    public string ProxyListening => "MikuSB SOCKS5 代理正在监听 {0}:{1}";
    public string ProxyDisabled => "MikuSB 代理已禁用。";
}

#endregion

#endregion

#region Layer 3

#region CommandText

/// <summary>
///     path: Game.Command.Notice
/// </summary>
public class NoticeTextCHS
{
    public string PlayerNotFound => "未找到玩家!";
    public string InvalidArguments => "无效的参数!";
    public string NoPermission => "你没有权限这么做!";
    public string CommandNotFound => "未找到命令! 输入 '/help' 来获取帮助";
    public string TargetOffline => "目标 {0}({1}) 离线了!清除当前目标";
    public string TargetFound => "找到目标 {0}({1}), 下一次命令将默认对其执行";
    public string TargetNotFound => "未找到目标 {0}!";
    public string InternalError => "在处理命令时发生了内部错误: {0}!";
}

/// <summary>
///     path: Game.Command.Help
/// </summary>
public class HelpTextCHS
{
    public string Desc => "显示帮助信息";
    public string Usage =>
        "用法: /help\n" +
        "用法: /help [命令]";
    public string Commands => "命令: ";
    public string CommandPermission => "所需权限: ";
    public string CommandAlias => "命令别名: ";
}

public class AccountTextCHS
{
    public string Desc => "管理 SDK 登录使用的账号映射";
    public string Usage =>
        "用法: /account create <邮箱> <UID>\n" +
        "用法: /account delete <邮箱|UID>\n" +
        "用法: /account list";
    public string Created => "已创建账号映射: {0} -> UID {1}";
    public string CreateFailed => "创建账号映射失败: {0}";
    public string Deleted => "已删除账号映射: {0} -> UID {1}";
    public string DeleteFailed => "删除账号映射失败: {0}";
    public string DeleteOnline => "账号在线时无法删除: {0} -> UID {1}";
    public string NotFound => "未找到账号: {0}";
}

/// <summary>
///     path: Game.Command.Girl
/// </summary>
public class GirlTextCHS
{
    public string Desc => "添加角色到玩家\n" +
                          "detail 和 particular 可在 Resources/ExcelOutput/card.json 中查看\n\n" +
                          "注意：-1 表示所有角色\n";

    public string Usage =>
        "用法: /girl add <detail/-1> -p<particular> -l<level> -s<star>\n" +
        "用法: /girl level <guid/-1> <level>\n" +
        "用法: /girl neuronic <guid/-1> <level>\n" +
        "用法: /girl break <guid/-1> <level>";

    public string NotFound => "角色不存在！";
    public string Added => "已为玩家添加 {0} 个角色！";
    public string UpdateLevel => "已将 {1} 个角色等级设置为 {0}！";
    public string UpdateNeuronicLevel => "已将 {1} 个角色的神经元等级设置为 {0}！";
    public string UpdateBreakLevel => "已将 {1} 个角色的天启/武格等级设置为 {0}！";
}

/// <summary>
///     path: Game.Command.GiveAll
/// </summary>
public class GiveAllTextCHS
{
    public string Desc => "给予玩家所有物品\n" +
                          "注意：-1 表示全部";
    public string Usage => "用法：/giveall weapon <detail/-1> -p<特定> -l<等級>\n" +
                           "用法：/giveall weaponskin <detail/-1> -p<特定>\n" +
                           "用法：/giveall card <detail/-1> -p<特定> -l<等級>" +
                           "用法：/giveall profile <detail/-1> -g<类型> -p<特定> -l<等级>" +
                           "用法：/giveall skinpart <detail/-1> -g<類型> -p<特定> -l<等級>" +
                           "用法：/giveall weaponpart <detail/-1> -g<類型> -p<特定> -l<等級>" +
                           "用法：/giveall call <detail/-1> -g<類型> -p<特定> -l<等級>";
    public string NotFound => "未找到 {0}！";
    public string GiveAllItems => "已向玩家添加 {0} 个 {1}！";
}

/// <summary>
///     path: Game.Command.Debug
/// </summary>
public class DebugTextCHS
{
    public string Desc => "调试包输出开关";
    public string Usage => "用法: /debug [on|off|simple|detail|file]";
    public string Enabled => "已启用调试包输出。";
    public string Disabled => "已禁用调试包输出。";
    public string SimpleEnabled => "已启用简单调试包输出。";
    public string DetailEnabled => "已启用详细调试包输出。";
    public string FileEnabled => "个人调试文件输出已启用。";
    public string FileDisabled => "个人调试文件输出已禁用。";
}

public class GameCommandTextCHS
{
    public string Desc => "使用补丁注入启动已配置的游戏";
    public string Usage => "用法: /game [额外游戏参数]";
    public string Started => "游戏已启动。PID: {0}";
    public string Failed => "游戏启动失败: {0}";
}

public class GameCommandCnTextCHS
{
    public string Desc => "写入 hosts 并启动特定渠道的客户端";
    public string Usage => "用法: /game-cn [额外游戏参数]";
    public string Started => "游戏已启动。PID: {0}";
    public string Failed => "游戏启动失败: {0}";
    public string HostsUpdated => "已更新 hosts 文件: {0}";
    public string HostsCurrent => "hosts 已包含托管条目。";
    public string HostsFailed => "写入 hosts 失败: {0}";
    public string NeedAdmin => "写入 hosts 需要管理员权限。请以管理员身份重新启动服务器, 或手动把下面这段加入 {0}:\n{1}";
    public string Unsupported => "当前系统不支持自动维护 hosts。";
}

public class GameCommandHostsCleanTextCHS
{
    public string Desc => "移除 /game-cn 写入 hosts 的托管段落";
    public string Usage => "用法: /hosts-clean";
    public string Removed => "已移除 hosts 中的托管段落, 并清空了 DNS 缓存: {0}";
    public string NothingToRemove => "hosts 里没有 MikuSB 的托管段落, 无需清理: {0}";
    public string NeedAdmin => "清理 hosts 需要管理员权限, 请以管理员身份重新启动服务端后再试: {0}";
    public string Failed => "清理 hosts 失败: {0}";
    public string Unsupported => "当前系统不支持自动维护 hosts。";
}

#endregion

#endregion
