using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MikuSB.Configuration;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Internationalization;
using MikuSB.SdkServer.Utils;
using MikuSB.Util;
using System.Diagnostics;

namespace MikuSB.SdkServer.Handlers;

/// <summary>
/// JSON API behind the admin panel. Only reachable on <see cref="MikuSB.SdkServer.Utils.AdminApi.Port"/>
/// (loopback) and only with the bearer token, see <see cref="MikuSB.SdkServer.Utils.AdminAuthMiddleware"/>.
///
/// Two rules shape every endpoint here:
/// 1. Nothing in SdkServer may reference GameServer types, so anything that needs the command layer or the
///    live socket table goes through <see cref="InGameConsoleBridge"/> and reports 503 while unwired.
/// 2. SdkServer starts before the database finishes loading (Program/Manager order), so every DB read is
///    gated on <see cref="DatabaseHelper.LoadAllData"/> instead of racing the loader.
/// </summary>
[ApiController]
[Route("api")]
public class AdminApiController : ControllerBase
{
    private static readonly DateTime StartedAt = GetStartedAt();

    /// <summary>
    /// Read-only shape of one account. Password hashes and auth tokens are deliberately absent: a panel
    /// session that leaks must not hand out credentials.
    /// </summary>
    private static object Project(AccountData account, bool online) => new
    {
        uid = account.Uid,
        username = account.Username,
        phone = account.Phone,
        ban_type = account.BanType.ToString(),
        permissions = account.Permissions.Select(perm => perm.ToString()).ToArray(),
        online
    };

    /// <summary>Accounts that hold a live connection right now; also the roster source for GET /online.</summary>
    private static IReadOnlyList<OnlinePlayerInfo> OnlinePlayers()
    {
        try
        {
            return InGameConsoleBridge.GetOnlinePlayers?.Invoke() ?? [];
        }
        catch (Exception)
        {
            // The socket table is mutated without a lock; a torn snapshot must not break the whole page.
            return [];
        }
    }

    private IActionResult DbNotReady() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { code = "db_not_ready", message = "the database is still loading, retry in a moment" });

    private IActionResult ConsoleNotReady() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { code = "console_not_ready", message = "the command layer is not initialised yet" });

    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        ok = true,
        db_loaded = DatabaseHelper.LoadAllData,
        uptime_sec = (int)(DateTime.UtcNow - StartedAt).TotalSeconds,
        version = BuildVersion.Current,
        // What the host is busy with right now. The panel is served before the database and the resource
        // pack exist (Program order), so this is the only way the page can tell "downloading the resource
        // pack" from "running" during a first start.
        phase = ServerRuntime.Phase,
        phase_detail = ServerRuntime.Detail,
        initialized = ServerRuntime.Initialized
    });

    [HttpGet("overview")]
    public IActionResult Overview()
    {
        if (!DatabaseHelper.LoadAllData) return DbNotReady();

        var accounts = DatabaseHelper.GetAllInstance<AccountData>() ?? [];
        var online = OnlinePlayers();

        return Ok(new
        {
            accounts = accounts.Count,
            online = online.Count,
            commands = InGameConsoleBridge.GetCommands?.Invoke()?.Count ?? 0,
            uptime_sec = (int)(DateTime.UtcNow - StartedAt).TotalSeconds,
            server_name = ConfigManager.Config.GameServer.GameServerName
        });
    }

    [HttpGet("accounts")]
    public IActionResult Accounts([FromQuery] string? q = null, [FromQuery] int page = 1,
        [FromQuery] int size = 50)
    {
        if (!DatabaseHelper.LoadAllData) return DbNotReady();

        var all = DatabaseHelper.GetAllInstance<AccountData>() ?? [];
        IEnumerable<AccountData> filtered = all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            filtered = all.Where(account =>
                account.Username.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                account.Uid.ToString() == needle);
        }

        var ordered = filtered.OrderBy(account => account.Uid).ToList();
        var onlineUids = OnlinePlayers().Select(player => player.Uid).ToHashSet();
        var pageSize = Math.Clamp(size, 1, 500);
        var skip = Math.Max(0, (page - 1) * pageSize);

        return Ok(new
        {
            total = ordered.Count,
            page,
            size = pageSize,
            items = ordered.Skip(skip).Take(pageSize)
                .Select(account => Project(account, onlineUids.Contains(account.Uid)))
                .ToArray()
        });
    }

    [HttpPost("accounts")]
    public IActionResult CreateAccount([FromBody] CreateAccountRequest request)
    {
        if (!DatabaseHelper.LoadAllData) return DbNotReady();

        var username = request.Username?.Trim() ?? "";
        if (username.Length == 0)
            return BadRequest(new { code = "invalid_arguments", message = "username is required" });

        try
        {
            var account = AccountData.CreateAccount(username, request.Uid, request.Password ?? "");
            return Ok(new { uid = account.Uid, username = account.Username });
        }
        catch (InvalidOperationException ex)
        {
            // Duplicate username or uid.
            return Conflict(new { code = "conflict", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "invalid_arguments", message = ex.Message });
        }
    }

    [HttpDelete("accounts/{uid:int}")]
    public IActionResult DeleteAccount(int uid)
    {
        if (!DatabaseHelper.LoadAllData) return DbNotReady();

        var account = AccountData.GetAccountByUid(uid);
        if (account == null) return NotFound(new { code = "not_found", message = $"no account with uid {uid}" });

        // Deleting a logged-in account would leave its player instance holding rows that no longer exist.
        if (OnlinePlayers().Any(player => player.Uid == uid))
            return Conflict(new { code = "account_online", message = "the account is logged in, disconnect it first" });

        AccountData.DeleteAccount(uid);
        return Ok(new { deleted = uid, username = account.Username });
    }

    [HttpGet("online")]
    public IActionResult Online()
    {
        var players = OnlinePlayers();
        return Ok(new
        {
            count = players.Count,
            items = players.Select(player => new
            {
                uid = player.Uid,
                name = player.Name,
                level = player.Level,
                remote_address = player.RemoteAddress
            }).ToArray()
        });
    }

    [HttpGet("players/{uid:int}")]
    public IActionResult Player(int uid)
    {
        if (!DatabaseHelper.LoadAllData) return DbNotReady();

        var account = AccountData.GetAccountByUid(uid);
        if (account == null) return NotFound(new { code = "not_found", message = $"no account with uid {uid}" });

        var data = PlayerGameData.GetPlayerByUid(uid);
        var inventory = DatabaseHelper.GetInstance<InventoryData>(uid);
        var catalog = InGameConsoleBridge.GetCurrencyCatalog?.Invoke();
        var names = catalog?.ToDictionary(entry => entry.Id, entry => entry.Name)
                    ?? new Dictionary<uint, string>();

        // AttrIds layout: gid 1 is the wallet, sid = moneyType * 2 + 1. Vigor (moneyType 4) is a player field
        // and the client reads it from there, so it would otherwise show the stale mirror in Attrs.
        var wallet = (data?.Attrs ?? [])
            .Where(attr => attr.Gid == AttrIds.Currency.GroupId)
            .ToDictionary(attr => attr.Sid, attr => attr.Val);

        var currencies = wallet.Keys
            .Where(sid => sid % 2 == 1)
            .OrderBy(sid => sid)
            .Select(sid =>
            {
                var moneyType = (sid - 1) / 2;
                var isVigor = moneyType == AttrIds.Currency.Vigor;
                return new
                {
                    id = moneyType,
                    name = names.TryGetValue(moneyType, out var name) ? name : $"#{moneyType}",
                    value = isVigor ? data?.Vigor ?? wallet[sid] : wallet[sid]
                };
            })
            .ToArray();

        return Ok(new
        {
            uid = account.Uid,
            username = account.Username,
            ban_type = account.BanType.ToString(),
            online = OnlinePlayers().Any(player => player.Uid == uid),
            player = data == null
                ? null
                : new
                {
                    name = data.Name ?? "",
                    level = data.Level,
                    exp = data.Exp,
                    vigor = data.Vigor,
                    register_time = data.RegisterTime,
                    last_active_time = data.LastActiveTime,
                    currency_seeded = data.CurrencySeeded
                },
            inventory = new
            {
                items = inventory?.Items?.Count ?? 0,
                weapons = inventory?.Weapons?.Count ?? 0,
                skins = inventory?.Skins?.Count ?? 0,
                support_cards = inventory?.SupportCards?.Count ?? 0
            },
            currencies
        });
    }

    [HttpGet("currencies")]
    public IActionResult Currencies()
    {
        var catalog = InGameConsoleBridge.GetCurrencyCatalog?.Invoke();
        if (catalog == null) return ConsoleNotReady();

        return Ok(new { items = catalog.Select(entry => new { id = entry.Id, name = entry.Name }).ToArray() });
    }

    /// <summary>
    /// The console command table. Command attributes double as the in-game prompt text, so most of them store
    /// an I18N key (<c>Game.Command.GiveAll.Desc</c>) instead of a sentence; the panel asks for a language and
    /// the keys are resolved here, otherwise the console page would print the raw keys. The server only ships
    /// Chinese (simplified/traditional) and English tables, so every other panel language falls back to EN.
    /// </summary>
    [HttpGet("commands")]
    public IActionResult Commands([FromQuery] string? lang = null)
    {
        var commands = InGameConsoleBridge.GetCommands?.Invoke();
        if (commands == null) return ConsoleNotReady();

        var language = PanelLanguage(lang);
        return Ok(new
        {
            items = commands.Select(command => new
            {
                name = command.Name,
                usage = TranslateCommandText(command.Usage, language),
                description = TranslateCommandText(command.Description, language),
                aliases = command.Aliases,
                permissions = command.Permissions
            }).ToArray()
        });
    }

    /// <summary>Maps a panel language id (<c>zh_CN</c>, <c>en_US</c>, …) onto a shipped text table.</summary>
    private static string PanelLanguage(string? lang) => (lang ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "zh_cn" or "zh_hans" or "zh" => "CHS",
        "zh_tw" or "zh_hant" or "zh_hk" => "CHT",
        _ => "EN"
    };

    /// <summary>
    /// Resolves I18N keys, leaves literal text alone. Attributes that were never keyed keep working, so a new
    /// command only has to switch to a key if it wants to show up localized.
    /// </summary>
    private static string TranslateCommandText(string text, string language) =>
        text.StartsWith("Game.Command.", StringComparison.Ordinal)
            ? I18NManager.TranslateAsCertainLang(language, text)
            : text;

    /// <summary>
    /// Runs one console command as the server console. The command layer reports failure as localized text
    /// with no status code, so <c>accepted</c> only means "the command ran"; read <c>messages</c> for the
    /// outcome. Clients that need certainty should check the affected resource afterwards.
    /// </summary>
    [HttpPost("console")]
    public async Task<IActionResult> Console([FromBody] ConsoleCommandRequest request)
    {
        var execute = InGameConsoleBridge.ExecuteCommandAsync;
        if (execute == null) return ConsoleNotReady();

        var command = request.Command?.Trim() ?? "";
        if (command.Length == 0)
            return BadRequest(new { code = "invalid_arguments", message = "command is required" });

        var result = await execute(command, HttpContext.RequestAborted);
        return Ok(new { accepted = true, success = result.Success, messages = result.Messages });
    }

    /// <summary>
    /// The replay page's whole dataset: every DLC era the resource repo ships, the characters and outfits each
    /// era handed out, and which era is served right now. Read-only and independent of the player database, so
    /// it also answers while the database is still loading. <paramref name="lang"/> picks the locale column used
    /// for era names, character names and outfit names (any of <see cref="ReplayCatalog.Locales"/>).
    /// </summary>
    [HttpGet("replay")]
    public IActionResult Replay([FromQuery] string? lang = null)
    {
        return Ok(ReplayCatalog.Load(lang));
    }

    /// <summary>
    /// Serves one era - the panel's "click a card, replay it". Executes the existing /dlc command instead of
    /// re-seeding attributes here: that command is what re-pushes era attributes and time windows to every
    /// connected player, and duplicating it would drift the moment the era data gains a new module.
    /// <c>save=true</c> adds -save, which persists the pin into Config.json so it survives a restart.
    /// </summary>
    [HttpPost("replay")]
    public async Task<IActionResult> SwitchReplay([FromBody] ReplayRequest request, [FromQuery] string? lang = null)
    {
        var execute = InGameConsoleBridge.ExecuteCommandAsync;
        if (execute == null) return ConsoleNotReady();

        var eras = ReplayCatalog.Load(lang).Eras;
        var era = eras.FirstOrDefault(item => item.Id == request.Id);
        if (request.Id != 0 && era == null)
            return BadRequest(new { code = "invalid_arguments", message = $"DLC {request.Id} is not in dlc_activities.json" });

        // Withdrawn eras keep a card on the page for reference only; the command rejects them too, so the
        // check lives here as well to answer with a code the page can localise instead of a raw reply.
        if (era is { Retired: true })
            return BadRequest(new { code = "era_retired", message = $"DLC {request.Id} data has been removed" });

        var command = request.Id == 0 ? "/dlc off" : "/dlc " + request.Id;
        if (request.Save) command += " -save";

        var result = await execute(command, HttpContext.RequestAborted);

        // Re-read so `current` reflects the switch that just happened rather than the pre-call snapshot.
        return Ok(new { accepted = true, command, messages = result.Messages, current = ReplayCatalog.Load(lang).Current });
    }

    /// <summary>
    /// Entry-button art for one era, exported offline from the client's cooked textures into
    /// <c>Resources/ui/replay/dlc-{id}.png</c>. Served through the API rather than as a static file so the
    /// same bearer token covers it; 404 means "this era has no exported art" and the card falls back to text.
    /// </summary>
    [HttpGet("replay/image/{id:int}")]
    public IActionResult ReplayImage(int id)
    {
        var file = ReplayCatalog.ImageFile((uint)id);
        if (file == null) return NotFound(new { code = "not_found", message = $"no exported art for DLC {id}" });

        // Revalidate instead of a fixed max-age: re-running scripts/export_era_images.py replaces the PNG
        // under the same URL, and the ETag still makes the common case a cheap 304.
        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(file, "image/png");
    }

    // ---------------------------------------------------------------------------------------------
    // Setup wizard: it only fills Config.json in, and for the CN build it maintains the hosts block.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Current setup state: what the config says, what the disk says, and what hosts says.</summary>
    [HttpGet("setup")]
    public IActionResult Setup()
    {
        var config = ConfigManager.Config;
        var probe = SetupService.Probe(config.Loader.GamePath);
        var region = SetupService.ResolveRegion(config.Loader.Region, config.Loader.Arguments);
        return Ok(new
        {
            region,
            configured = SetupService.IsConfigured(region, config.Loader.GamePath) && probe.Valid,
            game_path = config.Loader.GamePath,
            install_path = config.Loader.InstallPath.Length > 0 ? config.Loader.InstallPath : probe.Install,
            user_dir = config.Loader.UserDir.Length > 0 ? config.Loader.UserDir : probe.UserDir,
            arguments = config.Loader.Arguments,
            patch_paths = config.Loader.PatchPaths,
            patch_present = PatchPresent(config),
            cn_launch_mode = NormalizeCnLaunchMode(config.Loader.CnLaunchMode) ?? "dll",
            cn_patch_path = config.Loader.CnPatchPath,
            cn_patch_present = CnPatchPresent(config),
            elevated = SetupService.IsElevated(),
            proxy = new { enabled = config.Proxy.Enabled, set_all_proxy = config.Loader.SetAllProxy, port = config.Proxy.Port },
            public_address = config.HttpServer.PublicAddress,
            game_public_address = config.GameServer.PublicAddress,
            ports = new { http = config.HttpServer.Port, game = config.GameServer.Port, admin = config.HttpServer.AdminPort },
            regions = SetupService.Regions,
            addresses = SetupService.LocalAddresses(),
            probe,
            hosts = SetupService.ReadHosts(),
            cn_domains = SetupService.CnDomains
        });
    }

    /// <summary>
    /// Validate a path the operator typed. Returns every individual check so the page can say which piece is
    /// missing instead of a bare "invalid".
    /// </summary>
    [HttpPost("setup/probe")]
    public IActionResult SetupProbe([FromBody] SetupProbeRequest request)
    {
        var probe = SetupService.Probe(request?.Path);
        // No region picked yet means there is nothing to suggest: falling back to the global arguments here is
        // what made an untouched config look like a deliberate global install.
        var region = SetupService.NormalizeRegionOrNull(request?.Region);
        string[]? suggested = region == null ? null : SetupService.BuildArguments(region, probe.UserDir);
        return Ok(new { probe, suggested_arguments = suggested });
    }

    /// <summary>
    /// Step one of the wizard: pin the channel and make sure that channel's own patch DLL is on disk. The
    /// global build and the CN build both pull their own DLL from the release feed, so this only
    /// has to check the file survived the copy.
    ///
    /// It runs synchronously on purpose: the panel unlocks the path step only after this answers, and a
    /// download that finishes minutes later would leave the wizard in a state where "启动游戏" fails with a
    /// far less obvious message than a missing DLL.
    /// </summary>
    [HttpPost("setup/channel")]
    public IActionResult SetupChannel([FromBody] SetupChannelRequest request)
    {
        var region = SetupService.NormalizeRegionOrNull(request?.Region);
        if (region == null)
        {
            return BadRequest(new { code = "region_required", message = "先选择服务器区域（国服或国际服）" });
        }

        var config = ConfigManager.Config;

        if (region == "cn")
        {
            // The CN build is a release asset, so this step fetches it the same way the global branch
            // below does; only a network failure leaves the operator with a message instead of a file.
            var downloaded = false;
            try
            {
                downloaded = PatchDownloadService.EnsureCnPatchPresent();
            }
            catch (Exception ex)
            {
                ConfigManager.Logger.Warn($"[setup] CN patch DLL download failed: {ex.Message}");
            }

            var ready = CnPatchPresent(config);
            return Ok(new
            {
                ok = ready,
                region,
                patch_ready = ready,
                patch_path = config.Loader.CnPatchPath,
                // The panel localises this row from the code; detail stays as the raw fallback.
                code = ready ? "cn_ready" : (downloaded ? "cn_missing" : "cn_download_failed"),
                detail = ready
                    ? $"国服补丁 DLL 已就位：{config.Loader.CnPatchPath}"
                    : downloaded
                        ? $"国服补丁 DLL 不在 {config.Loader.CnPatchPath}：把 CN 版 MikuSB-Patch.dll 放到该路径后再继续。"
                        : $"国服补丁 DLL 下载失败：{config.Loader.CnPatchPath}。请检查网络后重试。"
            });
        }

        try
        {
            PatchDownloadService.EnsurePatchPresent();
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                ok = false,
                region,
                patch_ready = false,
                patch_path = config.Loader.PatchPaths.Length > 0 ? config.Loader.PatchPaths[0] : "",
                code = "download_failed",
                detail = $"下载国际服补丁 DLL 失败：{ex.Message}"
            });
        }

        var present = PatchPresent(config);
        return Ok(new
        {
            ok = present,
            region,
            patch_ready = present,
            patch_path = config.Loader.PatchPaths.Length > 0 ? config.Loader.PatchPaths[0] : "",
            code = present ? "ready" : "missing",
            detail = present ? "国际服补丁 DLL 已就位。" : "国际服补丁 DLL 仍未下载成功，检查网络后重试。"
        });
    }

    /// <summary>Look for an installed client on the fixed drives (one folder level per drive).</summary>
    [HttpGet("setup/detect")]
    public IActionResult SetupDetect() => Ok(new { candidates = SetupService.Suggest() });

    /// <summary>
    /// Write the wizard's answers into Config.json. The command args are regenerated from the region so a
    /// half-edited file cannot keep a stale <c>-userdir</c> pointing at another machine's folder.
    /// </summary>
    [HttpPost("setup/apply")]
    public IActionResult SetupApply([FromBody] SetupApplyRequest request)
    {
        var probe = SetupService.Probe(request?.GamePath);
        if (!probe.Valid)
        {
            return BadRequest(new { code = "invalid_path", message = "客户端路径未通过检查", probe });
        }

        // The region decides the channel argument and whether hosts get rewritten, so it has to be an explicit
        // answer from the operator rather than a default we invent here.
        var region = SetupService.NormalizeRegionOrNull(request?.Region);
        if (region == null)
        {
            return BadRequest(new { code = "region_required", message = "先选择服务器区域（国服或国际服）", probe });
        }

        var config = ConfigManager.Config;

        config.Loader.Region = region;
        config.Loader.GamePath = probe.Exe;
        config.Loader.InstallPath = probe.Install;
        config.Loader.UserDir = probe.UserDir;
        config.Loader.Arguments = SetupService.BuildArguments(region, probe.UserDir);

        if (!string.IsNullOrWhiteSpace(request?.PublicAddress))
        {
            config.HttpServer.PublicAddress = request!.PublicAddress!.Trim();
            config.GameServer.PublicAddress = request!.PublicAddress!.Trim();
        }

        // CN is redirected at the DNS layer, so the SOCKS proxy is not part of that flow; the global build
        // routes the client through it. Only switch the flag on, never off: clearing it by accident would
        // silently change a choice the operator made by hand.
        if (region != "cn")
        {
            config.Loader.SetAllProxy = true;
        }
        if (region != "cn" && config.Loader.PatchPaths.Length == 0)
        {
            config.Loader.PatchPaths = [@"Patch\MikuSB-Patch.dll"];
        }

        // The wizard asks for the CN launch method before it writes the config, because the method decides
        // what happens to the hosts block: hosts mode points the client's SDK domains at this machine, while
        // DLL mode must leave hosts untouched and inject Patch\MikuSB-Patch-cn.dll instead.
        var mode = NormalizeCnLaunchMode(request?.Mode);
        if (region == "cn" && mode != null)
        {
            config.Loader.CnLaunchMode = mode;
        }

        var cnMode = NormalizeCnLaunchMode(config.Loader.CnLaunchMode) ?? "dll";
        if (region == "cn")
        {
            // The CN client is pulled to this machine by the DLL or the hosts block, never by the SOCKS
            // proxy: exporting ALL_PROXY makes the SDK resolve the name inside the proxy and talk to the
            // real server, which is what "the redirect stopped working" looks like from the panel.
            config.Loader.SetAllProxy = false;
            config.Loader.ManageCnHosts = cnMode == "hosts";
        }

        ConfigManager.SaveConfig();

        var hostsSync = region != "cn"
            ? null
            : cnMode == "dll"
                ? SetupService.ClearHosts()
                : SetupService.SyncHosts(config.HttpServer.PublicAddress);

        ConfigManager.Logger.Info($"[setup] applied region={region} game={probe.Exe} userdir={probe.UserDir} address={config.HttpServer.PublicAddress} mode={cnMode} hosts={hostsSync?.Status ?? "n/a"}");
        return Ok(new
        {
            ok = true,
            region,
            game_path = config.Loader.GamePath,
            arguments = config.Loader.Arguments,
            public_address = config.HttpServer.PublicAddress,
            elevated = SetupService.IsElevated(),
            cn_launch_mode = cnMode,
            hosts_sync = hostsSync,
            // Codes first: the panel translates them, so the wizard stops pasting Chinese sentences into the
            // other nine locales. next_steps keeps the prose for anyone reading the API by hand.
            next_step_codes = region == "cn"
                ? cnMode == "dll"
                    ? new[] { "cn_dll" }
                    : new[] { "cn_hosts" }
                : new[] { "patch_dir", "launch" },
            next_steps = region == "cn"
                ? cnMode == "dll"
                    ? new[] { "国服：DLL 注入模式；用「启动游戏」按钮启动" }
                    : new[] { "国服：hosts 模式，直接用官方启动器/游戏 exe 启动" }
                : new[] { "把 Patch\\MikuSB-Patch.dll 放到 Config 同级目录（首次会自动下载）", "用「启动游戏」按钮启动" }
        });
    }

    /// <summary>
    /// Maintain the CN hosts block by hand: <c>remove</c> drops it, anything else syncs it to
    /// <c>target_ip</c>. Elevation is requested only when this process cannot write the file, and the outcome
    /// is always re-read from disk before it is reported - a declined UAC prompt is never reported as success.
    /// </summary>
    [HttpPost("setup/hosts")]
    public IActionResult SetupHosts([FromBody] SetupHostsRequest request)
    {
        var remove = string.Equals(request?.Action, "remove", StringComparison.OrdinalIgnoreCase);
        var result = remove
            ? SetupService.ClearHosts()
            : SetupService.SyncHosts(request?.TargetIp ?? "127.0.0.1");
        ConfigManager.Logger.Info($"[setup] hosts {(remove ? "remove" : "sync")} -> {result.Status}: {result.Message}");
        return Ok(new
        {
            ok = result.Status is "applied" or "already" or "removed" or "clear",
            status = result.Status,
            message = result.Message,
            entries = result.Entries,
            elevated = SetupService.IsElevated(),
            hosts = SetupService.ReadHosts()
        });
    }

    /// <summary>
    /// Starts the client from the panel. The command depends on the region: the global build goes through
    /// the upstream loader (<c>/game</c>), which is the only place the patch DLL is fetched -
    /// <c>GameLaunchService.Launch</c> calls <c>PatchDownloadService.EnsurePatchPresent()</c> before
    /// injecting, so a fresh install can go from "no patch" to a running client with one click. The CN build
    /// is started either way the operator picked: <c>/game-cn</c> writes the hosts block and starts the
    /// untouched client, <c>/game-cn -dll</c> injects the CN-channel patch and leaves hosts alone. Neither goes
    /// through the loader, so this request can only report the console command's own answer.
    /// </summary>
    [HttpPost("setup/launch")]
    public async Task<IActionResult> SetupLaunch([FromBody] SetupLaunchRequest? request)
    {
        var config = ConfigManager.Config;
        if (string.IsNullOrWhiteSpace(config.Loader.GamePath))
            return BadRequest(new { code = "invalid_path", message = "Loader.GamePath is not configured yet." });

        var extra = (request?.Arguments ?? "").Trim();
        var arguments = extra.Length == 0
            ? Array.Empty<string>()
            : extra.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var region = SetupService.ResolveRegion(config.Loader.Region, config.Loader.Arguments);

        // The CN build has two routes to the same server, so the panel's buttons say which one they mean. Sending
        // the mode also stores it, which keeps the overview's launch button on whatever the operator last chose.
        var cnMode = NormalizeCnLaunchMode(request?.Mode) ?? NormalizeCnLaunchMode(config.Loader.CnLaunchMode) ?? "dll";
        if (NormalizeCnLaunchMode(request?.Mode) is { } requestedMode && requestedMode != config.Loader.CnLaunchMode)
        {
            config.Loader.CnLaunchMode = requestedMode;
            ConfigManager.SaveConfig();
        }

        var command = region == "cn"
            ? (cnMode == "dll" ? "/game-cn -dll" : "/game-cn")
            : "/game";
        if (arguments.Length > 0)
            command += " " + string.Join(' ', arguments);

        // Preferred path: ask the loader itself, so the answer is a pid or the real reason. The CN build has
        // no loader in front of it, so it goes straight to the console command.
        var launch = region == "cn" ? null : InGameConsoleBridge.LaunchGame;
        if (launch != null)
        {
            var outcome = launch(arguments);
            ConfigManager.Logger.Info($"[setup] launch -> ok={outcome.Ok} pid={outcome.Pid} {outcome.Message}");
            return Ok(new
            {
                accepted = true,
                success = outcome.Ok,
                command,
                pid = outcome.Pid,
                messages = new[] { outcome.Ok ? $"pid {outcome.Pid}" : outcome.Message },
                mode = "loader",
                patch_present = PatchPresent(config),
                game_path = config.Loader.GamePath
            });
        }

        var execute = InGameConsoleBridge.ExecuteCommandAsync;
        if (execute == null) return ConsoleNotReady();

        var result = await execute(command, HttpContext.RequestAborted);
        ConfigManager.Logger.Info($"[setup] launch (console) -> {string.Join(" | ", result.Messages)}");

        return Ok(new
        {
            accepted = true,
            success = result.Success,
            command,
            pid = 0,
            messages = result.Messages,
            mode = region == "cn" ? cnMode : "loader",
            cn_patch_present = CnPatchPresent(config),
            patch_present = PatchPresent(config),
            game_path = config.Loader.GamePath
        });
    }

    /// <summary>
    /// Latest release as the updater sees it. <c>force</c> skips the five minute cache for the refresh button.
    /// </summary>
    [HttpGet("update")]
    public async Task<IActionResult> UpdateInfo([FromQuery] bool force = false)
    {
        var check = InGameConsoleBridge.CheckUpdate;
        if (check is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "update_not_ready",
                message = "The updater is not wired yet."
            });

        var status = await check(force);
        if (status is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "update_not_ready",
                message = "The updater did not answer."
            });

        return Ok(new
        {
            ok = status.Ok,
            current = status.Current,
            latest = status.Latest,
            available = status.Available,
            asset = status.Asset,
            size = status.Size,
            notes = status.Notes,
            error = status.Error
        });
    }

    /// <summary>
    /// Hands control to the updater. It downloads, swaps the files and restarts, so this returns before the work
    /// finishes and the panel will lose the connection; that is the expected behaviour, not a failure.
    /// </summary>
    [HttpPost("update")]
    public IActionResult ApplyUpdate()
    {
        var apply = InGameConsoleBridge.ApplyUpdate;
        if (apply is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "update_not_ready",
                message = "The updater is not wired yet."
            });

        _ = Task.Run(async () =>
        {
            try
            {
                await apply();
            }
            catch (Exception exception)
            {
                ConfigManager.Logger.Error("Update requested from the admin panel failed.", exception);
            }
        });

        return Ok(new
        {
            accepted = true,
            message = "The updater is taking over; the panel will disconnect for a moment."
        });
    }

    /// <summary>
    /// True when at least one entry of <c>Loader.PatchPaths</c> resolves next to the executable. Kept in one
    /// place because the setup page shows it, and the launch answer has to be able to say how it changed.
    /// </summary>
    private static bool PatchPresent(ConfigContainer config)
    {
        return config.Loader.PatchPaths.Any(item =>
            System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, item)));
    }

    /// <summary>
    /// True when the CN-channel patch DLL that <c>/game-cn -dll</c> injects is in place. Its own check because
    /// it is not part of <c>Loader.PatchPaths</c>: nothing downloads it, the operator copies it in.
    /// </summary>
    private static bool CnPatchPresent(ConfigContainer config)
    {
        return !string.IsNullOrWhiteSpace(config.Loader.CnPatchPath)
               && System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, config.Loader.CnPatchPath));
    }

    /// <summary>
    /// The CN launch route as one of the two known values, or <c>null</c> when the stored value says neither.
    /// Serialised configs are edited by hand often enough that an unknown string must not silently become
    /// "dll" and turn every launch into an injection the operator never asked for.
    /// </summary>
    private static string? NormalizeCnLaunchMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "dll" => "dll",
            "hosts" => "hosts",
            _ => null
        };
    }

    private static DateTime GetStartedAt()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            return DateTime.UtcNow;
        }
    }
}

public sealed class CreateAccountRequest
{
    public string? Username { get; set; }
    public int Uid { get; set; }
    public string? Password { get; set; }
}

public sealed class ConsoleCommandRequest
{
    public string? Command { get; set; }
}

public sealed class ReplayRequest
{
    /// <summary>DLC era id from dlc_activities.json; 0 means "serve no era" (/dlc off).</summary>
    public uint Id { get; set; }

    /// <summary>Persist the pin into Config.json (-save) instead of applying it to this run only.</summary>
    public bool Save { get; set; } = true;
}

public sealed class SetupProbeRequest
{
    /// <summary>Whatever the operator pasted: install root, data\game, Win64 or the exe itself.</summary>
    public string? Path { get; set; }

    /// <summary>Only used to suggest launch arguments alongside the probe result.</summary>
    public string? Region { get; set; }
}

public sealed class SetupApplyRequest
{
    public string? Region { get; set; }
    public string? GamePath { get; set; }

    /// <summary>Address clients should use; blank keeps the configured value.</summary>
    public string? PublicAddress { get; set; }

    /// <summary>
    /// CN only: <c>hosts</c> or <c>dll</c>. Picking it here is what decides whether the hosts block gets
    /// written, so the wizard asks before the config is saved rather than after.
    /// </summary>
    public string? Mode { get; set; }
}

public sealed class SetupChannelRequest
{
    /// <summary><c>cn</c> or <c>global</c>; anything else is rejected.</summary>
    public string? Region { get; set; }
}

public sealed class SetupHostsRequest
{
    /// <summary>"apply" (default) or "remove".</summary>
    public string? Action { get; set; }

    /// <summary>Address written for every CN SDK domain; defaults to loopback.</summary>
    public string? TargetIp { get; set; }
}

public sealed class SetupLaunchRequest
{
    /// <summary>Extra arguments appended to the <c>/game</c> command; usually blank.</summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// CN only: <c>hosts</c> (rewrite the hosts block, start the untouched client) or <c>dll</c> (inject the
    /// CN-channel patch). Also stored, so the overview button keeps using the same route.
    /// </summary>
    public string? Mode { get; set; }
}
