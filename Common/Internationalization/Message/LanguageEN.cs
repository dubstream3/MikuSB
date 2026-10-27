namespace MikuSB.Internationalization.Message;

#region Root

public class LanguageEN
{
    public GameTextEN Game { get; } = new();
    public ServerTextEN Server { get; } = new();
    public WordTextEN Word { get; } = new(); // a placeholder for the actual word text
}

#endregion

#region Layer 1

/// <summary>
///     path: Game
/// </summary>
public class GameTextEN
{
    public CommandTextEN Command { get; } = new();
}

/// <summary>
///     path: Server
/// </summary>
public class ServerTextEN
{
    public WebTextEN Web { get; } = new();
    public ServerInfoTextEN ServerInfo { get; } = new();
}

/// <summary>
///     path: Word
/// </summary>
public class WordTextEN
{
    public string Furniture => "Furniture";
    public string Girl => "character";
    public string GirlPiece => "character fragment";
    public string Skin => "Skin";
    public string WeaponPart => "Weapon Part";
    public string CallItem => "Call Item";
    public string SkinPart => "Skin Part";
    public string Profile => "Profile";
    public string WeaponSkin => "Weapon Skin";
    public string Valk => "Valkyrie";
    public string Material => "Material";
    public string SupportCard => "Support Card";
    public string Weapon => "Weapon";
    public string Banner => "Gacha";
    public string Activity => "Activity";
    public string Elf => "Elf";
    public string Dress => "Outfit";
    public string Bracket => "Bracket";
    public string Disturbance => "Disturbance";
    public string Site => "Site";

    // server info
    public string Config => "Config File";
    public string Language => "Language";
    public string Log => "Log";
    public string GameData => "Game Data";
    public string Cache => "Resource Cache";
    public string CustomData => "Custom Data";
    public string Database => "Database";
    public string Command => "Command";
    public string SdkServer => "Web Server";
    public string Handler => "Packet Handler";
    public string Dispatch => "Global Dispatch";
    public string Game => "Game";
    public string Handbook => "Handbook";
    public string NotFound => "Not Found";
    public string Error => "Error";
    public string DatabaseAccount => "Database Account";
    public string Tutorial => "Tutorial";
}

#endregion

#region Layer 2

#region GameText

/// <summary>
///     path: Game.Command
/// </summary>
public class CommandTextEN
{
    public NoticeTextEN Notice { get; } = new();
    public HelpTextEN Help { get; } = new();
    public AccountTextEN Account { get; } = new();
    public GirlTextEN Girl { get; } = new();
    public GiveAllTextEN GiveAll { get; } = new();
    public DebugTextEN Debug { get; } = new();
    public GameCommandTextEN Game { get; } = new();
    public GameCommandCnTextEN GameCn { get; } = new();
    public GameCommandHostsCleanTextEN HostsClean { get; } = new();
}

#endregion

#region ServerTextEN

/// <summary>
///     path: Server.Web
/// </summary>
public class WebTextEN
{
    public string Maintain => "The server is undergoing maintenance, please try again later.";
}

/// <summary>
///     path: Server.ServerInfo
/// </summary>
public class ServerInfoTextEN
{
    public string Shutdown => "Shutting down...";
    public string CancelKeyPressed => "Cancel key pressed (Ctrl + C), server shutting down...";
    public string StartingServer => "Starting MikuSB";
    public string CurrentVersion => "Server supported versions: {0}";
    public string InvalidVersion => "Unsupported game version {0}\nPlease update game to {1}";
    public string LoadingItem => "Loading {0}...";
    public string GeneratingItem => "Building {0}...";
    public string WaitingItem => "Waiting for process {0} to complete...";
    public string RegisterItem => "Registered {0} {1}(s).";
    public string FailedToLoadItem => "Failed to load {0}.";
    public string NewClientSecretKey => "Client Secret Key does not exist and a new Client Secret Key is being generated.";
    public string FailedToInitializeItem => "Failed to initialize {0}.";
    public string FailedToReadItem => "Failed to read {0}, file {1}";
    public string GeneratedItem => "Generated {0}.";
    public string LoadedItem => "Loaded {0}.";
    public string LoadedItems => "Loaded {0} {1}(s).";
    public string ServerRunning => "{0} server listening on {1}";

    public string ServerStarted =>
        "Startup complete! Took {0}s, better than 99% of users. Type 'help' for command help"; // This is a meme, consider localpermissiong in English

    public string MissionEnabled =>
        "Mission system enabled. This feature is still in development and may not work as expected. Please report any bugs to the developers.";
    public string KeyStoreError => "The SSL certificate does not exist, SSL functionality has been disabled.";
    public string CacheLoadSkip => "Skipped cache loading.";

    public string ConfigMissing => "{0} is missing. Please check your resource folder: {1}, {2} may not be available.";
    public string UnloadedItems => "Unloaded all {0}.";
    public string SaveDatabase => "Database saved in {0}s";

    public string WaitForAllDone =>
        "You cannot enter the game yet. Please wait for all items to load before trying again";

    public string UnhandledException => "An unhandled exception occurred: {0}";

    public string CurrentLanguage => "Current language: {0}";
    public string BuildVersion => "Build version: {0}";
    public string RequiredResourcesMissing => "Required resources are missing. Downloading the resource package.";
    public string InstallingResources => "Installing resources into {0}...";
    public string ResourcesInstalled => "Installed {0} resource files into {1}.";
    /// <summary>Resource data version changed ({0} -> {1}); downloading the package again.</summary>
    public string ResourcesOutdated => "Resource data version changed ({0} -> {1}); downloading the package again.";
    public string DownloadSourceDirect => "Download source: {0} (direct GitHub)";
    public string DownloadSourceMirror => "Download source: {2} (mirror {0}/{1}); the direct source failed, switched automatically";
    public string DownloadProgress => "Download {0}% ({1}/{2}), {3}, {4} left, from {5}";
    public string Unknown => "unknown";
    public string DownloadedItem => "Downloaded {0} from {1}.";
    public string DownloadedThroughMirror => "Downloaded {0} through mirror {1} because the direct GitHub source failed.";
    public string DownloadSourceFailed => "Download source {0} failed ({1}), trying the next source...";
    public string DownloadSourceFailedAll => "Download source {0} failed ({1}), no sources left.";
    public string DownloadStalled => "{0} delivered only {2} in {1}s, below the {3} floor";
    public string DownloadNoResponse => "{0} sent no response headers within {1}s";
    public string DownloadTimedOut => "{0} took longer than {1}s to finish";
    public string DownloadNoData => "{0} returned no data.";
    public string ConfirmUpdate => "New version found: {0} -> {1}. Update now? [Y/n]: ";
    public string AntiScamSeparator => "============================================================";
    public string AntiScamFree => "MikuSB is completely free and open source.";
    public string AntiScamScam => "If you paid anyone for this server, you were scammed.";
    public string AntiScamRefund => "Request a refund immediately and report the seller to us.";
    public string AntiScamDiscord => "Discord: https://discord.gg/aMwCu9JyUR";

    public string HostsDisabled => "Hosts maintenance is disabled in the loader options.";
    public string HostsSkipped => "Hosts maintenance skipped: the configured launch channel is not the specific one.";
    public string HostsApplied => "Pointed the specific-channel host names at this machine ({0}).";
    public string HostsNotElevated =>
        "Cannot update the hosts file without administrator rights. Run the server elevated, or use /game-cn there to print the block.";
    public string HostsFailed => "Failed to update the hosts file: {0}";
    public string HostsReleased => "Removed the managed hosts block.";
    public string HostsRestoreFailed => "Failed to restore the hosts file: {0}";
    public string HostsNothingToRemove => "The hosts file has no managed block to remove.";
    public string HostsCleanElevated =>
        "Cleaning the hosts file needs administrator rights. Run MikuSB elevated and try again.";
    public string HostsCleanUnsupported => "Managing the hosts file is not supported on this system.";

    public string AutoUpdateSkippedNotConfigured =>
        "Auto update skipped because the GitHub release source is not configured.";
    public string AutoUpdateSkippedNoUpdater => "Auto update skipped because MikuSB.Updater.exe was not found.";
    public string CurrentBuildVersion => "Current build version: {0}";
    public string LatestReleaseMissingAsset => "Latest release {0} does not contain asset {1}.";
    public string SkippedUpdateByUser => "Skipped update {0} by user choice.";
    public string DownloadingUpdate => "Downloading update {0}.";
    public string DownloadingResourcePackage => "Downloading resource package.";
    public string FailedToStartUpdater => "Failed to start MikuSB.Updater.exe.";
    public string UpdateHandedOver => "Update {0} found. Handing over to updater and shutting down.";
    public string AutoUpdateCheckFailed => "Auto update check failed. Continuing normal startup.";
    public string LatestReleaseNotAccessible =>
        "Latest GitHub release is not accessible. This is expected while the repository remains private.";
    public string ReleaseLookupFailed => "Release lookup via {0} failed ({1}). Trying the next source.";
    public string ProxyListening => "MikuSB SOCKS5 proxy listening on {0}:{1}";
    public string ProxyDisabled => "MikuSB proxy is disabled.";
}

#endregion

#endregion

#region Layer 3

#region CommandText

/// <summary>
///     path: Game.Command.Notice
/// </summary>
public class NoticeTextEN
{
    public string PlayerNotFound => "Player not found!";
    public string InvalidArguments => "Invalid arguments!";
    public string NoPermission => "You don't have permission!";
    public string CommandNotFound => "Command not found! Type '/help' for assistance";
    public string TargetOffline => "Target {0}({1}) is offline! Clearing current target";
    public string TargetFound => "Target {0}({1}) found. Next command will default to this target";
    public string TargetNotFound => "Target {0} not found!";
    public string InternalError => "Internal error occurred while processing command!";
}

/// <summary>
///     path: Game.Command.Help
/// </summary>
public class HelpTextEN
{
    public string Desc => "Show help information";
    public string Usage =>
        "Usage: /help\n" +
        "Usage: /help [cmd]";
    public string Commands => "Commands: ";
    public string CommandUsage => "Usage: ";
    public string CommandPermission => "Level Permission For Access: ";
    public string CommandAlias => "Command Alias：";
}

public class AccountTextEN
{
    public string Desc => "Manage account mappings for SDK logins";
    public string Usage =>
        "Usage: /account create <email> <uid>\n" +
        "Usage: /account delete <email|uid>\n" +
        "Usage: /account list";
    public string Created => "Created account mapping: {0} -> UID {1}";
    public string CreateFailed => "Failed to create account mapping: {0}";
    public string Deleted => "Deleted account mapping: {0} -> UID {1}";
    public string DeleteFailed => "Failed to delete account mapping: {0}";
    public string DeleteOnline => "Cannot delete account while online: {0} -> UID {1}";
    public string NotFound => "Account not found: {0}";
}

/// <summary>
///     path: Game.Command.Girl
/// </summary>
public class GirlTextEN
{
    public string Desc => "Add characters to player\n" +
                          "detail and particular can be found in Resources/ExcelOutput/card.json\n\n" +
                          "Note: -1 means all characters\n";

    public string Usage =>
        "Usage: /girl add <detail/-1> -p<particular> -l<level> -s<star>\n" +
        "Usage: /girl level <guid/-1> <level>\n" +
        "Usage: /girl neuronic <guid/-1> <level>\n" +
        "Usage: /girl break <guid/-1> <level>";

    public string NotFound => "Character not found!";
    public string Added => "Granted {0} character(s) to player!";
    public string UpdateLevel => "Set {1} character(s) to level {0}!";
    public string UpdateNeuronicLevel => "Set {1} character(s) Neuronic to level {0}!";
    public string UpdateBreakLevel => "Set {1} character(s) Manifestation to level {0}!";
}

/// <summary>
///     path: Game.Command.GiveAll
/// </summary>
public class GiveAllTextEN
{
    public string Desc => "Give all items to player\n"+
                          "Note: -1 means all";
    public string Usage => "Usage: /giveall weapon <detail/-1> -p<particular> -l<level>\n" +
                           "Usage: /giveall weaponskin <detail/-1> -p<particular>\n" +
                           "Usage: /giveall card <detail/-1> -p<particular> -l<level>" +
                           "Usage: /giveall profile <detail/-1> -g<genre> -p<particular> -l<level>" +
                           "Usage: /giveall skinpart <detail/-1> -g<genre> -p<particular> -l<level>" +
                           "Usage: /giveall weaponpart <detail/-1> -g<genre> -p<particular> -l<level>" +
                           "Usage: /giveall call <detail/-1> -g<genre> -p<particular> -l<level>\n" +
                           "Usage: /giveall piece <detail/-1> -p<particular> -c<count>\n" +
                           "Usage: /giveall girl <detail/-1> -p<particular>";
    public string NotFound => "{0} not found!";
    public string GiveAllItems => "Added {0} {1} to player!";
}

/// <summary>
///     path: Game.Command.Debug
/// </summary>
public class DebugTextEN
{
    public string Desc => "Toggle debug packet output";
    public string Usage => "Usage: /debug [on|off|simple|detail|file]";
    public string Enabled => "Debug packet output enabled.";
    public string Disabled => "Debug packet output disabled.";
    public string SimpleEnabled => "Simple debug packet output enabled.";
    public string DetailEnabled => "Detailed debug packet output enabled.";
    public string FileEnabled => "Personal debug file output enabled.";
    public string FileDisabled => "Personal debug file output disabled.";
}

public class GameCommandTextEN
{
    public string Desc => "Launch the configured game with patch injection";
    public string Usage => "Usage: /game [extra game args]";
    public string Started => "Game launched. PID: {0}";
    public string Failed => "Failed to launch game: {0}";
}

public class GameCommandCnTextEN
{
    public string Desc => "Launch the specific-channel client with the hosts records managed";
    public string Usage => "Usage: /game-cn [extra game args]";
    public string Started => "Game launched. PID: {0}";
    public string Failed => "Failed to launch game: {0}";
    public string HostsUpdated => "Updated the hosts file: {0}";
    public string HostsCurrent => "The hosts file already contains the managed block.";
    public string HostsFailed => "Failed to update the hosts file: {0}";
    public string NeedAdmin => "Writing the hosts file needs administrator rights. Restart the server elevated, or add this block to {0} by hand:\n{1}";
    public string Unsupported => "Managing the hosts file is not supported on this system.";
}

public class GameCommandHostsCleanTextEN
{
    public string Desc => "Remove the hosts block written by /game-cn";
    public string Usage => "Usage: /hosts-clean";
    public string Removed => "Removed the managed hosts block and flushed the DNS cache: {0}";
    public string NothingToRemove => "The hosts file has no managed block to remove: {0}";
    public string NeedAdmin => "Cleaning the hosts file needs administrator rights. Restart the server elevated and try again: {0}";
    public string Failed => "Failed to clean the hosts file: {0}";
    public string Unsupported => "Managing the hosts file is not supported on this system.";
}

#endregion

#endregion
