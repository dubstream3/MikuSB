using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
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
        version = BuildVersion.Current
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

    [HttpGet("commands")]
    public IActionResult Commands()
    {
        var commands = InGameConsoleBridge.GetCommands?.Invoke();
        if (commands == null) return ConsoleNotReady();

        return Ok(new
        {
            items = commands.Select(command => new
            {
                name = command.Name,
                usage = command.Usage,
                description = command.Description,
                aliases = command.Aliases,
                permissions = command.Permissions
            }).ToArray()
        });
    }

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
        return Ok(new
        {
            region = SetupService.ResolveRegion(config.Loader.Region, config.Loader.Arguments),
            game_path = config.Loader.GamePath,
            install_path = config.Loader.InstallPath.Length > 0 ? config.Loader.InstallPath : probe.Install,
            user_dir = config.Loader.UserDir.Length > 0 ? config.Loader.UserDir : probe.UserDir,
            arguments = config.Loader.Arguments,
            patch_paths = config.Loader.PatchPaths,
            patch_present = config.Loader.PatchPaths.Any(item => System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, item))),
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
        return Ok(new
        {
            probe,
            suggested_arguments = SetupService.BuildArguments(
                SetupService.NormalizeRegion(request?.Region),
                probe.UserDir)
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

        var region = SetupService.NormalizeRegion(request?.Region);
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

        ConfigManager.SaveConfig();

        ConfigManager.Logger.Info($"[setup] applied region={region} game={probe.Exe} userdir={probe.UserDir} address={config.HttpServer.PublicAddress}");
        return Ok(new
        {
            ok = true,
            region,
            game_path = config.Loader.GamePath,
            arguments = config.Loader.Arguments,
            public_address = config.HttpServer.PublicAddress,
            next_steps = region == "cn"
                ? new[] { "点下面的按钮把 7 个 SDK 域名写进 hosts（需要管理员权限）", "正常启动游戏，用官方登录窗口登录" }
                : new[] { "把 Patch\\MikuSB-Patch.dll 放到 Config 同级目录（首次会自动下载）", "用 MikuSB.Loader 启动游戏" }
        });
    }

    /// <summary>
    /// Maintain the CN hosts block. Elevation is requested only when the process itself cannot write the file;
    /// the outcome is always re-read from disk before it is reported.
    /// </summary>
    [HttpPost("setup/hosts")]
    public IActionResult SetupHosts([FromBody] SetupHostsRequest request)
    {
        var enable = request?.Action?.ToLowerInvariant() != "remove";
        var message = SetupService.WriteHosts(enable, request?.TargetIp ?? "127.0.0.1");
        ConfigManager.Logger.Info($"[setup] hosts {(enable ? "add" : "remove")} -> {message}");
        return Ok(new { ok = true, message, hosts = SetupService.ReadHosts() });
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
}

public sealed class SetupHostsRequest
{
    /// <summary>"apply" (default) or "remove".</summary>
    public string? Action { get; set; }

    /// <summary>Address written for every CN SDK domain; defaults to loopback.</summary>
    public string? TargetIp { get; set; }
}
