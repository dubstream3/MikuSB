using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MikuSB.Database.Account;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.SdkServer.Handlers;

/// <summary>
/// Emulation of the China (jinshan channel) SDK surface.
///
/// The CN client does not use the international seasun endpoints at all. It talks to
///   * https://js2sdk.xoyo.com:11443/v6/...        (account / captcha / login)
///   * https://sh-jxsj.xgsdk.com:18443/...         (xg config, privacy pages, login by token)
///   * https://sh-jxsj.xgsdk.com:31443/data/report (telemetry)
///   * https://xgdata.xgsdk.com/bisdk/batchpush    (telemetry)
/// Those hosts are pointed at this process through the hosts file, so every route below is
/// served from the same TLS listener as the international flow.
///
/// Real-server reply shapes were recovered from the client's own Game.log, e.g. loginCaptcha:
/// {"code":1,"msg":"操作成功","fuzzyMobile":null,"fuzzyMobileList":null,"responseSuccess":true}.
/// Replies are written with the default (PascalCase-untouched) serializer on purpose: MVC is
/// configured with SnakeCaseLower, which would turn responseSuccess into response_success.
///
/// The /v6/{**rest} catch-all answers any js2sdk endpoint that has not been seen yet, so a new
/// client build cannot dead-end on a missing route: the request is logged and answered with a
/// permissive success, which keeps the login flow moving and makes the next call observable.
/// </summary>
[ApiController]
public class JinshanController : ControllerBase
{
    private const string DefaultAccountKey = "cn_jinshan@local";

    private static readonly Logger Logger = new("Jinshan");

    /// <summary>Local stand-ins for the CN endpoints; every one of them is hosts-redirected here.</summary>
    private const string Js2SdkBase = "https://js2sdk.xoyo.com:11443";
    private const string XgSdkBase = "https://sh-jxsj.xgsdk.com:18443";
    private const string XgSdkReportUrl = "https://sh-jxsj.xgsdk.com:31443/data/report";

    /// <summary>Server list in the same shape MikuSB gives the international client.</summary>
    private static object ServerListPayload()
    {
        var host = ConfigManager.Config.GameServer.PublicAddress;
        var port = ConfigManager.Config.GameServer.Port;
        var httpHost = ConfigManager.Config.HttpServer.PublicAddress;
        var httpPort = ConfigManager.Config.HttpServer.Port;

        return Ok1(new Dictionary<string, object?>
        {
            ["version"] = "3.6.0",
            ["server_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["servers"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = 1,
                    ["server_id"] = 1,
                    ["name"] = "MikuSB",
                    ["title"] = "MikuSB",
                    ["host"] = host,
                    ["ip"] = host,
                    ["port"] = port,
                    ["status"] = 1,
                    ["state"] = 1,
                    ["is_open"] = true,
                    ["open"] = true,
                    ["recommend"] = true
                }
            },
            ["game_server"] = new Dictionary<string, object?> { ["host"] = host, ["ip"] = host, ["port"] = port },
            ["http_server"] = new Dictionary<string, object?> { ["host"] = httpHost, ["port"] = httpPort }
        });
    }

    /// <summary>Serializes with the exact casing used by the real jinshan/XGSDK services.</summary>
    private static ContentResult Json(object payload) => Utils.ExactJson.Serialize(payload);

    private static object Ok1(object? data = null) => new
    {
        code = 1,
        msg = "操作成功",
        responseSuccess = true,
        data = data ?? new { }
    };

    private static JsonNode? Lookup(JsonNode? node, string name)
    {
        if (node is not JsonObject obj)
            return null;

        if (obj.TryGetPropertyValue(name, out var direct) && direct is JsonValue value
            && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return direct;
        }

        foreach (var container in new[] { "data", "ext", "userInfo", "params" })
        {
            if (obj.TryGetPropertyValue(container, out var child))
            {
                var found = Lookup(child, name);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private async Task<string?> BodyValue(string name)
    {
        if (Request.ContentLength is null or 0)
            return null;

        Request.EnableBuffering();
        Request.Body.Position = 0;
        try
        {
            var node = await JsonNode.ParseAsync(Request.Body);
            var hit = Lookup(node, name);
            return hit is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Request.Body.Position = 0;
        }
    }

    /// <summary>
    /// True when the request comes from this machine (the host client) rather than over the LAN.
    /// Shares LanAddress with the game-server side so both agree on who is a guest: the host client
    /// reaches the SDK through the local proxy (loopback) but may also use the host's own LAN address.
    /// </summary>
    private bool IsLoopbackRequest() => LanAddress.IsLocal(HttpContext?.Connection.RemoteIpAddress);

    /// <summary>
    /// Maps a CN SDK login onto an account.
    ///
    /// With <c>ServerOption.PerIdentityAccount</c> (the default) the identity the tester typed picks
    /// the account, so different usernames land on different uids with independent progress. The key
    /// is the identity itself; a legacy <c>cn_</c>-prefixed key is tried as well so accounts created
    /// by older builds stay reachable (uid 1..3 are <c>cn_*</c>, uid 4/5/6 are bare).
    ///
    /// <c>PinLoginIdentity</c> / <c>LanGuestLoginIdentity</c> are now only consulted for logins that
    /// carry no identity at all, and a LAN client that types nothing gets a per-address key so two
    /// silent guests cannot end up sharing the host's player. With PerIdentityAccount off this is the
    /// old behaviour: the pin wins no matter what was typed.
    /// </summary>
    private AccountData? ResolveLoginAccount(string? identity, string? password, out string reason)
    {
        reason = "";
        var options = ConfigManager.Config.ServerOption;
        var loopback = IsLoopbackRequest();

        // A typed identity owns the login outright: the pin must NOT be able to answer for it, or the
        // first login of a brand-new name would silently fall through to the pinned account.
        var typed = options.PerIdentityAccount && !string.IsNullOrWhiteSpace(identity) ? identity : null;

        var keys = new List<string>();
        if (typed != null)
        {
            keys.Add(typed);
            keys.Add($"cn_{typed}");
        }
        else
        {
            var pinned = options.PinLoginIdentity;
            if (!string.IsNullOrWhiteSpace(options.LanGuestLoginIdentity) && !loopback)
                pinned = options.LanGuestLoginIdentity;

            if (!string.IsNullOrWhiteSpace(pinned))
                keys.Add(pinned);

            if (keys.Count == 0)
            {
                // Nobody typed anything and nothing is pinned: still give every machine its own player
                // rather than funnelling every silent login into one shared account.
                keys.Add(loopback ? DefaultAccountKey : $"lan_{HttpContext?.Connection.RemoteIpAddress}");
            }
        }

        foreach (var key in keys)
        {
            var found = AccountData.GetAccountByUserName(key);
            if (found == null)
                continue;
            if (!VerifyLoginPassword(found, password, out reason))
                return null;
            return found;
        }

        var newKey = keys[0];
        if (!options.AutoCreateUser)
        {
            reason = $"account '{newKey}' does not exist and AutoCreateUser is off";
            return null;
        }

        var defaultPassword = string.IsNullOrEmpty(options.LoginDefaultPassword)
            ? "123456"
            : options.LoginDefaultPassword;
        try
        {
            AccountData.CreateAccount(newKey, 0, defaultPassword);
            Logger.Info($"Created account '{newKey}' on first login " +
                        $"(remote={HttpContext?.Connection.RemoteIpAddress}).");
        }
        catch (Exception ex)
        {
            reason = $"could not create account '{newKey}': {ex.Message}";
            return null;
        }

        return AccountData.GetAccountByUserName(newKey);
    }

    /// <summary>
    /// Password gate for a resolved account. Off by default because the SMS flow the CN client
    /// actually uses (/v6/loginBySms) sends no password at all; turn on
    /// <c>ServerOption.LoginPasswordRequired</c> to require one.
    /// </summary>
    private static bool VerifyLoginPassword(AccountData account, string? password, out string reason)
    {
        reason = "";
        if (!ConfigManager.Config.ServerOption.LoginPasswordRequired)
            return true;

        // An account stored without a password has nothing to check against.
        if (string.IsNullOrEmpty(account.Password))
            return true;

        if (string.IsNullOrEmpty(password))
        {
            reason = $"account '{account.Username}' requires a password";
            return false;
        }

        if (!AccountData.VerifyPassword(account, password))
        {
            reason = $"wrong password for account '{account.Username}'";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Real-name / anti-addiction block of the login reply.
    ///
    /// The CN client logs "Field X was not found" for every key it expects but cannot find, so the
    /// key names below are the ones recovered from its own Game.log after a login attempt:
    ///   LogJson: Warning: Field certification was not found.
    ///   LogJson: Warning: Field needVerifyIdCard was not found.
    ///   LogJson: Warning: Field leftSeconds was not found.
    /// Without them the SDK falls back to "uncertified minor": UXGSDKWinHelper::CheckIdCard(),
    /// "Start ShouldShowPreventAddictionRemind", "inRate = -600" and finally a forced
    /// "Quit game, Quit Reason: XGSDK" behind the 防沉迷 dialog.
    ///
    /// certification matches EXGSDKCertification: 0 = EC_Uncertified, 1 = EC_Certified_Young,
    /// 2 = EC_Certified (adult). leftSeconds is the remaining playable time.
    /// </summary>
    private static Dictionary<string, object?> RealNameBlock() => new()
    {
        ["certification"] = 2,
        ["needVerifyIdCard"] = false,
        ["leftSeconds"] = 86400,
        ["realNameStatus"] = 2,
        ["isCertified"] = true,
        ["isAdult"] = true,
        ["isMinor"] = false,
        ["age"] = 30,
        ["idNumber"] = "110101199001011234",
        ["hasBindedPhone"] = true,
        ["hasBindedEmail"] = true
    };

    /// <summary>Builds the login reply: uid + token that the game later trades for a session.</summary>
    private ContentResult LoginPayload(AccountData account, string? mobile, string? passportId)
    {
        var uid = account.Uid.ToString();
        var token = account.GenerateComboToken();
        var nickname = account.Username;

        var root = new Dictionary<string, object?>
        {
            ["code"] = 1,
            ["msg"] = "操作成功",
            ["responseSuccess"] = true,
            ["uid"] = uid,
            ["userId"] = uid,
            ["token"] = token,
            ["accessToken"] = token,
            ["ksOpenId"] = $"ks_{uid}",
            ["nickname"] = nickname,
            // Game.log reports "Field passportId was not found" / "Field canTouristPay was not
            // found" against the login reply, so the root object has to carry them as well.
            ["passportId"] = passportId ?? uid,
            ["canTouristPay"] = false,
            ["mobile"] = mobile ?? "",
            ["fuzzyMobile"] = mobile ?? "",
            ["email"] = passportId ?? "",
            ["type"] = "guest",
            ["accountType"] = 3,
            ["status"] = 0
        };

        // The SDK parses the account data both from the root object and from "data"; emit the
        // real-name fields in both places so neither parse site reports a missing field.
        var data = new Dictionary<string, object?>
        {
            ["uid"] = uid,
            ["userId"] = uid,
            ["userIdStr"] = uid,
            ["token"] = token,
            ["accessToken"] = token,
            ["sessionId"] = token,
            ["ksOpenId"] = $"ks_{uid}",
            ["openId"] = uid,
            ["passportId"] = passportId ?? uid,
            ["nickname"] = nickname,
            ["type"] = "guest",
            ["status"] = 0,
            ["accountType"] = 3,
            ["accountStatus"] = 0,
            ["mobile"] = mobile ?? "",
            ["fuzzyMobile"] = mobile ?? "",
            ["email"] = passportId ?? "",
            ["isFirstLogin"] = false,
            ["isNeedKoreaSciAuth"] = false,
            ["expireTime"] = 0,
            ["expiresIn"] = 0,
            ["associatedAccounts"] = Array.Empty<string>(),
            ["thirdPartyUid"] = "",
            ["playerFillAgeUrl"] = ""
        };

        foreach (var pair in RealNameBlock())
        {
            root[pair.Key] = pair.Value;
            data[pair.Key] = pair.Value;
        }

        root["data"] = data;
        return Json(root);
    }

    /// <summary>Picks the account identity out of a js2sdk login request.</summary>
    private async Task<ContentResult> LoginResponseForCurrentRequest()
    {
        var passportId = await BodyValue("passportId");
        var mobile = await BodyValue("mobile")
            ?? await BodyValue("phone")
            ?? await BodyValue("account")
            ?? await BodyValue("userName")
            ?? await BodyValue("username")
            ?? await BodyValue("email");
        var password = await BodyValue("password");

        var identity = (string.IsNullOrWhiteSpace(passportId) ? mobile : passportId)?.Trim();
        var account = ResolveLoginAccount(identity, password, out var reason);
        if (account == null)
        {
            Logger.Warn($"Login rejected from {HttpContext.Connection.RemoteIpAddress}: " +
                        $"identity='{identity ?? ""}', reason={reason}");
            return Json(new { code = 255, msg = "登录失败", responseSuccess = false });
        }

        Logger.Info($"Login from {HttpContext.Connection.RemoteIpAddress} -> account " +
                    $"'{account.Username}' (uid={account.Uid}), identity='{identity ?? ""}'.");

        return LoginPayload(account, mobile, passportId);
    }

    /// <summary>js2sdk /v6/config/{appId}: SDK feature config (captcha, login methods).</summary>
    [HttpGet("/v6/config/{appId}")]
    [HttpPost("/v6/config/{appId}")]
    public IActionResult V6Config(string appId) => Json(Ok1(new Dictionary<string, object?>
    {
        // Game.log reports "Field config was not found" plus "Json Value of type 'Null' used as a
        // 'Object'" right after this reply, i.e. the SDK reads data.config and gets null. Every
        // endpoint it could dispatch to afterwards is advertised here so the client keeps moving.
        ["config"] = new Dictionary<string, object?>
        {
            ["appId"] = appId,
            ["channelId"] = "pc_jinshan",
            ["planId"] = "30006",
            ["sdkVersion"] = "5.3.6",
            ["captchaType"] = "none",
            ["closeGeetest"] = true,
            ["enableSms"] = true,
            ["enableAccountLogin"] = true,
            ["enableQrCodeLogin"] = true,
            ["enableRealName"] = false,
            ["loginUrl"] = Js2SdkBase,
            ["serverListUrl"] = Js2SdkBase,
            ["dispatchUrl"] = XgSdkBase,
            ["userInfoUrl"] = XgSdkBase,
            ["payUrl"] = XgSdkBase,
            ["qrCodeUrl"] = XgSdkBase,
            ["giftUrl"] = XgSdkBase,
            ["reportUrl"] = XgSdkReportUrl,
            ["privacyUrl"] = XgSdkBase + "/privacy/tgame/tgame_pc_jinshan_privacy.html",
            ["noticeUrl"] = Js2SdkBase,
            ["versionUrl"] = Js2SdkBase,
            ["webUrl"] = Js2SdkBase
        },
        ["appId"] = appId,
        ["closeGeetest"] = true,
        ["captchaType"] = "none",
        ["enableSms"] = true,
        ["loginType"] = new[] { "channel" },
        ["accountLoginType"] = new[] { "phone", "email" },
        ["qrCodeLogin"] = true,
        ["protocolVersion"] = "1.0",
        ["privacyVersion"] = 1,
        ["userAgreementVersion"] = 1
    }));

    /// <summary>js2sdk /v6/loginCaptcha/{appId}: geetest challenge. Answering success skips it.</summary>
    [HttpGet("/v6/loginCaptcha/{appId}")]
    [HttpPost("/v6/loginCaptcha/{appId}")]
    public IActionResult V6LoginCaptcha(string appId) => Json(new
    {
        code = 1,
        msg = "操作成功",
        fuzzyMobile = (string?)null,
        fuzzyMobileList = (string[]?)null,
        responseSuccess = true
    });

    /// <summary>js2sdk /v6/sendSmsCaptcha/{appId}: SMS code dispatch (accepted, not verified).</summary>
    [HttpGet("/v6/sendSmsCaptcha/{appId}")]
    [HttpPost("/v6/sendSmsCaptcha/{appId}")]
    public IActionResult V6SendSmsCaptcha(string appId) => Json(new
    {
        code = 1,
        msg = "操作成功",
        responseSuccess = true,
        data = new { sendInterval = 60, expiredTime = 600 }
    });

    /// <summary>js2sdk /v6/login/{appId}: account login. Returns uid + token for the game.</summary>
    [HttpGet("/v6/login/{appId}")]
    [HttpPost("/v6/login/{appId}")]
    public Task<ContentResult> V6Login(string appId) => LoginResponseForCurrentRequest();

    /// <summary>SMS-code login (the flow the CN client actually uses on PC).</summary>
    [HttpPost("/v6/loginBySms/{appId}")]
    [HttpGet("/v6/loginBySms/{appId}")]
    public Task<ContentResult> V6LoginBySms(string appId) => LoginResponseForCurrentRequest();

    /// <summary>Sign-out: always accepted.</summary>
    [HttpPost("/v6/logout/{appId}")]
    [HttpGet("/v6/logout/{appId}")]
    public IActionResult V6Logout(string appId) => Json(Ok1(new { appId }));

    /// <summary>js2sdk /v6/player/refresh/{...}: profile refresh after login.</summary>
    [HttpGet("/v6/player/refresh/{**rest}")]
    [HttpPost("/v6/player/refresh/{**rest}")]
    public async Task<IActionResult> V6PlayerRefresh(string? rest)
    {
        var uid = await BodyValue("uid") ?? "0";
        // Report the account's own name: with per-identity accounts a hard-coded nickname would
        // show the same player name for every tester.
        var nickname = int.TryParse(uid, out var parsedUid)
            ? AccountData.GetAccountByUid(parsedUid)?.Username ?? DefaultAccountKey
            : DefaultAccountKey;
        return Json(Ok1(new { uid, userId = uid, nickname, status = 0 }));
    }

    /// <summary>
    /// Catch-all for the remaining js2sdk /v6 endpoints (loginByPassport, realName, qrCode, ...).
    /// Dispatches on the endpoint name so unknown-but-similar calls still get a usable reply.
    /// </summary>
    [HttpGet("/v6/{**rest}")]
    [HttpPost("/v6/{**rest}")]
    public async Task<IActionResult> V6CatchAll(string? rest)
    {
        var name = (rest ?? string.Empty).ToLowerInvariant();

        if (name.Contains("config"))
            return V6Config(name);

        if (name.Contains("captcha"))
            return V6LoginCaptcha(name);

        // Server list / dispatch: the client has to be told to connect to the local game server.
        if (name.Contains("server") || name.Contains("dispatch") || name.Contains("zone"))
            return Json(ServerListPayload());

        if (name.Contains("login") || name.Contains("token") || name.Contains("passport"))
        {
            var response = await LoginResponseForCurrentRequest();
            return response;
        }

        return Json(Ok1(new { path = $"/v6/{rest}" }));
    }

    /// <summary>XGSDK login-by-token handshake (sh-jxsj.xgsdk.com:18443).</summary>
    [HttpGet("/loginByToken")]
    [HttpPost("/loginByToken")]
    public async Task<IActionResult> LoginByToken() => await LoginResponseForCurrentRequest();

    /// <summary>Telemetry sink (xgdata.xgsdk.com/bisdk, sh-jxsj:31443/data/report).</summary>
    [HttpPost("/data/report")]
    [HttpGet("/data/report")]
    public IActionResult DataReport() => Json(new { code = 0, msg = "ok" });

    /// <summary>QR account/pay endpoints: answered permissively so the client does not stall.</summary>
    [HttpGet("/account/qrcode/{action}")]
    [HttpPost("/account/qrcode/{action}")]
    public IActionResult AccountQrCode(string action) => Json(new
    {
        code = 1,
        msg = "操作成功",
        responseSuccess = true,
        data = new { status = 1, qrCode = "", qrCodeUrl = "", token = "", expireTime = 600, action }
    });

    [HttpGet("/pay/qrcode/{action}")]
    [HttpPost("/pay/qrcode/{action}")]
    public IActionResult PayQrCode(string action) => Json(new
    {
        code = 1,
        msg = "操作成功",
        responseSuccess = true,
        data = new { status = 1, qrCode = "", action }
    });

    /// <summary>Gift-code exchange (activity.xgsdk.com:18443/push/gift/exchange).</summary>
    [HttpGet("/push/gift/exchange")]
    [HttpPost("/push/gift/exchange")]
    public IActionResult GiftExchange() => Json(new
    {
        code = 0,
        msg = "兑换成功",
        responseSuccess = true,
        data = new { }
    });

    /// <summary>Privacy/agreement pages opened inside the SDK's CEF browser.</summary>
    [HttpGet("/privacy/{**rest}")]
    public IActionResult PrivacyPage(string? rest)
    {
        const string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>MikuSB</title></head>"
            + "<body><p>MikuSB local emulation.</p></body></html>";
        return Content(html, "text/html; charset=utf-8");
    }
}
