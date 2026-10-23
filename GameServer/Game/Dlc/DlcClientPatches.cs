using System.Globalization;

namespace MikuSB.GameServer.Game.Dlc;

/// <summary>
/// Client-side Lua patches shipped over the existing GM script channel
/// (CallGSRouter.SendScript(connection, "GM", {"code": ...}) -> Misc/GM.lua's s2c.Register('GM')).
///
/// Why this exists: several DLC sub features gate themselves on local CSV time tables that have been
/// expired since the real event ended, and the s2c callbacks that would refresh them are either dead
/// code or never sent by this server. Rewriting the client tables is the only way to open them
/// without touching the game package.
///
/// HARD RULE for every payload here: no '+', no '&lt;', no '&gt;', no '&amp;', no apostrophes, no double
/// quotes and no non-ASCII. System.Text.Json escapes those to \uXXXX, and the client's
/// Misc/Json.lua:387 decodes string values with load("return \"...\""), which Lua 5.1 cannot parse -
/// the whole payload then dies with "attempt to call a nil value". Use "now - off" with a negative
/// offset instead of "now + off".
/// </summary>
public static class DlcClientPatches
{
    // ---------------------------------------------------------------------------------------------
    // LISTEN-HOST PATCH LINE - FROZEN 2026-10-09
    //
    // Every constant below whose name starts with ZBugHost is the accumulated workaround for one
    // root disease: on a listen host the game skips every branch written as "if not HasAuthority()
    // or IsStandalone" (client-only presentation and local HUD init) and the Lua-side OnRep_*
    // handlers never fire for the host own player. This is the frozen register.
    //
    //   pushed name                 const line role
    //   ---------------------------------------------------------------------------------------------------
    //   ZBugHostFix                 :792       (v1) first host probes and init attempts
    //   ZBugHostInitFix             :1518      (v5) pawn-level CheckIsLocallyControlled, CheckInitFinish pump (camera,
    //                                          mesh, UI init) and one-shot HUD replay. Superseded in behaviour,
    //                                          but its CheckInitFinish part has no replacement - keep pushing.
    //   ZBugHostPlayFix             :1762      (v6) PartyBtnPress bridge (fire half dead, superseded) plus HUD repop
    //                                          and NotifyLocalPlayer* - keep for the HUD half
    //   ZBugHostViewFix             :1947      (v7) IsStandalone opening for the client-visual branches
    //   ZBugHostHudFix              :2099      (v8) periodic HUD value push (self healing)
    //   ZBugHostScopeFix            :2269      (v9) restricts the above to the host and restores the real IsStandalone
    //                                          value on the guest (fixed a double buff bug there)
    //   ZBugHostBuffFix             :2455      (v10) buff list prompt replay
    //   ZBugHostLocalFix            :2700      (v11) fixes the v6 helper that called nonexistent bare IsServer globals
    //   ZBugHostWeaponFix           :2930      (v12) weapon list and ammo HUD plus item and skill notify
    //   ZBugHostFireFix             :3271      (v13) first fire bridge; taught that PartyBtnPress passes 1/0 and not
    //                                          true/false. Dead code, superseded.
    //   ZBugHostAssetFix            :3465      (v14) replicates the client-side PreLoadGameAssetByConfig calls and
    //                                          PreloadOtherAsset that the host skips because
    //                                          BP_WPZBugRunMgr:ReceiveBeginPlay gates them on authority
    //   ZBugHostFinalFix            :4329      (v18) ONE payload on the wire = ClarityFix + CoreFix + ShotFix below
    //   ZBugHostClarityFix          :3613      (v15) reborn mesh restore and DamageOther gate for the guest
    //   ZBugHostCoreFix             :3845      (v16) identity based CheckIsLocallyControlled on four class tables,
    //                                          IsStandalone sweep, unbounded 0.5s HUD repop stage, countdown
    //                                          event 104 replay and the Server_ShootEntityBullet fire pump
    //   ZBugHostShotFix             :4245      (v17) K2_GetActorLocation fix for the pump muzzle and aim
    //
    // Two landmines left in place on purpose (documented, not fixed - this line is frozen):
    //   * ZBugHostFireFix (v13) still calls c:GetActorLocation(), a name that does not exist in
    //     this game Lua bindings (UnLua exposes K2_GetActorLocation; the shipped Script tree uses
    //     it 957 times and the bare name zero times). Its event handler is still registered in any
    //     client that ran it, but it only writes its own MikuFXFire flag, so it cannot disturb
    //     v16/v17. Do not copy its code.
    //   * ZBugHostCoreFix (v16) MikuCVCilc returns the engine value first, so AI monsters on the
    //     host (IsLocallyControlled() is true for them on a listen server) still pass the
    //     local-player gate. The ZBugHostClarityFix DamageOther wrapper masks the visible symptom.
    //     The proper fix is to return false for AGEGameMonster on the host - see 035, deliberately
    //     not done here.
    // ---------------------------------------------------------------------------------------------
    /// <summary>
    /// 水城观光 (DLC mission board). AchievementDLC.LoadTimeConf() sets nMaxLoadId to the newest group
    /// whose window ended more than 50 days ago; with every DLC35 group long expired that is 152, and
    /// LoadConf() then drops every row because of its "nPanelId &gt; nMaxLoadId" filter, leaving
    /// tbGroupConfig empty and the board blank. Reload with nMaxLoadId pinned to 0 and re-stamp the
    /// group and mission windows onto the running clock.
    /// </summary>
    public const string DlcMissionBoard = """
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        AchievementDLC.LoadTimeConf() AchievementDLC.nMaxLoadId = 0 AchievementDLC.LoadConf()
        for _, t in pairs(AchievementDLC.tbTiemConf) do t.nStartTime = now - off1 t.nEndTime = now - off2 end
        for _, cfg in pairs(AchievementDLC.tbConfig) do cfg.nStartTime = math.min(cfg.nStartTime or 0, now - off1) cfg.nEndTime = math.max(cfg.nEndTime or 0, now - off2) end
        """;

    /// <summary>
    /// 崇福集市. Shop.lua:651 indexes the numeric-keyed ShopLogic.tbShopTabConf with the JSON object
    /// key it just decoded, which is always a string, so the ShopLogic_GetOpenTime push is dead code
    /// in this build. Install a normalising __index and stamp the DLC tabs onto the running clock.
    /// </summary>
    public const string DlcShopWindows = """
        local c = DLC_Logic.GetCurConf() or {}
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        local t = ShopLogic.tbShopTabConf
        setmetatable(t, { __index = function(tt, k) return rawget(tt, tonumber(k)) end })
        for _, id in ipairs(c.tbShopId or {}) do local info = t[id] info.nBegin = now - off1 info.nEnd = now - off2 end
        """;

    // >>> SUPPLY SHOP PAYLOAD (generated by scripts/build_supply_shop.py) >>>
    // Source data: Shop-Universal/RawAssets/Game/Content/Settings/shop/test_shop_tab.txt
    // and test_goods.txt. The client merges these rows into ShopLogic at login, so this
    // ships over the MikuSB socket with no pak. Do not edit by hand: rerun
    // `python scripts/build_supply_shop.py` instead.
    public const string SupplyShop = """
        -- Supply shop: merges the test server flat-rate shop tables into ShopLogic at login.
        -- The two csv blobs below are verbatim copies of Settings/shop/test_shop_tab.txt and
        -- Settings/shop/test_goods.txt, so the ShopLogic loaders do every bit of field parsing
        -- and this patch cannot drift from the client column set.
        if not ShopLogic then return end
        if ShopLogic.bSupplyShopPatched then return end
        ShopLogic.bSupplyShopPatched = true

        local NL = string.char(10)
        local CR = string.char(13)
        local TAB = string.char(9)
        local EMPTY = [[]]
        local GOODS_PATH = [[shop/test_goods.txt]]
        local TABS_PATH = [[shop/test_shop_tab.txt]]

        local GOODS_CSV = [[
        xxx_3885127109	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A
        ShopId	GoodsId	GDPLN	PosIndex	Weight	Class	Price1	LimitType	LimitNum	DiscountType	Discount	ShowSupportHead
        61	3101	{3,1,13,1,1}	1	10	ordinary	{3,1}
        61	3102	{3,2,13,1,1}	2	10	ordinary	{3,1}
        61	3103	{3,3,13,1,1}	3	10	ordinary	{3,1}
        61	3104	{3,1,14,1,1}	4	10	ordinary	{3,1}
        61	3105	{3,2,14,1,1}	5	10	ordinary	{3,1}
        61	3106	{3,3,14,1,1}	6	10	ordinary	{3,1}
        61	3107	{3,1,15,1,1}	7	10	ordinary	{3,1}
        61	3108	{3,2,15,1,1}	8	10	ordinary	{3,1}
        61	3109	{3,3,15,1,1}	9	10	ordinary	{3,1}
        61	3110	{3,1,16,1,1}	10	10	ordinary	{3,1}
        61	3111	{3,2,16,1,1}	11	10	ordinary	{3,1}
        61	3112	{3,3,16,1,1}	12	10	ordinary	{3,1}
        61	3113	{3,1,22,1,1}	13	10	ordinary	{3,1}
        61	3114	{3,2,22,1,1}	14	10	ordinary	{3,1}
        61	3115	{3,3,22,1,1}	15	10	ordinary	{3,1}
        61	3116	{3,1,24,1,1}	16	10	ordinary	{3,1}
        61	3117	{3,2,24,1,1}	17	10	ordinary	{3,1}
        61	3118	{3,3,24,1,1}	18	10	ordinary	{3,1}
        61	3119	{3,1,25,1,1}	19	10	ordinary	{3,1}
        61	3120	{3,2,25,1,1}	20	10	ordinary	{3,1}
        61	3121	{3,3,25,1,1}	21	10	ordinary	{3,1}
        61	3122	{3,1,27,1,1}	22	10	ordinary	{3,1}
        61	3123	{3,2,27,1,1}	23	10	ordinary	{3,1}
        61	3124	{3,3,27,1,1}	24	10	ordinary	{3,1}
        61	3125	{3,1,17,1,1}	25	10	ordinary	{3,1}
        61	3126	{3,2,17,1,1}	26	10	ordinary	{3,1}
        61	3127	{3,3,17,1,1}	27	10	ordinary	{3,1}
        61	3128	{3,1,23,1,1}	28	10	ordinary	{3,1}
        61	3129	{3,2,23,1,1}	29	10	ordinary	{3,1}
        61	3130	{3,3,23,1,1}	30	10	ordinary	{3,1}
        61	3131	{3,1,26,1,1}	31	10	ordinary	{3,1}
        61	3132	{3,2,26,1,1}	32	10	ordinary	{3,1}
        61	3133	{3,3,26,1,1}	33	10	ordinary	{3,1}
        64	3501	{3,1,28,1,1}	67	10	ordinary	{3,1}					1
        64	3502	{3,2,28,1,1}	68	10	ordinary	{3,1}					1
        64	3503	{3,3,28,1,1}	69	10	ordinary	{3,1}					1
        64	3504	{3,1,29,1,1}	70	10	ordinary	{3,1}					1
        64	3505	{3,2,29,1,1}	71	10	ordinary	{3,1}					1
        64	3506	{3,3,29,1,1}	72	10	ordinary	{3,1}					1
        64	3507	{3,1,30,1,1}	73	10	ordinary	{3,1}					1
        64	3508	{3,2,30,1,1}	74	10	ordinary	{3,1}					1
        64	3509	{3,3,30,1,1}	75	10	ordinary	{3,1}					1
        64	3510	{3,1,31,1,1}	76	10	ordinary	{3,1}					1
        64	3511	{3,2,31,1,1}	77	10	ordinary	{3,1}					1
        64	3512	{3,3,31,1,1}	78	10	ordinary	{3,1}					1
        64	3513	{3,1,32,1,1}	79	10	ordinary	{3,1}					1
        64	3514	{3,2,32,1,1}	80	10	ordinary	{3,1}					1
        64	3515	{3,3,32,1,1}	81	10	ordinary	{3,1}					1
        64	3516	{3,1,33,1,1}	82	10	ordinary	{3,1}					1
        64	3517	{3,2,33,1,1}	83	10	ordinary	{3,1}					1
        64	3518	{3,3,33,1,1}	84	10	ordinary	{3,1}					1
        64	3519	{3,1,34,1,1}	85	10	ordinary	{3,1}					1
        64	3520	{3,2,34,1,1}	86	10	ordinary	{3,1}					1
        64	3521	{3,3,34,1,1}	87	10	ordinary	{3,1}					1
        64	3522	{3,1,35,1,1}	88	10	ordinary	{3,1}					1
        64	3523	{3,2,35,1,1}	89	10	ordinary	{3,1}					1
        64	3524	{3,3,35,1,1}	90	10	ordinary	{3,1}					1
        64	3525	{3,1,36,1,1}	91	10	ordinary	{3,1}					1
        64	3526	{3,2,36,1,1}	92	10	ordinary	{3,1}					1
        64	3527	{3,3,36,1,1}	93	10	ordinary	{3,1}					1
        64	3528	{3,1,37,1,1}	58	10	ordinary	{3,1}					1
        64	3529	{3,2,37,1,1}	59	10	ordinary	{3,1}					1
        64	3530	{3,3,37,1,1}	60	10	ordinary	{3,1}					1
        64	3531	{3,1,38,1,1}	61	10	ordinary	{3,1}					1
        64	3532	{3,2,38,1,1}	62	10	ordinary	{3,1}					1
        64	3533	{3,3,38,1,1}	63	10	ordinary	{3,1}					1
        64	3534	{3,1,39,1,1}	64	10	ordinary	{3,1}					1
        64	3535	{3,2,39,1,1}	65	10	ordinary	{3,1}					1
        64	3536	{3,3,39,1,1}	66	10	ordinary	{3,1}					1
        64	3537	{3,1,41,1,1}	52	10	ordinary	{3,1}					1
        64	3538	{3,2,41,1,1}	53	10	ordinary	{3,1}					1
        64	3539	{3,3,41,1,1}	54	10	ordinary	{3,1}					1
        64	3540	{3,1,40,1,1}	55	10	ordinary	{3,1}					1
        64	3541	{3,2,40,1,1}	56	10	ordinary	{3,1}					1
        64	3542	{3,3,40,1,1}	57	10	ordinary	{3,1}					1
        64	3543	{3,1,42,1,1}	49	10	ordinary	{3,1}					1
        64	3544	{3,2,42,1,1}	50	10	ordinary	{3,1}					1
        64	3545	{3,3,42,1,1}	51	10	ordinary	{3,1}					1
        64	3546	{3,1,43,1,1}	46	10	ordinary	{3,1}					1
        64	3547	{3,2,43,1,1}	47	10	ordinary	{3,1}					1
        64	3548	{3,3,43,1,1}	48	10	ordinary	{3,1}					1
        64	3549	{3,1,44,1,1}	40	10	ordinary	{3,1}					1
        64	3550	{3,2,44,1,1}	41	10	ordinary	{3,1}					1
        64	3551	{3,3,44,1,1}	42	10	ordinary	{3,1}					1
        64	3552	{3,1,45,1,1}	43	10	ordinary	{3,1}					1
        64	3553	{3,2,45,1,1}	44	10	ordinary	{3,1}					1
        64	3554	{3,3,45,1,1}	45	10	ordinary	{3,1}					1
        64	3555	{3,1,46,1,1}	37	10	ordinary	{3,1}					1
        64	3556	{3,2,46,1,1}	38	10	ordinary	{3,1}					1
        64	3557	{3,3,46,1,1}	39	10	ordinary	{3,1}					1
        64	3558	{3,1,47,1,1}	34	10	ordinary	{3,1}					1
        64	3559	{3,2,47,1,1}	35	10	ordinary	{3,1}					1
        64	3560	{3,3,47,1,1}	36	10	ordinary	{3,1}					1
        64	3561	{3,1,48,1,1}	31	10	ordinary	{3,1}					1
        64	3562	{3,2,48,1,1}	32	10	ordinary	{3,1}					1
        64	3563	{3,3,48,1,1}	33	10	ordinary	{3,1}					1
        64	3564	{3,1,49,1,1}	28	10	ordinary	{3,1}					1
        64	3565	{3,2,49,1,1}	29	10	ordinary	{3,1}					1
        64	3566	{3,3,49,1,1}	30	10	ordinary	{3,1}					1
        64	3567	{3,1,50,1,1}	22	10	ordinary	{3,1}					1
        64	3568	{3,2,50,1,1}	23	10	ordinary	{3,1}					1
        64	3569	{3,3,50,1,1}	24	10	ordinary	{3,1}					1
        64	3570	{3,1,51,1,1}	25	10	ordinary	{3,1}					1
        64	3571	{3,2,51,1,1}	26	10	ordinary	{3,1}					1
        64	3572	{3,3,51,1,1}	27	10	ordinary	{3,1}					1
        64	3573	{3,1,52,1,1}	19	10	ordinary	{3,1}					1
        64	3574	{3,2,52,1,1}	20	10	ordinary	{3,1}					1
        64	3575	{3,3,52,1,1}	21	10	ordinary	{3,1}					1
        64	3576	{3,1,53,1,1}	13	10	ordinary	{3,1}					1
        64	3577	{3,2,53,1,1}	14	10	ordinary	{3,1}					1
        64	3578	{3,3,53,1,1}	15	10	ordinary	{3,1}					1
        64	3579	{3,1,54,1,1}	16	10	ordinary	{3,1}					1
        64	3580	{3,2,54,1,1}	17	10	ordinary	{3,1}					1
        64	3581	{3,3,54,1,1}	18	10	ordinary	{3,1}					1
        64	3582	{3,1,55,1,1}	7	10	ordinary	{3,1}					1
        64	3583	{3,2,55,1,1}	8	10	ordinary	{3,1}					1
        64	3584	{3,3,55,1,1}	9	10	ordinary	{3,1}					1
        64	3585	{3,1,56,1,1}	10	10	ordinary	{3,1}					1
        64	3586	{3,2,56,1,1}	11	10	ordinary	{3,1}					1
        64	3587	{3,3,56,1,1}	12	10	ordinary	{3,1}					1
        64	3588	{3,1,57,1,1}	1	10	ordinary	{3,1}					1
        64	3589	{3,2,57,1,1}	2	10	ordinary	{3,1}					1
        64	3590	{3,3,57,1,1}	3	10	ordinary	{3,1}					1
        64	3591	{3,1,58,1,1}	4	10	ordinary	{3,1}					1
        64	3592	{3,2,58,1,1}	5	10	ordinary	{3,1}					1
        64	3593	{3,3,58,1,1}	6	10	ordinary	{3,1}					1
        62	3201	{5,4,5,2,1}	26	10	ordinary	{3,1}
        62	3202	{5,4,2,2,1}	27	10	ordinary	{3,1}
        62	3203	{5,4,4,2,1}	28	10	ordinary	{3,1}
        62	3204	{5,4,10,2,1}	29	10	ordinary	{3,1}
        62	3205	{5,4,1,2,1}	30	10	ordinary	{3,1}
        62	3206	{5,4,8,2,1}	31	10	ordinary	{3,1}
        62	3207	{5,4,14,2,1}	32	10	ordinary	{3,1}
        62	3208	{5,4,6,2,1}	33	10	ordinary	{3,1}
        62	3209	{5,4,12,2,1}	34	10	ordinary	{3,1}
        62	3210	{5,4,17,1,1}	35	10	ordinary	{3,1}
        62	3211	{5,4,15,1,1}	36	10	ordinary	{3,1}
        62	3212	{5,4,16,1,1}	37	10	ordinary	{3,1}
        62	3213	{5,4,7,2,1}	38	10	ordinary	{3,1}
        62	3214	{5,4,11,2,1}	39	10	ordinary	{3,1}
        62	3215	{5,4,13,2,1}	40	10	ordinary	{3,1}
        62	3216	{5,4,2,3,1}	24	10	ordinary	{3,1}
        62	3217	{5,4,8,3,1}	25	10	ordinary	{3,1}
        62	3218	{5,4,18,1,1}	23	10	ordinary	{3,1}
        62	3219	{5,4,15,2,1}	21	10	ordinary	{3,1}
        62	3220	{5,4,5,3,1}	22	10	ordinary	{3,1}
        62	3221	{5,4,4,3,1}	20	10	ordinary	{3,1}
        62	3222	{5,4,20,1,1}	19	10	ordinary	{3,1}
        62	3223	{5,4,14,3,1}	17	10	ordinary	{3,1}
        62	3224	{5,4,10,3,1}	18	10	ordinary	{3,1}
        62	3225	{5,4,11,3,1}	16	10	ordinary	{3,1}
        62	3226	{5,4,21,1,1}	15	10	ordinary	{3,1}
        62	3227	{1,3,2,1,1}	13	10	ordinary	{3,1}
        62	3228	{5,32,1,1,1}	14	10	ordinary	{3,1}
        62	3229	{5,4,18,2,1}	12	10	ordinary	{3,1}
        62	3230	{5,4,15,3,1}	10	10	ordinary	{3,1}
        62	3231	{5,4,17,2,1}	11	10	ordinary	{3,1}
        62	3232	{5,4,22,1,1}	9	10	ordinary	{3,1}
        62	3233	{5,4,8,4,1}	7	10	ordinary	{3,1}
        62	3234	{5,4,13,3,1}	8	10	ordinary	{3,1}
        62	3235	{5,4,23,1,1}	4	10	ordinary	{3,1}
        62	3236	{1,6,3,1,1}	5	10	ordinary	{3,1}
        62	3237	{5,32,2,1,1}	6	10	ordinary	{3,1}
        62	3238	{5,4,20,2,1}	1	10	ordinary	{3,1}
        62	3239	{1,12,3,1,1}	2	10	ordinary	{3,1}
        62	3240	{5,32,3,1,1}	3	10	ordinary	{3,1}
        62	3241	{5,4,1,1,1}	100	10	ordinary	{3,1}
        62	3242	{5,4,2,1,1}	101	10	ordinary	{3,1}
        62	3243	{5,4,3,1,1}	102	10	ordinary	{3,1}
        62	3244	{5,4,4,1,1}	103	10	ordinary	{3,1}
        62	3245	{5,4,5,1,1}	104	10	ordinary	{3,1}
        62	3246	{5,4,6,1,1}	105	10	ordinary	{3,1}
        62	3247	{5,4,7,1,1}	106	10	ordinary	{3,1}
        62	3248	{5,4,8,1,1}	107	10	ordinary	{3,1}
        62	3249	{5,4,10,1,1}	108	10	ordinary	{3,1}
        62	3250	{5,4,11,1,1}	109	10	ordinary	{3,1}
        62	3251	{5,4,12,1,1}	110	10	ordinary	{3,1}
        62	3252	{5,4,14,1,1}	111	10	ordinary	{3,1}
        62	3253	{5,4,13,1,1}	112	10	ordinary	{3,1}
        60	3001	{4,3,1,1,3000}	2		ordinary	{3,1}
        60	3002	{5,1,1,4,1}	3		ordinary	{3,1}
        60	3003	{5,2,1,4,1}	4		ordinary	{3,1}
        60	3004	{5,3,1,4,1}	5		ordinary	{3,1}
        60	3005	{5,5,1,1,1}	6		ordinary	{3,1}
        60	3006	{5,5,1,2,1}	7		ordinary	{3,1}
        60	3007	{5,5,1,3,1}	8		ordinary	{3,1}
        60	3008	{5,5,1,4,1}	9		ordinary	{3,1}
        60	3009	{5,9,1,2,1}	10		ordinary	{3,1}
        60	3010	{5,9,1,3,1}	11		ordinary	{3,1}
        60	3011	{5,6,1,2,1}	12		ordinary	{3,1}
        60	3012	{5,6,2,1,1}	13		ordinary	{3,1}
        60	3013	{25,20,2,2}	1		ordinary	{1,50}	5	1	1	{{1,60}}
        63	3301	{2,1,17,1,1}	23		ordinary	{3,1}
        63	3302	{2,1,18,1,1}	24		ordinary	{3,1}
        63	3303	{2,2,4,1,1}	25		ordinary	{3,1}
        63	3304	{2,2,17,1,1}	26		ordinary	{3,1}
        63	3305	{2,2,19,1,1}	27		ordinary	{3,1}
        63	3306	{2,3,17,1,1}	28		ordinary	{3,1}
        63	3307	{2,3,4,1,1}	29		ordinary	{3,1}
        63	3308	{2,4,3,1,1}	30		ordinary	{3,1}
        63	3309	{2,5,3,1,1}	31		ordinary	{3,1}
        63	3310	{2,5,19,1,1}	32		ordinary	{3,1}
        63	3311	{2,4,18,1,1}	33		ordinary	{3,1}
        63	3312	{2,5,22,1,1}	34		ordinary	{3,1}
        63	3313	{2,4,20,1,1}	35		ordinary	{3,1}
        63	3314	{2,1,25,1,1}	36		ordinary	{3,1}
        63	3315	{2,2,22,1,1}	37		ordinary	{3,1}
        63	3316	{2,6,4,1,1}	38		ordinary	{3,1}
        63	3317	{2,3,24,1,1}	39		ordinary	{3,1}
        63	3318	{2,1,27,1,1}	40		ordinary	{3,1}
        63	3319	{2,4,26,1,1}	41		ordinary	{3,1}
        63	3320	{2,3,26,1,1}	42		ordinary	{3,1}
        63	3321	{2,7,3,1,1}	21		ordinary	{3,1}
        63	3322	{2,5,24,1,1}	22		ordinary	{3,1}
        63	3323	{2,4,28,1,1}	20		ordinary	{3,1}
        63	3324	{2,5,26,1,1}	18		ordinary	{3,1}
        63	3325	{2,3,28,1,1}	19		ordinary	{3,1}
        63	3326	{2,2,24,1,1}	17		ordinary	{3,1}
        63	3327	{2,4,30,1,1}	16		ordinary	{3,1}
        63	3328	{2,8,3,1,1}	14		ordinary	{3,1}
        63	3329	{2,1,29,1,1}	15		ordinary	{3,1}
        63	3330	{2,3,30,1,1}	13		ordinary	{3,1}
        63	3331	{2,5,28,1,1}	12		ordinary	{3,1}
        63	3332	{2,3,31,1,1}	11		ordinary	{3,1}
        63	3333	{2,2,26,1,1}	10		ordinary	{3,1}
        63	3334	{2,9,3,1,1}	8		ordinary	{3,1}
        63	3335	{2,5,30,1,1}	9		ordinary	{3,1}
        63	3336	{2,1,31,1,1}	7		ordinary	{3,1}
        63	3337	{2,1,33,1,1}	5		ordinary	{3,1}
        63	3338	{2,4,32,1,1}	6		ordinary	{3,1}
        63	3339	{2,3,33,1,1}	3		ordinary	{3,1}
        63	3340	{2,5,31,1,1}	4		ordinary	{3,1}
        63	3341	{2,7,5,1,1}	1		ordinary	{3,1}
        63	3342	{2,2,27,1,1}	2		ordinary	{3,1}
        63	3343	{2,1,3,1,1}	120		ordinary	{3,1}
        63	3344	{2,1,5,1,1}	121		ordinary	{3,1}
        63	3345	{2,1,9,1,1}	122		ordinary	{3,1}
        63	3346	{2,1,10,1,1}	123		ordinary	{3,1}
        63	3347	{2,1,11,1,1}	124		ordinary	{3,1}
        63	3348	{2,1,12,1,1}	125		ordinary	{3,1}
        63	3349	{2,1,13,1,1}	126		ordinary	{3,1}
        63	3350	{2,1,14,1,1}	127		ordinary	{3,1}
        63	3351	{2,1,21,1,1}	128		ordinary	{3,1}
        63	3352	{2,1,22,1,1}	129		ordinary	{3,1}
        63	3353	{2,1,23,1,1}	130		ordinary	{3,1}
        63	3354	{2,1,24,1,1}	131		ordinary	{3,1}
        63	3355	{2,1,26,1,1}	132		ordinary	{3,1}
        63	3356	{2,2,3,1,1}	133		ordinary	{3,1}
        63	3357	{2,2,5,1,1}	134		ordinary	{3,1}
        63	3358	{2,2,6,1,1}	135		ordinary	{3,1}
        63	3359	{2,2,8,1,1}	136		ordinary	{3,1}
        63	3360	{2,2,12,1,1}	137		ordinary	{3,1}
        63	3361	{2,2,13,1,1}	138		ordinary	{3,1}
        63	3362	{2,2,15,1,1}	139		ordinary	{3,1}
        63	3363	{2,2,16,1,1}	140		ordinary	{3,1}
        63	3364	{2,2,21,1,1}	141		ordinary	{3,1}
        63	3365	{2,3,3,1,1}	142		ordinary	{3,1}
        63	3366	{2,3,5,1,1}	143		ordinary	{3,1}
        63	3367	{2,3,9,1,1}	144		ordinary	{3,1}
        63	3368	{2,3,11,1,1}	145		ordinary	{3,1}
        63	3369	{2,3,12,1,1}	146		ordinary	{3,1}
        63	3370	{2,3,13,1,1}	147		ordinary	{3,1}
        63	3371	{2,3,14,1,1}	148		ordinary	{3,1}
        63	3372	{2,3,15,1,1}	149		ordinary	{3,1}
        63	3373	{2,3,21,1,1}	150		ordinary	{3,1}
        63	3374	{2,3,22,1,1}	151		ordinary	{3,1}
        63	3375	{2,3,23,1,1}	152		ordinary	{3,1}
        63	3376	{2,3,25,1,1}	153		ordinary	{3,1}
        63	3377	{2,4,2,1,1}	154		ordinary	{3,1}
        63	3378	{2,4,4,1,1}	155		ordinary	{3,1}
        63	3379	{2,4,9,1,1}	156		ordinary	{3,1}
        63	3380	{2,4,10,1,1}	157		ordinary	{3,1}
        63	3381	{2,4,11,1,1}	158		ordinary	{3,1}
        63	3382	{2,4,12,1,1}	159		ordinary	{3,1}
        63	3383	{2,4,15,1,1}	160		ordinary	{3,1}
        63	3384	{2,4,16,1,1}	161		ordinary	{3,1}
        63	3385	{2,4,21,1,1}	162		ordinary	{3,1}
        63	3386	{2,4,22,1,1}	163		ordinary	{3,1}
        63	3387	{2,4,23,1,1}	164		ordinary	{3,1}
        63	3388	{2,4,25,1,1}	165		ordinary	{3,1}
        63	3389	{2,5,2,1,1}	166		ordinary	{3,1}
        63	3390	{2,5,6,1,1}	167		ordinary	{3,1}
        63	3391	{2,5,7,1,1}	168		ordinary	{3,1}
        63	3392	{2,5,10,1,1}	169		ordinary	{3,1}
        63	3393	{2,5,11,1,1}	170		ordinary	{3,1}
        63	3394	{2,5,12,1,1}	171		ordinary	{3,1}
        63	3395	{2,5,15,1,1}	172		ordinary	{3,1}
        63	3396	{2,5,16,1,1}	173		ordinary	{3,1}
        63	3397	{2,5,21,1,1}	174		ordinary	{3,1}
        63	3398	{2,6,2,1,1}	175		ordinary	{3,1}
        63	3399	{2,7,2,1,1}	118		ordinary	{3,1}
        63	3400	{2,5,23,1,1}	119		ordinary	{3,1}
        63	3401	{2,4,27,1,1}	117		ordinary	{3,1}
        63	3402	{2,5,25,1,1}	115		ordinary	{3,1}
        63	3403	{2,3,27,1,1}	116		ordinary	{3,1}
        63	3404	{2,2,23,1,1}	114		ordinary	{3,1}
        63	3405	{2,4,29,1,1}	113		ordinary	{3,1}
        63	3406	{2,8,2,1,1}	111		ordinary	{3,1}
        63	3407	{2,1,28,1,1}	112		ordinary	{3,1}
        63	3408	{2,3,29,1,1}	110		ordinary	{3,1}
        63	3409	{2,5,27,1,1}	109		ordinary	{3,1}
        63	3410	{2,2,25,1,1}	108		ordinary	{3,1}
        63	3411	{2,9,2,1,1}	106		ordinary	{3,1}
        63	3412	{2,5,29,1,1}	107		ordinary	{3,1}
        63	3413	{2,1,30,1,1}	105		ordinary	{3,1}
        63	3414	{2,1,32,1,1}	103		ordinary	{3,1}
        63	3415	{2,4,31,1,1}	104		ordinary	{3,1}
        63	3416	{2,3,32,1,1}	102		ordinary	{3,1}
        63	3417	{2,7,4,1,1}	101		ordinary	{3,1}
        ]]
        local TABS_CSV = [[
        xxx_454630312	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A	A
        ShopId	GroupId	GroupName	Name	GroupIcon	ShopIcon	UnselectIcon	Sort	OnOff	WidgetType	Begin	End	Columns	ShopMoneyType	Version	ListVersion	Label	CashLimit
        60	60	shop.test_shop.GroupName1	shop.shop21.Name	1701128	1701129	1701156	2	1	1	[202109010000]	[209909191400]	100	{3,1}	2	0	1	1
        61	60	shop.test_shop.GroupName1	shop.shop12.Name	1701128	1701129	1701156	3	1	1	[202109010000]	[209909191400]	100	{3}	2	0	1
        62	60	shop.test_shop.GroupName1	shop.shop7.Name	1701128	1701129	1701153	5	1	1	[202109010000]	[209909191400]	100	{3}	2	0	1	1
        63	60	shop.test_shop.GroupName1	shop.shop22.Name	1701128	1701129	1701153	6	1	1	[202109010000]	[209909191400]	100	{3}	2	0	1	1
        64	60	shop.test_shop.GroupName1	shop.shop24.Name	1701128	1701129	1701156	4	1	1	[202109010000]	[209909191400]	100	{3}	2	0	1
        ]]

        -- Same contract as Lib.lua LoadCsv: a list of rows keyed by the header names, with empty
        -- cells left nil. The only difference is that the text comes from the blob above rather
        -- than from LoadSetting, which keeps the pak out of the picture.
        local function ParseCsv(sContent, nHeader)
            sContent = string.gsub(sContent, CR, EMPTY)
            nHeader = nHeader - (-1)
            local tbLine = Split(sContent, NL)
            if tbLine[nHeader] == nil then return {} end
            local tbKey = Split(tbLine[nHeader], TAB)
            local tbData = {}
            for i = nHeader - (-1), #tbLine do
                local sLine = tbLine[i]
                if string.find(sLine, [[%S]]) then
                    local tbVal = Split(sLine, TAB)
                    local tbAtt = {}
                    for nCol, sVal in ipairs(tbVal) do
                        if tbKey[nCol] ~= nil and #sVal ~= 0 then tbAtt[tbKey[nCol]] = sVal end
                    end
                    if tbAtt.ShopId ~= nil then tbData[#tbData - (-1)] = tbAtt end
                end
            end
            return tbData
        end

        local oldGoods = ShopLogic.LoadGoodsConf
        local oldTabs = ShopLogic.LoadShopTabConf

        local function MergeSupplyShop()
            local wantGoods = ShopLogic.tbGoodsConf
            local wantTabs = ShopLogic.tbShopTabConf
            local wantGroups = ShopLogic.tbGroupId
            if wantGoods == nil or wantTabs == nil or wantGroups == nil then return end

            local keepCsv = LoadCsv
            local keepIsTest = IsTestServer
            LoadCsv = function(sPath, nHeader)
                if sPath == GOODS_PATH then return ParseCsv(GOODS_CSV, nHeader) end
                if sPath == TABS_PATH then return ParseCsv(TABS_CSV, nHeader) end
                return keepCsv(sPath, nHeader)
            end
            IsTestServer = function() return true end

            local okGoods = pcall(oldGoods)
            local supGoods = ShopLogic.tbGoodsConf
            local okTabs = pcall(oldTabs)
            local supTabs = ShopLogic.tbShopTabConf
            local supGroups = ShopLogic.tbGroupId

            IsTestServer = keepIsTest
            LoadCsv = keepCsv
            ShopLogic.tbGoodsConf = wantGoods
            ShopLogic.tbShopTabConf = wantTabs
            ShopLogic.tbGroupId = wantGroups

            if not okGoods or not okTabs then return end
            if supGoods == nil or supTabs == nil then return end

            local nGoods, nTabs = 0, 0
            for nId, tbInfo in pairs(supGoods) do
                if wantGoods[nId] == nil then wantGoods[nId] = tbInfo nGoods = nGoods - (-1) end
            end
            for nId, tbInfo in pairs(supTabs) do
                if wantTabs[nId] == nil then wantTabs[nId] = tbInfo nTabs = nTabs - (-1) end
            end
            for _, nGid in ipairs(supGroups or {}) do
                local bHave = false
                for _, nOld in ipairs(wantGroups) do
                    if nOld == nGid then bHave = true break end
                end
                if not bHave then wantGroups[#wantGroups - (-1)] = nGid end
            end
            return nGoods, nTabs
        end

        local nGoods, nTabs = MergeSupplyShop()
        if nTabs == nil or nTabs == 0 then
            -- Shop.Shop had not built its tables yet, so redo the merge right after the real load.
            ShopLogic.LoadGoodsConf = function(...) local r = oldGoods(...) MergeSupplyShop() return r end
            ShopLogic.LoadShopTabConf = function(...) local r = oldTabs(...) MergeSupplyShop() return r end
        end

        """;
    // <<< SUPPLY SHOP PAYLOAD <<<

    /// <summary>
    /// 多诺-巅峰对决. Three separate local time gates block it, and none survives a reconnect:
    /// (1) DonoDuelConfig:CheckOpenConfig() rejects the season while GetSeasonConfig()'s window is
    /// still the factory one - that table is only refreshed by DonoDuel_ClientCheck, which is pushed
    /// from PushDlcWindowOverrides and therefore only when the client calls DLCLogic_CheckOpenAct;
    /// (2) DonoDuelConfig:CheckFunctionOpen() (:603-632) rejects every function while
    /// tbFunctionList[id].nOpenTime/nCloseTime (from dlc/DonoDuel/donoduel_function.txt) is the factory
    /// window 1770235200..1773259200, i.e. 202602050400..202603120400. That is what hid all of the
    /// base NPCs: DonoDuel:CheckNpcShowMap (:1228-1229) returns CheckFunctionOpen(npc.nFunctionId) for
    /// NPC_TYPE_FUNC, so no interaction (F) prompt ever appears for the player;
    /// (3) DonoDuelArena:GetOpenLevelListForType filters the arena battle table with
    /// IsInTime(nOpenTime, nCloseTime) and every row still carries the same factory window, so the
    /// arena list comes back empty. Re-stamp all three onto the running clock.
    ///
    /// The season row also gets an empty tbCondition: season 1 ships
    /// Condition = {{{2,11429}},{{2,50101}}} and DonoDuelConfig:CheckOpenConfig (:568) runs it through
    /// Condition.CheckConditionWithOr, i.e. an unmet row would report 未开放. The old refresh path was
    /// the DonoDuel_ClientCheck callback (DonoDuel.lua:1919-1921), which also cleared it - and ended
    /// with DonoDuel:EnterBase(true) (:1925), so pushing it unsolicited walked the player into the
    /// base every time the DLC page was opened. Writing the table here has the same effect without the
    /// pseudo-click.
    ///
    /// DonoDuelConfig is a client-startup global (Preload.lua:304-308 requires DonoDuel, DonoDuelConfig
    /// and DonoDuelArena before login), so the tables are there by the time this runs - but their
    /// LoadConfig may not have populated tbSeasonList yet, and 'me' is still nil while the login push is
    /// executed (the patch is sent ahead of RspLogin), which is why the whole season list is stamped
    /// instead of looking the act id up through DonoDuel:GetActId(). Both loaders are wrapped because
    /// the client re-runs them from its own GM commands and would otherwise restore the dead windows,
    /// and DLC_Logic.InitFuncObj is hooked as well: it runs on the Logined event, i.e. right after 'me'
    /// exists, which guarantees a stamp before the DLC UI is built.
    /// </summary>
    public const string DonoDuelArenaWindows = """
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        local function ddfix()
        if not DonoDuelConfig then return end
        for _, c in pairs(DonoDuelConfig.tbSeasonList or {}) do c.nOpenTime = now - off1 c.nCloseTime = now - off2 c.tbCondition = {} end
        for _, f in pairs(DonoDuelConfig.tbFunctionList or {}) do f.nOpenTime = now - off1 f.nCloseTime = now - off2 end
        if DonoDuelArena and DonoDuelArena.tbArenaBattle then
        for _, cfg in pairs(DonoDuelArena.tbArenaBattle) do cfg.nOpenTime = now - off1 cfg.nCloseTime = now - off2 end
        end
        end
        ddfix()
        if DonoDuelConfig and not DonoDuelConfig.bWinPatched then
        DonoDuelConfig.bWinPatched = true
        local oldF = DonoDuelConfig.LoadFunctionConfig
        DonoDuelConfig.LoadFunctionConfig = function(...) local r = oldF(...) ddfix() return r end
        local oldC = DonoDuelConfig.LoadConfig
        DonoDuelConfig.LoadConfig = function(...) local r = oldC(...) ddfix() return r end
        end
        if DLC_Logic and not DLC_Logic.bDonoFuncStamp then
        DLC_Logic.bDonoFuncStamp = true
        local oldInit = DLC_Logic.InitFuncObj
        DLC_Logic.InitFuncObj = function(...) local r = oldInit(...) ddfix() return r end
        end
        """;

    /// <summary>
    /// One shot fix for every "this event has ended / is not open yet" gate in the client.
    ///
    /// All of those messages come from a single global helper, Misc/Lib.lua:506-509:
    ///     function IsInTime(nStart, nEnd, nNow)
    ///         nNow = nNow or GetTime()
    ///         return (nStart == -1 or nNow &gt;= nStart) and (nEnd == -1 or nNow &lt; nEnd)
    ///     end
    /// with 418 call sites across the extracted scripts. In particular:
    ///   - 弑神斗场 SeasonBossLogic.CheckLevelOpen (:908) rejects every level whose local
    ///     nStartTime/nEndTime (Challenge/SeasonBoss/SeasonBoss.lua:217-218) is in the past;
    ///   - 先行试用 / 角色试玩 Activity.IsOpen (Activity/Activity.lua:207) and
    ///     GachaTry.IsGachaTryAvalible both run on it, and Gacha:IsInTime (:754) calls the global
    ///     from its own table method, so gacha pools are fixed by the same wrapper;
    ///   - every other DLC sub feature that keeps its window in a local CSV (the four per-module
    ///     patches below) ends up here as well.
    ///
    /// The wrapper only relaxes the end of a window that has already started and expired, AND that
    /// ended inside the era the pinned DLC activated (see GlobalTimeWindowsFor): a window that has not
    /// begun (nStart in the future) stays closed, which keeps chapter 28 style "unlock by date"
    /// content honest, and a window that ended before the pinned DLC began belongs to an earlier
    /// version and keeps live-server semantics (expired = closed). "Has the window expired" is
    /// expressed as not old(-1, nEnd, now) because the payload must not contain '&lt;' or '&gt;' (see
    /// the class comment: they would be escaped to \uXXXX and the client could not decode the payload).
    /// It is applied once per (floor, era) pair (guarded by plain globals - note that Lua 5.1
    /// functions cannot carry fields, so the marker must NOT be attached to IsInTime itself) and is
    /// re-sent on every login because a reconnect can happen after the client reloaded Misc/Lib.lua.
    /// The era id is part of the guard: switching the served DLC at runtime sends the same chunk with
    /// a different era id, which must re-apply the wrapper against the *raw* IsInTime (kept in
    /// MikuRawIsInTime) instead of stacking a second wrapper - and it must reload the activity table
    /// again, because every row it stamped for the previous era has to be replaced by the new floor.
    /// </summary>
    public const string GlobalTimeWindows = """
        local function gtwfix()
        if not IsInTime then return end
        local floor = MIKU_ACTIVATION_FLOOR
        local era = MIKU_ACT_ERA
        if MikuGlobalTimePatched and MikuGlobalEraFloor == floor and MikuGlobalEraId == era then return end
        MikuGlobalTimePatched = true
        MikuGlobalEraFloor = floor
        MikuGlobalEraId = era
        local old = MikuRawIsInTime or IsInTime
        MikuRawIsInTime = old
        IsInTime = function(nStart, nEnd, nNow)
        if old(nStart, nEnd, nNow) then return true end
        if not nEnd or nEnd == -1 or nEnd == 0 then return false end
        if floor ~= 0 and math.max(nEnd, floor) ~= nEnd then return false end
        local t = nNow or GetTime()
        if old(-1, nEnd, t) then return false end
        return true
        end
        if Activity and Activity.LoadActivitiesConfig and Activity.bMikuEraReset ~= era then
        Activity.bMikuEraReset = era
        Activity.LoadActivitiesConfig()
        end
        end
        gtwfix()
        """;

    /// <summary>Placeholder in <see cref="GlobalTimeWindows"/>; replaced before the payload is sent.</summary>
    public const string ActivationFloorToken = "MIKU_ACTIVATION_FLOOR";

    /// <summary>Placeholder in <see cref="GlobalTimeWindows"/>; replaced before the payload is sent.</summary>
    public const string ActivationEraToken = "MIKU_ACT_ERA";

    /// <summary>
    /// <see cref="GlobalTimeWindows"/> with the era floor and era id baked in.
    /// <paramref name="activationFloor"/> is the unix second at which the served DLC began
    /// (DlcRuntimeOverrides.ActivationFloor, DLC35 -&gt; 2026-01-29 04:00): activating DLCxx activates
    /// that DLC's content, so a window that already ended before the floor belongs to an earlier
    /// version and keeps live-server semantics (expired = closed). Without it every historical
    /// activity - the 特别邀请 splash above all - activates at once, which is what "什么都串起来"
    /// was. 0 disables the gate.
    ///
    /// The payload also drops the activity windows the server stamped into the running session
    /// (ActivityQuest_Open copies them over the local tbConf row, and Quest_Refresh then sees
    /// nServerTime == tbConf.nStartTime and stops asking, leaving the row open forever). Reloading the
    /// shipped CSV puts the real windows back so the floor decides.
    ///
    /// Measured 2026-10-06 12:26: the reload branch does NOT fire on a login push - the chunk runs
    /// before the activity table has loaded, so 'Activity' is still nil and the guard skips it
    /// (Activity.bMikuEraReset stayed nil while MikuGlobalTimePatched became true). It is kept as the
    /// reconnect fallback; on login the client reloads its own activity table anyway, and the probe
    /// confirmed the historical rows went back to their CSV windows and closed.
    /// </summary>
    public static string GlobalTimeWindowsFor(long activationFloor, uint eraId = 0) =>
        GlobalTimeWindows
            .Replace(
                ActivationFloorToken,
                activationFloor.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace(
                ActivationEraToken,
                eraId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

    /// <summary>
    /// 活动打脸图（login splash）的护栏。Activityface.CheckDateTime (Activity/Activityface.lua:91-148)
    /// 决定一张打脸图能不能弹，而它的第一道判断就是全局 IsInTime —— 于是 GlobalTimeWindows 一旦把
    /// 「已过期」放开，**所有历史活动的打脸图同时变成可弹**，客户端会给每一张都排一次队
    /// （实测：重启后连续打开 152 次 ActiviyFace，正是「所有版本的弹窗」）。
    /// 打脸图只是公告，所以这里让它退回**原始**时间语义：只有自身时间窗真正在当下的活动才弹。
    /// MikuRawIsInTime 由 GlobalTimeWindows 在包装前捕获；万一拿不到（例如本次会话的 IsInTime 早已
    /// 被包装过、原始函数已不可达），就一律不弹——「安静」是这里的正确退化方向，绝不能退回全弹。
    /// 同时清掉登录时已经排好的队列，否则本次会话仍会把旧的 152 张弹完。
    /// </summary>
    public const string ActivityFaceQuiet = """
        local function afqfix()
        if not Activityface or not Activityface.CheckDateTime then return end
        if Activityface.bQuietPatched then return end
        Activityface.bQuietPatched = true
        local oldCheck = Activityface.CheckDateTime
        Activityface.CheckDateTime = function(InId)
        local raw = MikuRawIsInTime
        if not raw then return false end
        local cfg = Activityface.GetConfig(InId)
        if not cfg then return false end
        if not raw(cfg.tStarttime, cfg.tEndtime, GetTime()) then return false end
        return oldCheck(InId)
        end
        Activityface.tbPopFaceId = {}
        Activityface.bLoginFirst = false
        end
        afqfix()
        """;

    /// <summary>
    /// DLC entry window. UMG/Main/umg_main.lua:1344 ShowBtnDlc takes the current DLC row from
    /// DLC_Logic.GetCurConf() and hands nCloseEndTime to the main-screen countdown widget
    /// (uw_main_time:ShowNormal(time, callback)), and that widget runs the callback IMMEDIATELY when
    /// the deadline is already past (UMG/Main/Widgets/uw_main_time.lua:49-56). The callback is
    /// ShowBtnDlc itself, so an expired DLC window recurses until the Lua stack overflows - measured
    /// 2026-10-06 11:25:32 as "Chapter.lua:666: stack overflow ... (skipping 363591 levels)" with
    /// uw_main_time:18 -> umg_main:1355 -> ShowBtnDlc repeating. GlobalTimeWindows is what exposes it:
    /// the window itself (dlc/dlc_activities.txt EnterStartTime/CloseEndTime) is long expired, but the
    /// relaxed IsInTime lets ShowBtnDlc take the "open" branch anyway.
    ///
    /// The fix re-stamps the window of whichever DLC row GetCurConf() reports onto the running clock
    /// instead of hiding the button: nEnterStartTime one hour in the past (so the first IsInTime branch
    /// is genuinely true and the button keeps its icon/label) and nCloseEndTime thirty days ahead, so
    /// the countdown widget shows "30天" and never fires its callback early. Do not simply move the
    /// deadline into the past - that would make IsInTime false and collapse the DLC entry entirely.
    /// </summary>
    public const string DlcWindowFix = """
        local function dlcwinfix()
        if not DLC_Logic or not DLC_Logic.GetCurConf then return end
        if DLC_Logic.bCurWinPatched then return end
        DLC_Logic.bCurWinPatched = true
        local oldGet = DLC_Logic.GetCurConf
        DLC_Logic.GetCurConf = function(...)
        local conf = oldGet(...)
        if conf then
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        conf.nEnterStartTime = now - off1
        conf.nCloseEndTime = now - off2
        end
        return conf
        end
        end
        dlcwinfix()
        """;

    /// <summary>
    /// 多诺-巅峰对决 base spawn. DonoDuel:LoadForStarPos (DLC/DonoDuel/DonoDuel.lua:1390-1430) moves
    /// the PlayerStart tagged "DonoPlayerStart" to a position that the CLIENT persisted locally
    /// (DonoDuel:SaveInitPlayerPos :1369-1376 writes UUserSetting "Dono_PlayerPos_&lt;uid&gt;_&lt;actId&gt;",
    /// umg_donoduel_room.lua:136 is the writer) and only falls back to the shipped spawn
    /// {-1705, 0, 168} while the stored Z is below 50 (:1412-1416). A position saved from a broken
    /// entry therefore survives every later entry and the player keeps dropping through the floor,
    /// so the stored value is cleared once and the shipped spawn point used instead.
    /// </summary>
    public const string DonoDuelBaseSpawn = """
        local function ddspawn()
        if not DonoDuel or not DonoDuel.LoadForStarPos then return end
        if DonoDuel.bSpawnPatched then return end
        DonoDuel.bSpawnPatched = true
        local oldLoad = DonoDuel.LoadInitPlayerPos
        DonoDuel.LoadInitPlayerPos = function(self, ...)
        oldLoad(self, ...)
        DonoDuel.tbInitPlayerPos = {tbPos = {-1705, 0, 168}, tbRot = {0, 0, 0}}
        return DonoDuel.tbInitPlayerPos
        end
        local old = DonoDuel.LoadForStarPos
        DonoDuel.LoadForStarPos = function(self, ...)
        DonoDuel.tbInitPlayerPos = {tbPos = {-1705, 0, 168}, tbRot = {0, 0, 0}}
        return old(self, ...)
        end
        end
        ddspawn()
        """;

    /// <summary>
    /// 异星守护 (alien defense / Mow). Entering a level group is gated by two numbers that never come
    /// from the server: umg_mow_main.lua:180-191 OpenMap tips ui.TxtNotOpen when IsInTime(min
    /// nStartTime, max nEndTime) fails, and both are read per level from
    /// AlienDefenseLevel.GetLevelConfig() (:134-141) - i.e. the local CSV
    /// dlc/AlienDefense/Aliendefense_level.txt, whose act 4 rows still carry the factory window
    /// 20260211..20260303. The act level gates in AlienDefense.lua (:508, :515, :676) and the endless
    /// daily rotation in umg_mow_map3.lua read the same fields, so one re-stamp covers them all.
    /// AlienDefenseLevel is a plain global loaded by require from AlienDefense.lua:7, which means it
    /// may not exist yet at login time - the CheckOpenAct handler pushes this again, and by then the
    /// table is guaranteed to be there. LoadConfig is wrapped because the client can re-run it from
    /// its own GM commands (GMCommandRegister.lua:6401) and would otherwise restore the dead window.
    ///
    /// The act rows themselves (AlienDefenseConfig.tbTimeList, aliendefense/alienlist.txt act 4 =
    /// [202602120400]..[202603040400]) are stamped here too: DlcFuncMow.OnGetTimeInfo (:19-22) hands
    /// those two numbers to DlcFuncBase.CheckIsUnlock, which compares nEndTime against the clock
    /// DIRECTLY (:97 nNowSec &gt;= nEndTime -&gt; ui.TxtDLC1Over) and therefore does not route through
    /// the relaxed IsInTime. Without this stamp the 异星守护 button on the DLC page reads 已结束 and
    /// OpenFunc refuses to run. It used to be refreshed by pushing the AlienDefense_CheckOpenAct
    /// callback unsolicited, which made the client walk straight into MowMain (that callback ends with
    /// UI.SafeOpen(MowMain) at AlienDefense.lua:813/818) - a patch writes the table without the
    /// pseudo-click. Every act row is stamped rather than the current act's, so no
    /// AlienDefense.GetActId() lookup is needed: that helper goes through me:GetAttribute, and 'me' is
    /// still nil while the login push is being executed (the patch is sent ahead of RspLogin).
    /// DLC_Logic.InitFuncObj is hooked as well - it runs on the Logined event, right after 'me' exists,
    /// which guarantees a stamp before the DLC UI is built.
    /// </summary>
    public const string AlienDefenseLevelWindows = """
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        local function mowstamp()
        if AlienDefenseConfig then
        for _, cfg in pairs(AlienDefenseConfig.tbTimeList or {}) do cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end
        end
        if not AlienDefenseLevel then return end
        for _, cfg in pairs(AlienDefenseLevel.tbLevel or {}) do cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end
        end
        mowstamp()
        if AlienDefenseConfig and not AlienDefenseConfig.bActWinPatched then
        AlienDefenseConfig.bActWinPatched = true
        local oldAct = AlienDefenseConfig.LoadConfig
        AlienDefenseConfig.LoadConfig = function(...) local r = oldAct(...) mowstamp() return r end
        end
        if AlienDefenseLevel and not AlienDefenseLevel.bWinPatched then
        AlienDefenseLevel.bWinPatched = true
        local old = AlienDefenseLevel.LoadConfig
        AlienDefenseLevel.LoadConfig = function(...) local r = old(...) mowstamp() return r end
        end
        if DLC_Logic and not DLC_Logic.bMowFuncStamp then
        DLC_Logic.bMowFuncStamp = true
        local oldInit = DLC_Logic.InitFuncObj
        DLC_Logic.InitFuncObj = function(...) local r = oldInit(...) mowstamp() return r end
        end
        """;

    /// <summary>
    /// 抢滩登陆 (Sevendays / FunTimed). Unlike every other season mode this one never calls the global
    /// IsInTime helper: FunTimedLevel.IsOpenTime (Launch/FunTimed/FunTimed.lua:30-41) and
    /// FunTimedSession.CheckOpen (:161-180) compare GetTime() against tbTimeLimit / tbOpenTime directly,
    /// so GlobalTimeWindows cannot reach them and the six shipped seasons (the last one ran
    /// 2026-04-09..04-23) all read as closed. The tables are plain globals the client rebuilds inside
    /// FunTimed.Load (:282-336, called at file scope on :337), which is why the wrapper re-stamps after
    /// every reload instead of editing the CSV once.
    /// </summary>
    public const string FunTimedWindows = """
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        local function ftfmt(t)
        local s = os.date([[%Y%m%d%H%M]], t)
        if s and #s == 12 then return s end
        return t
        end
        local function ftstamp()
        if not FunTimed then return end
        if FunTimed.tbSession then
        for _, s in pairs(FunTimed.tbSession) do s.tbOpenTime = { ftfmt(now - off1), ftfmt(now - off2) } end
        end
        if FunTimed.tbLevels then
        for _, l in pairs(FunTimed.tbLevels) do l.tbTimeLimit = { ftfmt(now - off1), ftfmt(now - off2) } end
        end
        end
        ftstamp()
        if FunTimed and not FunTimed.bWinPatched then
        FunTimed.bWinPatched = true
        local oldLoad = FunTimed.Load
        FunTimed.Load = function(...) local r = oldLoad(...) ftstamp() return r end
        end
        if DLC_Logic and not DLC_Logic.bFtFuncStamp then
        DLC_Logic.bFtFuncStamp = true
        local oldInit = DLC_Logic.InitFuncObj
        DLC_Logic.InitFuncObj = function(...) local r = oldInit(...) ftstamp() return r end
        end
        """;

    /// <summary>
    /// Per-era window fix for the DLC function buttons that have no dedicated patch of their own.
    ///
    /// DlcFuncBase.CheckIsUnlock (DLC/DLCFunc/DlcFuncBase.lua:43-52) reads the button's window FIRST and
    /// returns FuncState.Over ("ui.TxtDLC1Over", 已结束) before it ever asks OnCheckIsUnlock, so a button
    /// whose table still carries the shipped CSV window is dead no matter how far GlobalTimeWindows
    /// relaxes IsInTime - and GlobalTimeWindows cannot help anyway, because its era floor deliberately
    /// keeps any window that ended before the pinned era's start closed.
    ///
    /// Every DlcFunc takes its window from one table, and the set of buttons that exist for an era is
    /// exactly the keys of that era row's DlcFuncConfId (DLC_Logic.lua:69 evaluates it, :1381 builds one
    /// button per key, DlcFuncBase.GetFuncConfigId:145-148 looks the key up again). So instead of a
    /// patch per activity, this walks the CURRENT era's keys and re-stamps whatever table each of them
    /// reads - era-agnostic, and correct for any era the /dlc command pins afterwards:
    ///   Rush / Event / Onlinesea / Onlinepurge -> Online.GetGroupConfig(id)  (DlcFuncRush, DlcFuncEvent,
    ///     DlcFuncOnlinesea, DlcFuncOnlinepurge all take min start / max end over the group's rows)
    ///   Puzzle / Former                       -> Activity activity row (DlcFuncPuzzle.OnGetTimeInfo,
    ///     DlcFuncFormer.OnGetTimeInfo; Activity.IsOpen also short-circuits on nCloseFlag > 0)
    ///   Worldboss                             -> WorldBossLogic.tbBossListCfg[id][actId] (DlcFuncWorldboss
    ///     filters the acts through IsInTime and FALLS BACK to act 2's raw window when none survives,
    ///     which is why relaxing IsInTime alone still reads the button as Over)
    ///   Farmwar                               -> FarmWarActivityCfg.tbActivityCfg[id]
    ///   Celebrate                             -> StarWishConfig.tbTimeList[id]
    /// Refresh / Defense / Teamboss / Sevendays already have a server push (DLCChapter_SyncTime,
    /// DefendLogic_CheckOpenAct, SeasonBoss_GMChangeTime) or their own patch (FunTimedWindows), and are
    /// deliberately not routed here.
    ///
    /// Re-stamping has to survive the client's own reloads, so the driver is also chained onto
    /// Activity.LoadActivitiesConfig (GlobalTimeWindows' era reset calls it and rebuilds every row),
    /// Online.LoadConfig and DLC_Logic.InitFuncObj (DLC page entry). All three are idempotent and the
    /// flags keep a re-sent patch from stacking wrappers.
    /// </summary>
    public const string DlcFuncWindowFix = """
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        local nWinStart = now - off1 local nWinEnd = now - off2
        local function dfwGroup(nId)
        if not (Online and Online.GetGroupConfig) then return end
        local tbRows = Online.GetGroupConfig(nId)
        if type(tbRows) ~= type({}) then return end
        for _, row in pairs(tbRows) do
        if type(row) == type({}) then row.nStartTime = nWinStart row.nEndTime = nWinEnd end
        end
        end
        local function dfwActivity(nId)
        local cfg = Activity and Activity.tbActivityConfig and Activity.tbActivityConfig[nId]
        if type(cfg) ~= type({}) and Activity and Activity.GetActivityConfig then
        cfg = Activity.GetActivityConfig(nId)
        end
        if type(cfg) ~= type({}) then return end
        cfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd
        if (cfg.nCloseFlag or 0) ~= 0 then cfg.nCloseFlag = 0 end
        end
        local function dfwWorldboss(nId)
        if not (WorldBossLogic and WorldBossLogic.tbBossListCfg) then return end
        local tbActs = WorldBossLogic.tbBossListCfg[nId]
        if type(tbActs) ~= type({}) then return end
        for _, cfg in pairs(tbActs) do
        if type(cfg) == type({}) then
        cfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd cfg.nAwardTime = nWinEnd
        end
        end
        end
        local function dfwFarmwar(nId)
        local cfg = FarmWarActivityCfg and FarmWarActivityCfg.tbActivityCfg and FarmWarActivityCfg.tbActivityCfg[nId]
        if type(cfg) ~= type({}) then return end
        cfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd
        end
        local function dfwCelebrate(nId)
        local cfg = StarWishConfig and StarWishConfig.tbTimeList and StarWishConfig.tbTimeList[nId]
        if type(cfg) ~= type({}) then return end
        cfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd
        end
        local tbRoute = {
        Rush = dfwGroup, Event = dfwGroup, Onlinesea = dfwGroup, Onlinepurge = dfwGroup,
        Puzzle = dfwActivity, Former = dfwActivity,
        Worldboss = dfwWorldboss, Farmwar = dfwFarmwar, Celebrate = dfwCelebrate,
        }
        local function dfwstamp()
        if not (DLC_Logic and DLC_Logic.GetCurConf) then return end
        local conf = DLC_Logic.GetCurConf()
        local tbIds = conf and conf.tbDlcFuncConfId
        if type(tbIds) ~= type({}) then return end
        for sKey, nId in pairs(tbIds) do
        local fnFix = tbRoute[sKey]
        if fnFix and type(nId) == [[number]] then fnFix(nId) end
        end
        end
        dfwstamp()
        if DLC_Logic and not DLC_Logic.bDfwStamp then
        DLC_Logic.bDfwStamp = true
        local oldInit = DLC_Logic.InitFuncObj
        DLC_Logic.InitFuncObj = function(...) local r = oldInit(...) dfwstamp() return r end
        end
        if Activity and not Activity.bDfwStamp and Activity.LoadActivitiesConfig then
        Activity.bDfwStamp = true
        local oldActs = Activity.LoadActivitiesConfig
        Activity.LoadActivitiesConfig = function(...) local r = oldActs(...) dfwstamp() return r end
        end
        if Online and not Online.bDfwStamp and Online.LoadConfig then
        Online.bDfwStamp = true
        local oldOnl = Online.LoadConfig
        Online.LoadConfig = function(...) local r = oldOnl(...) dfwstamp() return r end
        end
        """;

    /// <summary>
    /// 异绪消解 (GirlRush) window fix. The single shipped season (girlrush/girlrushlist.json Id 1) ran
    /// 2025-05-15..2025-05-29 and every stage row carries the same window, both long before any DLC era
    /// this server can pin - GlobalTimeWindows keeps those closed on purpose (a window that ended before
    /// the activation floor belongs to an older version), so the mode could never be opened again.
    ///
    /// The client rebuilds GirlRushConfig.tbRushList / tbRushLevel inside
    /// GirlRushConfig.LoadGirlRush*Cfg (GirlRush/GirlRushConfig.lua:132-205) and compares the parsed
    /// nStartTime/nEndTime through the global IsInTime (GirlRushLevelTemplate.IsInTime:100-105), so
    /// re-stamping those two tables is enough - and the wrappers keep it stamped after a reload.
    /// </summary>
    public const string GirlRushWindows = """
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        local function grstamp()
        if not GirlRushConfig then return end
        local function fix(tb)
        if type(tb) ~= type({}) then return end
        for _, cfg in pairs(tb) do
        if type(cfg) == type({}) then cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end
        end
        end
        fix(GirlRushConfig.tbRushList)
        fix(GirlRushConfig.tbRushLevel)
        end
        grstamp()
        if GirlRushConfig and not GirlRushConfig.bGrWindowPatched then
        GirlRushConfig.bGrWindowPatched = true
        local oldList = GirlRushConfig.LoadGirlRushListCfg
        if oldList then
        GirlRushConfig.LoadGirlRushListCfg = function(...) local r = oldList(...) grstamp() return r end
        end
        local oldLevel = GirlRushConfig.LoadGirlRushLevelCfg
        if oldLevel then
        GirlRushConfig.LoadGirlRushLevelCfg = function(...) local r = oldLevel(...) grstamp() return r end
        end
        end
        if DLC_Logic and not DLC_Logic.bGrFuncStamp then
        DLC_Logic.bGrFuncStamp = true
        local oldInit = DLC_Logic.InitFuncObj
        DLC_Logic.InitFuncObj = function(...) local r = oldInit(...) grstamp() return r end
        end
        """;

    /// <summary>
    /// Cartoon/cutscene backdrop. The framework helper PlayPlot (Misc/UE4.lua:592-620) hides exactly
    /// ONE widget - UI.GetTop(), i.e. the last Stack UI - and restores it when the cartoon finishes, so
    /// any UI below it keeps covering the cartoon. Measured on the reported case: entering 心意寄语 from
    /// the DLC page stacks Dlc35Main -&gt; Dorm (HouseLogic.GoToHouseFromGirlSendMessage -&gt;
    /// House.lua:1631 LoadHouse -&gt; :244-246 OpenUI) and then trips the dorm anniversary story -
    /// House.lua:2111 OnTransHall gates it with IsInTime(202507200400, 202508210400), which the era patch
    /// re-opens, and House.lua:1352-1371 PlayDLC30Story plays plot 8230301 after hiding only the top UI.
    /// The cartoon is drawn inside the 3D scene and its images load asynchronously, so every visible UMG
    /// widget shows through (the "dorm UI in the four corners" report) - the fix is to hide all of them
    /// for the duration and put them back afterwards. Verified live: during the plot the dorm and AdinGM
    /// widgets read IsVisible false while the plot's own subtitle layer (dialogue, opened by the plot
    /// itself) stays visible, and everything is restored once it ends.
    /// The wrapper keeps bHideTopUI semantics (a caller that passes false - the PlotMask convention used
    /// by UMG/StarWish/umg_starwish_main.lua:390-391 - is left completely alone) and re-wraps whenever
    /// PlayPlot is no longer our wrapper, because a client Reload of Misc/UE4.lua would otherwise leave a
    /// stale flag behind (Lua 5.1 function values cannot carry fields, so identity is compared instead).
    /// MikuPlotToken guards overlapping plots: when a new plot starts before the old one is torn down, the
    /// old restore is skipped and the new plot owns the list (its hide pass walks every logically open
    /// widget, so already hidden ones are included and nothing is left hidden).
    /// </summary>
    public const string PlotHideAllWindows = """
        local function dplot()
        if not PlayPlot then return end
        if MikuPlotWrapped == PlayPlot then return end
        local old = PlayPlot
        local function wrap(id, cb, ca, bgm, hide)
        if hide == false or not id or id == 0 then return old(id, cb, ca, bgm, hide) end
        local token = {}
        MikuPlotToken = token
        local hid = {}
        if UI and UI.tbWidget then
        for _, w in pairs(UI.tbWidget) do
        if w and w.IsOpen and w:IsOpen() then
        WidgetUtils.Hidden(w)
        table.insert(hid, w)
        end
        end
        end
        local function back()
        if MikuPlotToken ~= token then return end
        for i = 1, #hid do
        local w = hid[i]
        if w and IsValid(w) then WidgetUtils.Visible(w) end
        end
        end
        local function done(...)
        back()
        if ca then return ca(...) end
        end
        return old(id, cb, done, bgm, false)
        end
        MikuPlotWrapped = wrap
        PlayPlot = wrap
        end
        dplot()
        """;

    /// <summary>Replaced by the pinned era's activation floor (unix seconds) in <see cref="GachaTryWindowsFor"/>.</summary>
    public const string GachaTryFloorToken = "MIKU_ACT_FLOOR";

    /// <summary>
    /// 先行试用 / 角色试玩 (gacha try). Two independent client gates keep DLC 35's try button shut:
    ///
    ///   * DLC/DLCFunc/DlcFuncTry.lua:21-24 hands DlcFuncBase the window from
    ///     GachaTry.GetTimeConf(conf.tbTryID), which is the earliest start and the latest end over
    ///     every activity row of the two groups. The DLC 35 groups are 561 and 576 and every one of
    ///     their 38 rows ended in March 2026, so the latest end is long past and DlcFuncBase.lua:52
    ///     turns the button into FuncState.Over (ui.TxtDLC1Over) before it even asks about unlock
    ///     state.
    ///   * The list inside the page is built from the same rows: ShowTab
    ///     (UMG/Activitytry/umg_activitytrydlc.lua:150-169) walks Activity.GetCaseByGroup(group) and
    ///     keeps every row that passes Activity.IsOpen, and OpenDLCUI (Activity/GachaTry.lua:244-263)
    ///     refuses to open at all when no row does. So the page shows one entry per live row of the
    ///     groups, and the era gate alone would leave exactly one (act 580, the row the server picks).
    ///
    /// The patch therefore walks every row of the DLC's own TryID groups and stamps the ones that
    /// belong to the pinned era - the rows whose shipped window ends at or after the activation floor -
    /// as open (now-1h .. now+30d), clearing tbCondition (act 580 carries [[[2,11429]],[[2,10104]]],
    /// i.e. clear level 11429 or 10104, which Condition.CheckConditionWithOr would evaluate against
    /// this account's pass times). Rows of an earlier era are left untouched: their own window is in
    /// the past, so IsInTime keeps them closed. That restores the whole 自往昔归还 try roster
    /// (15 rows of group 561 plus 23 rows of group 576) instead of a single entry, and GetTimeConf
    /// then reports a live window so the DLC button works too.
    ///
    /// The stamped start is taken from the server attribute (50, actId * 10) whenever it is non-zero,
    /// because Activity.Quest_Refresh (Activity.lua:1332-1356) reports the activity as CLOSED - and
    /// re-asks every 30 seconds - until the attribute and the row's nStartTime agree within an hour.
    /// HandlerReqLogin stamps that attribute for every row of the groups before pushing this patch, so
    /// the first click already matches; the "now - off1" fallback only covers an attribute that was
    /// never written.
    ///
    /// The stamp has to survive two reloads: Activity.LoadActivitiesConfig() rebuilds tbConfig and
    /// tbModelGroup from the CSV (the era patch calls it once at login) and DLC_Logic.InitFuncObj runs
    /// on the Logined event / DLC page build. Both are wrapped, and the idempotency flag is a plain
    /// global reset by the load wrapper, because Lua 5.1 function values cannot carry fields. At login
    /// time Activity is still nil (Preload.lua has not run the module), so the login push is a no-op
    /// and the DLC-entry push does the work - which is fine, the button only exists on the DLC page.
    ///
    /// The flag names carry a "2" suffix so a client that already ran the first revision of this patch
    /// (which pushed the non-era rows ten years into the future) repairs itself on the next push
    /// instead of returning early on the old flag.
    /// </summary>
    public const string GachaTryWindows = """
        local function gtry()
        if not Activity or not Activity.tbConfig then return end
        if not DLC_Logic or not DLC_Logic.GetCurConf then return end
        local floor = MIKU_ACT_FLOOR
        local era = MIKU_ACT_ERA
        if MikuTryWin2 and MikuTryWinFloor == floor and MikuTryWinEra == era then return end
        local conf = DLC_Logic.GetCurConf()
        if not conf or not conf.tbTryID then return end
        local now = GetTime() local off1 = 3600 local off2 = -2592000
        for _, group in pairs(conf.tbTryID) do
        local rows = Activity.GetTemplateCase(group)
        if rows then
        for _, row in pairs(rows) do
        local nEnd = row.nEndTime
        if nEnd and nEnd ~= -1 and math.max(nEnd, floor) == nEnd then
        local srv = nil
        if Activity.GetOpenTime then srv = Activity.GetOpenTime(row.nId) end
        if srv and srv ~= 0 then row.nStartTime = srv else row.nStartTime = now - off1 end
        row.nEndTime = now - off2
        row.tbCondition = {}
        end
        end
        end
        end
        MikuTryWin2 = true
        MikuTryWinFloor = floor
        MikuTryWinEra = era
        end
        MikuTryApply = gtry
        gtry()
        if not MikuTryHooked2 then
        MikuTryHooked2 = true
        if DLC_Logic and DLC_Logic.InitFuncObj then
        local oldInit = DLC_Logic.InitFuncObj
        DLC_Logic.InitFuncObj = function(...) local r = oldInit(...) if MikuTryApply then MikuTryApply() end return r end
        end
        if Activity and Activity.LoadActivitiesConfig then
        local oldLoad = Activity.LoadActivitiesConfig
        Activity.LoadActivitiesConfig = function(...) MikuTryWin2 = nil local r = oldLoad(...) if MikuTryApply then MikuTryApply() end return r end
        end
        end
        """;

    /// <summary>
    /// Binds the pinned era's activation floor (DlcRuntimeOverrides.ActivationFloor(), e.g. DLC 35 ->
    /// 1769630400) and era id into <see cref="GachaTryWindows"/>. The floor is what tells the patch
    /// which rows of the DLC's try groups belong to this era and which ones are earlier reprints that
    /// stay closed; the era id is what lets a runtime era switch re-stamp the rows of the new era
    /// (the previous era's rows were stamped open in the running session, and the reload only puts the
    /// shipped windows back - the guard would otherwise treat that as "already done").
    /// </summary>
    public static string GachaTryWindowsFor(long floor, uint eraId = 0) =>
        GachaTryWindows
            .Replace(GachaTryFloorToken, floor.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(ActivationEraToken, eraId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// Error isolation for the Lua event bus. Misc/EventSystem.lua dispatches every listener of an
    /// event in a bare loop with no pcall and no per-listener guard:
    ///
    ///   * Trigger (Misc/EventSystem.lua:130-132): "for _, f in ipairs(tbTODO) do f(...) end";
    ///   * TriggerTarget (Misc/EventSystem.lua:175): "for _, f in pairs(tbEvents) do f(pTarget, ...) end".
    ///
    /// In 决战哈姆 the kill-count logic node (WaterPark/Common/LogicNode/LT_WP_KillMonsterBySpawnNotify.lua)
    /// registers its death listener in ExecuteBP (:17) and only unregisters it in BP_TaskNode_Finish
    /// (:93-97), so a node released with its listener still attached makes the NEXT level's dispatch
    /// raise "attempt to read property 'KillTag' on released object" inside the loop (:65). That aborts
    /// every listener registered after the dead one - the live kill node never sees the kill, the wave
    /// never reaches its target count and the run can no longer be cleared (2026-10-06 22:14 session:
    /// zero 杀怪目标查询 lines, wave stuck; after the client restart the same level cleared normally
    /// with 36/36 kills and the boss wave 1/1, and the log held no released-object error at all).
    ///
    /// The patch replaces both dispatchers with pcall-per-listener copies that keep the original
    /// semantics (bOnce removal, the Logined -&gt; ZoneTime.DoLogin() priority, TriggerTarget's
    /// </summary>
    public const string EventSystemGuard = """
        if not MikuEvtSafe then
        MikuEvtSafe = true
        EventSystem.Trigger = function(e, ...)
        local tbRemoved = {}
        local tbTODO = {}
        for k, v in pairs(EventSystem.tbEvents[e] or {}) do
        table.insert(tbTODO, v.f)
        if v.bOnce then table.insert(tbRemoved, k) end
        end
        if e == [[Logined]] then ZoneTime.DoLogin() end
        for _, k in ipairs(tbRemoved) do
        local t = EventSystem.tbEvents[e]
        if t then t[k] = nil end
        EventSystem.tbHandles[k] = nil
        end
        for _, f in ipairs(tbTODO) do
        local ok, err = pcall(f, ...)
        end
        end
        EventSystem.TriggerTarget = function(p, s, ...)
        local tbTargets = EventSystem.tbTargets[p]
        if not tbTargets then return false end
        local tbEvents = tbTargets[s]
        if not tbEvents then
        return false
        end
        if next(tbEvents) == nil then return end
        for _, f in pairs(tbEvents) do
        local ok, err = pcall(f, p, ...)
        end
        return true
        end
        end
        """;

    /// <summary>
    /// 决战哈姆 / 冲关啦 artwork preload. BP_WPZBugRunMgr.lua:11-23 wraps the whole client-side preload
    /// block in "if not self:HasAuthority()", but this private server runs every 联机 level as a
    /// standalone world (the client itself logs "IsStandalone: true IsServer: true"), so HasAuthority()
    /// is true - the block that pulls in the mode's Niagara/blueprint/UI/UMG/icon packages
    /// (/Game/PreloadConfig/Misc/*) and calls PreloadOtherAsset() (BP_WPZBugRunMgr.lua:532-600, the
    /// Q-version animation, montage, blend-space and Wwise bank lists) never runs. Result observed in
    /// the client log: "UWaterParkQAnimIns::InitZBugWeaponAsset Failed!!! NoResPath CharPath: 0,
    /// WeaponPath:1" spamming ~5x/s inside PartyGo_9633 (1904 hits in one run) and the mode's effects
    /// and outline assets only streaming in on demand.
    ///
    /// The patch therefore wraps WPInterface.LoadMapComplete (WaterPark/GameBase/WPInterface.lua:10,
    /// a startup global: Preload.lua:254 requires WaterPark.WaterParkLoad, whose :38 requires
    /// WaterPark.GameBase.GameBaseInit, whose :16 defines WPInterface) and, for PartyGo_ levels, runs
    /// the exact same eight PreLoadGameAssetByConfig calls plus the manager's PreloadOtherAsset().
    /// Measured with the same hook installed live through /lua (2026-10-06 22:14 run): the NoResPath
    /// spam dropped from 1904 to 104 per run - the remaining ones come from the anim instance asking
    /// for a weapon on actors that are not Q-version characters at all.
    /// </summary>
    public const string PartyZBugPreload = """
        if not MikuZBugHook then
        MikuZBugDo = function()
        if WPInterface and WPInterface.LoadMapComplete then
        MikuZBugHook = true
        local oldComplete = WPInterface.LoadMapComplete
        WPInterface.LoadMapComplete = function(w)
        local r = oldComplete(w)
        if w then
        local n = w:GetName()
        if string.find(n, [[PartyGo_]]) then
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/ZBugNiagara.ZBugNiagara]], [[ZBugNiagara]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/ZBugBP.ZBugBP]], [[ZBugBP]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/ZBugUI.ZBugUI]], [[ZBugUI]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/ZBugUMG.ZBugUMG]], [[ZBugUMG]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/DLC35ZBugNiagara.DLC35ZBugNiagara]], [[DLC35ZBugNiagara]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/ZBugChallengeBP.ZBugChallengeBP]], [[ZBugChallengeBP]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/PartyIcon.PartyIcon]])
        UE4.UGameLibrary.PreLoadGameAssetByConfig([[/Game/PreloadConfig/Misc/PartyBuffIcon.PartyBuffIcon]])
        local all = UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEZBugRunMgr)
        local cnt = 0
        if all then
        local total = all:Length()
        for i = 1, total do
        local m = all:Get(i)
        if m and m.PreloadOtherAsset then
        cnt = cnt - (-1)
        m:PreloadOtherAsset()
        end
        end
        end
        end
        end
        return r
        end
        return true
        end
        return false
        end
        MikuZBugTry = function(left)
        if MikuZBugDo() then return end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuZBugTry(left - 1) end) end
        end
        MikuZBugTry(120)
        end
        """;

    /// <summary>
    /// 决战哈姆 item pickup fix (v2). BP_WPItemPoint.lua:245-257 StartRefresh() calls
    /// self:OnRep_Item() whenever WP.GameMgr:WorldIIsStandalone(world) is true
    /// (.temp/release-script/Game/Content/Script/WaterPark/GameBase/WPGameMgr.lua:44-45
    /// -&gt; IsStandalone), i.e. exactly in this private server's standalone runs, but OnRep_Item
    /// exists nowhere: the whole 5007-file script tree mentions it only at that one call site, and
    /// the blueprint (.temp/../倒数两个版本的文件/dataoutput_20260715_221020/Game/Content/Blueprints/
    /// LevelTask/Level/MultiLevel/Party_02/Actor/BP_WPItemPoint.uasset, tokens
    /// "Item/curItemId/ItemList/bNeedDestroyItem/OnRep_curItemId", zero hits for
    /// "OnRep_Item") does not declare it either. The call therefore raises
    ///   [string ".../BP_WPItemPoint.lua"]:249: attempt to call a nil value (method 'OnRep_Item')
    /// from .temp/release-script/Game/Content/Script/WaterPark/Common/Actors/BP_WP_Item.lua:268
    /// PlayerCollision_MultiCast &lt;- :139 OnComponentBeginOverlap, aborting the rest of the pickup:
    /// PointOwner=nil, SetCustomRepData(1,1) and ItemMgr:FreeItem(self) (same file :279-282) never
    /// run - so crates are never consumed and can be picked up again after leaving the trigger radius.
    ///
    /// v1 only added OnRep_Item to the class table found in package.loaded. A live probe on
    /// 2026-10-06 23:21 proved that is necessary but not sufficient: the class table held
    /// OnRep_Item=function while
    /// .temp/release-script/Game/Content/Script/WaterPark/Common/Actors/BP_WPItemPoint.lua:249 kept
    /// raising for 9 pickups between 23:13:56 and 23:15:50, and UnLua class tables have no metatable
    /// at all (getmetatable(cls)=nil), so method lookup for a live instance is not a plain rawget on
    /// that table - fields added after the class was registered (and, for subclass-registered
    /// classes, a second table) do not reach the instances that are already alive.
    ///
    /// v2 therefore patches four levels and never stops scanning:
    ///  1. require is hooked at login, before any level is loaded, and patches the module table the
    ///     moment it is required - i.e. before UnLua registers the class and snapshots its method
    ///     binding. This is the decisive layer: item points are spawned during level init, so the
    ///     module is usually already required by the time LoadMapComplete fires (23:11:36.633), which
    ///     is exactly why patching the table at that point (v1) came too late;
    ///  2. every table in package.loaded that is either keyed *BP_WPItemPoint* or owns a
    ///     StartRefresh function (safe shape:
    ///     .temp/release-script/Game/Content/Script/WaterPark/Common/Actors/BP_WPItemPoint.lua:245 is
    ///     the only definition in the 5007-file tree, and BP_WP_Item.lua:268 the only caller);
    ///  3. every live actor whose name contains WPItemPoint, patched on the instance itself, which
    ///     is the lookup path the failing call really uses (self.canSpawn/self.Item writes in the
    ///     same function prove per-instance fields resolve);
    ///  4. StartRefresh is wrapped in pcall, so even if OnRep_Item stays nil the exception can no
    ///     longer abort BP_WP_Item.PlayerCollision_MultiCast and ItemMgr:FreeItem(self) still runs.
    /// The scan runs from the PartyGo_ level hook every 2s for 400s (a match is 300s) instead of
    /// stopping at the first hit, because the module is only required when the item point blueprint
    /// </summary>
    public const string ZBugItemFix = """
        if not MikuItemFix then
        MikuItemFix = true
        MikuItemClsKey = [[WaterPark.Common.Actors.BP_WPItemPoint]]
        MikuItemNoop = function() end
        MikuItemPatchTable = function(v, tag)
        local n = 0
        if v.OnRep_Item == nil then
        v.OnRep_Item = MikuItemNoop
        n = n - (-1)
        end
        if type(v.StartRefresh) == [[function]] and v.MikuSrGuard ~= true then
        local mikuSrOrig = v.StartRefresh
        v.MikuSrGuard = true
        v.StartRefresh = function(self, ...)
        local ok, err = pcall(mikuSrOrig, self, ...)
        end
        n = n - (-1)
        end
        if n ~= 0 then
        end
        return n
        end
        MikuItemSweep = function()
        local c = 0
        for k, v in pairs(package.loaded) do
        if type(v) == [[table]] and type(k) == [[string]] then
        if string.find(k, [[BP_WPItemPoint]], 1, true) or type(v.StartRefresh) == [[function]] then
        if MikuItemPatchTable(v, k) ~= 0 then c = c - (-1) end
        end
        end
        end
        return c
        end
        MikuItemInstances = function(w)
        if w == nil then return 0 end
        local cls = package.loaded[MikuItemClsKey]
        local arr = UE4.TArray(UE4.AActor)
        UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AActor, arr)
        local len = arr:Length()
        local c = 0
        for i = 1, len do
        local a = arr:Get(i)
        local ok, nm = pcall(function() return a:GetName() end)
        if ok and nm ~= nil and string.find(nm, [[WPItemPoint]], 1, true) then
        local okr, before = pcall(function() return a.OnRep_Item end)
        pcall(MikuItemPatchTable, a, nm)
        if okr and before == nil then
        local mt = getmetatable(a)
        local idx = mt and mt.__index
        end
        c = c - (-1)
        end
        end
        return c
        end
        MikuItemLoop = function(w, left)
        if w == nil then return end
        local okw, nmw = pcall(function() return w:GetName() end)
        if not okw or nmw == nil or not string.find(nmw, [[PartyGo_]], 1, true) then return end
        MikuItemSweep()
        local n = MikuItemInstances(w)
        if n ~= 0 and left == 0 then
        end
        if left ~= 0 then UE4.Timer.Add(2, function() MikuItemLoop(w, left - 1) end) end
        end
        MikuItemDo = function()
        if WPInterface ~= nil and WPInterface.LoadMapComplete ~= nil and not MikuItemHook then
        MikuItemHook = true
        local mikuOldComplete = WPInterface.LoadMapComplete
        WPInterface.LoadMapComplete = function(w)
        local r = mikuOldComplete(w)
        if w ~= nil and string.find(w:GetName(), [[PartyGo_]]) then
        MikuItemLoop(w, 15)
        end
        return r
        end
        return true
        end
        return MikuItemHook == true
        end
        MikuItemTry = function(left)
        if MikuItemDo() then return end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuItemTry(left - 1) end) end
        end
        MikuItemReqHook = function()
        if MikuItemReq ~= nil then return end
        MikuItemReq = require
        require = function(name, ...)
        local m, x = MikuItemReq(name, ...)
        if type(m) == [[table]] and type(name) == [[string]] then
        if string.find(name, [[BP_WPItemPoint]], 1, true) or type(m.StartRefresh) == [[function]] then
        pcall(MikuItemPatchTable, m, name)
        end
        end
        return m, x
        end
        end
        MikuItemReqHook()
        MikuItemSweep()
        MikuItemTry(120)
        end
        """;

    /// <summary>
    /// 决战哈姆 host fix: the party level runs with RealMaxPlayerCount = 0, so the challenge mode's
    /// monster scaling is fed a zero strength level.
    ///
    /// Evidence (live listen match on 2026-10-07 03:22, A = uid 4 host, B = uid 5 guest joined):
    /// i.e. gameMode.isAllocWeapon is already true (WPSGameModeWaterPark001.lua:1133-1165
    /// ZBugChallengePreReadyCDOver did run) but RealMaxPlayerCount - the value that same function
    /// passes to ZBugMgr:SetMonsterStrengthLevel(self.RealMaxPlayerCount) - is still 0, while the
    /// user sees monsters at 999/99999 HP instead of the expected 2000 and the host cannot play.
    ///
    /// RealMaxPlayerCount is normally filled from the C++ room-data callback
    /// (WPSGameModeWaterPark001.lua:967-986 OnRspRoomData, which also derives
    /// MaxRobotCount = MaxPlayerCount - RealMaxPlayerCount). This server never answers
    /// ReqRoomStart(2001)/RspRoomStart(2002), so that callback never fires in either single player or
    /// LAN play and the field keeps its zero default.
    ///
    /// Fix: on every PartyGo_ level, tick once per second for 30s and write the real player count
    /// (GameState.PlayerArray, which on the host contains the guest too) into gameMode
    /// .RealMaxPlayerCount, so the value is correct before the 7s pre-ready countdown
    /// (WPSWaterPark001.lua:219-221 sets GamePreReadyCD_ZBugRun = 7) ends. If the property turns out
    /// not to be writable from Lua, the same tick calls ZBugMgr:SetMonsterStrengthLevel(n) directly
    /// </summary>
    public const string ZBugHostFix = """
        if not MikuHostFixV2 then
        MikuHostFixV2 = true
        MikuHostDone = false
        MikuHostReloads = {}
        MikuHostLoop = function(w, left)
        if w == nil then return end
        local okw, nmw = pcall(function() return w:GetName() end)
        if not okw or nmw == nil or not string.find(nmw, [[PartyGo_]], 1, true) then return end
        local lc = WP.GameMgr:GetCharacter(w)
        if lc ~= nil then
        local wl = nil
        pcall(function() wl = lc:GetCurWeapon() end)
        if wl ~= nil then
        local fin = nil
        pcall(function() fin = wl.isClientInitFinish end)
        if fin == false then
        pcall(function() wl.isClientInitFinish = true end)
        local fin2 = nil
        pcall(function() fin2 = wl.isClientInitFinish end)
        end
        -- The listen host is the only netmode where nothing ever fills the magazine.
        -- BP_WPCharacter.lua:2586-2593 runs AnimIns:InitZBugWeaponAsset() and then
        -- self:TryReload() from inside OnRep_PlayerWeapon(), a RepNotify that never fires on the
        -- authority own pawn. So a listen host is handed a weapon with BulletNum = 0 and keeps it
        -- for the whole match - it cannot shoot, while a guest has 45. Measured 2026-10-08 03:19 on
        -- BP_Player002_q_C_2147475596: BP_WP_ChallengeSubmachineGun_C read BulletNum=0, and after one
        -- TryReload() it read 45 and stayed there. TryReload is TIMED - the value only shows up once
        -- the reload animation finishes, so never read BulletNum in the same frame as the call.
        local bar = nil
        pcall(function() bar = wl.BulletNum end)
        if bar ~= nil and bar == 0 and MikuHostReloads[lc] ~= true then
        local okr, errr = pcall(function() lc:TryReload() end)
        if okr then MikuHostReloads[lc] = true end
        end
        end
        end
        local gm = WP.GameMgr:GetGameMode()
        local gs = WP.GameMgr:GetGameState(w)
        if gm ~= nil and gs ~= nil then
        local ps = gs.PlayerArray
        local n = 0
        if ps ~= nil then n = ps:Length() end
        if n ~= 0 then
        local cur = gm.RealMaxPlayerCount
        if cur ~= n then
        pcall(function() gm.RealMaxPlayerCount = n end)
        end
        local st = 0
        pcall(function() st = gs:GetCurState() end)
        if not MikuHostDone and (st == 4 or st == 5) then
        MikuHostDone = true
        local zb = nil
        pcall(function() zb = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w) end)
        if zb == nil then zb = gm.ZBugMgr end
        if zb ~= nil and zb.SetMonsterStrengthLevel ~= nil then
        local okz, errz = pcall(function() zb:SetMonsterStrengthLevel(n) end)
        end
        UE4.Timer.Add(3, function()
        local okw2, nmw2 = pcall(function() return w:GetName() end)
        if not okw2 or nmw2 == nil then return end
        -- Same trap as ZBugCharFix: CheckIsLocallyControlled() is false on a listen host even for
        -- the host own pawn, so this whole block never ran there (measured 2026-10-08 03:12 on
        -- BP_Player002_q_C_2147475596: IsLocallyControlled()=true, CheckIsLocallyControlled()=false).
        -- GetCharacter(w) goes through GetPlayerController(w,0):K2_GetPawn() and works on any netmode.
        local okc2, c = pcall(function() return WP.GameMgr:GetCharacter(w) end)
        if not okc2 then c = nil end
        if c ~= nil then
        local nm = [[?]]
        pcall(function() nm = c:GetName() end)
        local wcur = nil
        pcall(function() wcur = c:GetCurWeapon() end)
        if wcur == nil and gm.isAllocWeapon and zb ~= nil and zb.AllocateWeaponToChar ~= nil then
        local oka, erra = pcall(function() zb:AllocateWeaponToChar(WP.Config.WeaponId.ChallengeSubmachineGun, c) end)
        local wafter = nil
        pcall(function() wafter = c:GetCurWeapon() end)
        end
        end
        end)
        end
        end
        end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHostLoop(w, left - 1) end) end
        end
        MikuHostDo = function()
        if WPInterface ~= nil and WPInterface.LoadMapComplete ~= nil and not MikuHostHook then
        MikuHostHook = true
        local mikuHostOld = WPInterface.LoadMapComplete
        WPInterface.LoadMapComplete = function(w)
        local r = mikuHostOld(w)
        if w ~= nil then
        local okw, nmw = pcall(function() return w:GetName() end)
        if okw and nmw ~= nil and string.find(nmw, [[PartyGo_]], 1, true) then
        MikuHostDone = false
        MikuHostLoop(w, 30)
        end
        end
        return r
        end
        return true
        end
        return MikuHostHook == true
        end
        MikuHostTry = function(left)
        if MikuHostDo() then return end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHostTry(left - 1) end) end
        end
        MikuHostTry(120)
        end
        """;

    /// <summary>
    /// 决战哈姆 listen-host character fix: the host's own player pawn never runs the mode's character
    /// initialisation.
    ///
    /// BP_WPCharacter.lua:133-154 (PostBeginPlay, ZBugChallenge branch) gates it on
    ///     if not self:Cast(UE4.AGEGameMonster) or not self:HasAuthority() then self:InitZBugChar() end
    /// with the comment "真实玩家和怪物客户端在BeginPlay的这里初始化属性" - i.e. the attributes are
    /// initialised on the CLIENT side of every replicated pawn. In a listen-server match the host's own
    /// pawn IS the authority, so both operands are false, InitZBugChar() (a native, undefined anywhere
    /// in the 5007-file Lua tree) never runs for it, and the host plays with an uninitialised character
    /// while the guest - a pure client - is fine. That is exactly the user's report: A (host) cannot
    /// attack, downed model disappears, HP 999/99999 instead of 2000, while B plays normally.
    ///
    /// Fix: on every PartyGo_ level, sweep all AGECharacter actors once per second for 20s and call
    /// InitZBugChar() on the ones that are authoritative, locally controlled (the host's own pawn -
    /// BP_WPCharacter.lua:126 uses the same predicate) and not monsters, in ZBugChallenge levels only.
    /// Each pawn is initialised at most once (MikuCharDone), and the sweep starts at level load, well
    /// </summary>
    public const string ZBugCharFix = """
        if not MikuCharFixV2 then
        MikuCharFixV2 = true
        MikuCharDone = {}
        MikuCharOne = function(p)
        if p == nil or MikuCharDone[p] == true then return false end
        local okm, isMon = pcall(function() return p:Cast(UE4.AGEGameMonster) end)
        if not okm or isMon then return false end
        local oka, auth = pcall(function() return p:HasAuthority() end)
        if not oka or not auth then return false end
        -- Do NOT gate on CheckIsLocallyControlled(): AGEGameCharacter::CheckIsLocallyControlled()
        -- means I am a *client-side* local player, so on a listen host it is false for every
        -- actor in the world - including the host own pawn. Measured on the host during a live
        -- two-machine match 2026-10-08 03:12 (BP_Player002_q_C_2147475596):
        --   IsLocallyControlled()=true, CheckIsLocallyControlled()=false,
        --   GetLocalRole()=3 (ROLE_Authority), GetRemoteRole()=2 (ROLE_AutonomousProxy),
        --   GetController():IsLocalController()=true
        -- That single predicate is why the host own character never got InitZBugChar() while the
        -- guest did (standalone reports true, which is why single player worked). The plain UE
        -- IsLocallyControlled() is true exactly for the pawns this machine drives, on any netmode.
        local okl, loc = pcall(function() return p:IsLocallyControlled() end)
        if not okl or not loc then return false end
        local okw, w = pcall(function() return p:GetWorld() end)
        if not okw or w == nil then return false end
        local gs = WP.GameMgr:GetGameState(w)
        if gs == nil or gs:GetGameType() ~= WP.Config.LevelType.ZBugChallenge then return false end
        MikuCharDone[p] = true
        local ok, err = pcall(function() p:InitZBugChar() end)
        return true
        end
        MikuCharSweep = function(w)
        local c = 0
        -- The local player is the one that matters, and WP.GameMgr:GetCharacter(w) reaches it via
        -- GetPlayerController(w,0):K2_GetPawn(), which keeps working on a listen host. The class
        -- enumeration below is only a fallback for everything else (it does work - measured n=7 -
        -- it was the locality predicate, not the enumeration, that used to reject every actor).
        local okc, lc = pcall(function() return WP.GameMgr:GetCharacter(w) end)
        local locname = [[?]]
        if okc and lc ~= nil then
        pcall(function() locname = lc:GetName() end)
        if MikuCharOne(lc) then c = c - (-1) end
        end
        local arr = UE4.TArray(UE4.AGEGameCharacter)
        UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEGameCharacter, arr)
        local n = arr:Length()
        for i = 1, n do
        if MikuCharOne(arr:Get(i)) then c = c - (-1) end
        end
        return c
        end
        MikuCharLoop = function(w, left)
        if w == nil then return end
        local okw, nmw = pcall(function() return w:GetName() end)
        if not okw or nmw == nil or not string.find(nmw, [[PartyGo_]], 1, true) then return end
        MikuCharSweep(w)
        if left ~= 0 then UE4.Timer.Add(1, function() MikuCharLoop(w, left - 1) end) end
        end
        MikuCharDo = function()
        if WPInterface ~= nil and WPInterface.LoadMapComplete ~= nil and not MikuCharHook then
        MikuCharHook = true
        local mikuCharOld = WPInterface.LoadMapComplete
        WPInterface.LoadMapComplete = function(w)
        local r = mikuCharOld(w)
        if w ~= nil then
        local okw, nmw = pcall(function() return w:GetName() end)
        if okw and nmw ~= nil and string.find(nmw, [[PartyGo_]], 1, true) then
        MikuCharLoop(w, 20)
        end
        end
        return r
        end
        return true
        end
        return MikuCharHook == true
        end
        MikuCharTry = function(left)
        if MikuCharDo() then return end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuCharTry(left - 1) end) end
        end
        MikuCharTry(120)
        end
        """;

    /// <summary>
    /// 进击行动 (online id 106) - and every other level whose GameMode is BP_GameOnline - crashes on
    /// entry with this private server, and the crash is NOT the travel URL: the level, its task graph
    /// and its AIDirector all load, and then the native player spawn aborts.
    ///
    /// Client log 2026-10-07 12:39:56 (crash dump 6cdbcc12-b065-4a18-8d2b-a1fea8e069ea.dmp, whose
    /// ExtraData shows the whole 进击行动 UI path - OnlineAssaultOpsMain / OnlineAssaultOpsBuff /
    /// OnlineAssaultOpsSchemepop / formation / MessageBox / Connection - so this is the official
    /// single-player entry, not a scripted one):
    ///     Browse: /Game/.../AssaultOps_60200?Game=.../BP_GameOnline_C?MapId=60200?CurrentLevelId=60200
    ///             ?FightId=10?TaskPath=.../Multi_AssaultOps_60200?listen
    ///     AGameBaseMode::InitNewPlayer :Team is Empty. UniqueId:4-DummyToken
    ///     LogSpawn: Warning: Login failed: Invalid Character ID! ErrorMsg:Team is Empty.
    ///     LogWindows: Error: === Critical error: ===
    ///     Couldn't spawn player: Invalid Character ID! ErrorMsg:Team is Empty. UniqueId:4-DummyToken
    ///
    /// "Team is Empty" is the native team array AGameBaseMode::InitNewPlayer reads when it spawns the
    /// player. Only UE4.UUMGLibrary.SetTeamCharacters() fills it, and in the whole 5007-file Lua tree
    /// the only callers are Launch/Launch.lua:778 (inside Launch.PreStart, the single-player path),
    /// GM/GMCommandRegister.lua:3627/9631/10435 and the four single-player logic classes
    /// (TowerLogic/GuideLogic/GirlRushLevelLogic/AlienDefenseLogic) - no online or room UI calls it.
    /// On the official service that is fine because the level runs on a dedicated server, which is
    /// handed the room's lineup by the room service; here the "?listen" host IS the server, so the
    /// spawn happens on the player's own machine with no team data. Pushing Launch.PreStart() by hand
    /// through /lua before the ready packet was what made every earlier scripted entry work (the log
    /// then shows AGameCharacter::SetCharacterCardId CharacterCardID = 37/1/43 and no crash).
    ///
    /// The first attempt wrapped only Online.UpdatePlayerState and printed nothing but "hooked"
    /// during a live sortie, so the ready packet did not travel through that wrapper (or the wrapper
    /// ran with a different flag value). This version is defensive on three fronts:
    ///   1. it wraps Online.UpdatePlayerState, Online.ReadyRoom and Online.CraeteRoom, and each wrapper
    ///      sortie actually used (uw_formation_online.lua:636/643 goes through ReadyRoom);
    ///   2. every wrapper pushes the lineup through UE4.UUMGLibrary.SetTeamCharacters before calling
    ///      the original, so the native team array is filled on room creation and on ready alike;
    ///   3. a 5s timer re-pushes the lineup for as long as Launch.GetType() == LaunchType.ONLINE, which
    ///      covers a sortie that never touches any of the three wrapped functions (the native layer can
    ///      send ReqOnlineRoomStart(305) on its own).
    /// Online is a startup global (Preload.lua:102) but the hook still retries for 300s in case the
    /// patch lands before the module is required. All pushes are pcall-guarded and print either
    /// the engine.
    ///
    /// UnLua note: a function value cannot carry fields - marking the wrapper with
    /// "wrapper.MikuWrapped = true" throws "[string ...]: attempt to index a function value (local
    /// 'old')" and aborts the whole patch, so the wrappers are tracked in the MikuTeamFixWrapped side
    /// table instead. That table also detects a clobbered Online.* entry (the client can re-run
    /// Online.lua, which redefines the originals) and re-wraps on the next tick.
    ///
    /// Delivery note: the guard global is versioned (MikuOnlineTeamFixVer) on purpose. A plain
    /// "if not MikuOnlineTeamFix then" guard is useless for a re-push, because the Lua state survives
    /// the reconnect and the flag set by the previous revision silently skips the new body - which is
    /// exactly how a broken v2 stayed invisible in the log. Bump the number whenever the payload
    /// changes.
    ///
    /// v5 (2026-10-08) adds the fourth and last entry point: Map.Open. Measured the same day on
    /// 进击行动 (T13) entered straight from the console with
    /// Map.Open(60200, [[...?TaskPath=...Multi_AssaultOps_60200]], nil, true): the level loaded in
    /// Standalone, AGameBaseMode::InitGame ran, and then the run died with
    /// "AGameBaseMode::InitNewPlayer :Team is Empty. UniqueId:4-DummyToken" ->
    /// "Couldn't spawn player: Invalid Character ID!" -> StaticShutdownAfterError, because a
    /// console/window-driven sortie never touches Online.UpdatePlayerState, Online.ReadyRoom or
    /// Online.CraeteRoom - the three functions v4 wraps. Map.Open is the single choke point every
    /// native and scripted travel goes through (Map/Map.lua:22-29 forwards to
    /// UE4.UMapManager.Open), so pushing the lineup immediately before it covers the console path,
    /// the T13 chapter button and every future scripted entry alike.
    /// </summary>
    public const string OnlineTeamFix = """
        if MikuOnlineTeamFixVer ~= 5 then
        MikuOnlineTeamFixVer = 5
        MikuTeamFixPush = function(tag)
        local ok, err = pcall(function()
        local tbteam = Formation.GetCurrentLineup()
        if tbteam == nil then
        return
        end
        UE4.UUMGLibrary.SetTeamCharacters(tbteam:GetCards())
        end)
        end
        MikuTeamFixWrapped = {}
        MikuTeamFixWrap = function(name)
        if Online == nil then return false end
        local old = Online[name]
        if old == nil then return false end
        if MikuTeamFixWrapped[name] == old then return true end
        local wrapper = function(a, b, c)
        MikuTeamFixPush(name)
        return old(a, b, c)
        end
        MikuTeamFixWrapped[name] = wrapper
        Online[name] = wrapper
        return true
        end
        MikuTeamFixMapWrap = function()
        if Map == nil or Map.Open == nil then return false end
        if MikuTeamFixWrapped.MapOpen == Map.Open then return true end
        local oldMap = Map.Open
        local wrapper = function(a, b, c, d)
        MikuTeamFixPush([[mapopen]])
        return oldMap(a, b, c, d)
        end
        MikuTeamFixWrapped.MapOpen = wrapper
        Map.Open = wrapper
        return true
        end
        MikuTeamFixTick = function()
        local delay = 5
        MikuTeamFixMapWrap()
        if Online ~= nil then
        MikuTeamFixWrap([[UpdatePlayerState]])
        MikuTeamFixWrap([[ReadyRoom]])
        MikuTeamFixWrap([[CraeteRoom]])
        local isOnline = false
        if Launch ~= nil and Launch.GetType ~= nil and LaunchType ~= nil then
        isOnline = Launch.GetType() == LaunchType.ONLINE
        end
        if isOnline then
        MikuTeamFixPush([[loop]])
        delay = 1
        end
        end
        UE4.Timer.Add(delay, MikuTeamFixTick)
        end
        MikuTeamFixDo = function()
        MikuTeamFixMapWrap()
        if Online == nil then return false end
        if MikuTeamFixWrap([[UpdatePlayerState]]) == false then return false end
        MikuTeamFixWrap([[ReadyRoom]])
        MikuTeamFixWrap([[CraeteRoom]])
        if not MikuTeamFixLoop then
        MikuTeamFixLoop = true
        UE4.Timer.Add(1, MikuTeamFixTick)
        end
        return true
        end
        MikuTeamFixTry = function(left)
        if MikuTeamFixDo() then return end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuTeamFixTry(left - 1) end) end
        end
        MikuTeamFixTry(300)
        end
        """;

    /// <summary>
    /// Client-driven re-entry for online levels whose native travel leaves UMapManager's map id stale
    /// (v1, 2026-10-08).
    ///
    /// Root cause, measured on 进击行动 / T13 the same day: an online sortie travels natively
    /// (NtfOnlineStart -&gt; LogNet: Browse: .../AssaultOps_60200?Game=...), and
    /// UMapManager::PostLoadLogic resolves the new map id from the name of the loaded UWorld. The T13
    /// world object is named "Assaultops_60200" (lower-case o) while the client's own map table spells
    /// the package "AssaultOps_60200" (map.txt:1599), so a case-sensitive lookup misses and the manager
    /// keeps the id of the map that was loaded before. Same session, both entry paths:
    ///   party  UMapManager::PostLoadLogic World [0] [9633] [PartyGo_9633]     - found
    ///   T13    UMapManager::PostLoadLogic World [0] [2]    [Assaultops_60200]  - stale lobby id
    /// Everything downstream then uses the wrong id: Map.Class(GetLuaClass()) runs the LOBBY class
    /// inside the level, Map.IsFightMap(2) is false so OnEnterFight / PreRelease.EnterFight /
    /// FightLevel:OnEntryMap are skipped, Map.Leave reports 2 and - the visible bug - GoToMainLevel()'s
    /// Map.Open(2) is refused by Map.IsCanOpen(2), because the stale id already IS 2. 放弃 / 退出
    /// therefore does nothing at all and the client stays in the level until the session aborts.
    ///
    /// Fix: the only Lua-visible write path to that id is UE4.UMapManager.Open, reached through
    /// Map/Map.lua:22-29, so this patch wraps Map.Enter and - when the level that just loaded really is
    /// the online level (current ULevel name equals the online level's map-table leaf, case-insensitive)
    /// while the id disagrees - re-opens the level with the correct id one second later. The second load
    /// is cheap (the package is already in memory) and reproduces the state of a scripted Map.Open
    /// entry, which is the one verified-working entry path (defend 7106). MikuOnlineLevelOptions,
    /// pushed by OnlineRoomService.PushLaunchState, carries the same URL options the native travel used,
    /// so the TaskPath still reaches AGameTaskActor.
    ///
    /// The guard is cleared whenever the current level is NOT the online level, so the next sortie
    /// re-enters again. UnLua note: Map.Enter is a table field, not a function value, so a plain field
    /// guard is safe here (unlike the OnlineTeamFix wrappers), and the 5s tick re-hooks the wrapper
    /// after a script reload.
    ///
    /// </summary>
    public const string OnlineMapFix = """
        if MikuOnlineMapFixVer ~= 1 then
        MikuOnlineMapFixVer = 1
        MikuMapFixLeaf = function(sPath)
        if sPath == nil then return nil end
        local sName = sPath
        for i = 1, 12 do
        local sHead, sTail = string.match(sName, [[([^/]*)/(.*)]])
        if sTail == nil then break end
        sName = sTail
        end
        return string.lower(sName)
        end
        MikuMapFixWorld = function()
        local ok, sName = pcall(UE4.UGameplayStatics.GetCurrentLevelName, GetGameIns())
        if ok and sName ~= nil then return string.lower(sName) end
        return nil
        end
        MikuMapFixWant = function()
        if Launch == nil or LaunchType == nil or Online == nil then return 0 end
        if Launch.GetType() ~= LaunchType.ONLINE then return 0 end
        if Online.GetOnlineLevelId == nil then return 0 end
        local ok, nId = pcall(Online.GetOnlineLevelId)
        if ok and nId ~= nil and nId ~= 0 then return nId end
        return 0
        end
        MikuMapFixIsOnlineLevel = function(nWant)
        local ok, bFind, mapInfo = pcall(UE4.UMapManager.Find, nWant)
        if not ok or not bFind or mapInfo == nil then return false end
        local sWant = MikuMapFixLeaf(mapInfo.MapPath)
        local sNow = MikuMapFixWorld()
        if sWant == nil or sNow == nil then return false end
        return sWant == sNow
        end
        MikuMapFixEnter = function(nMapID)
        local nWant = MikuMapFixWant()
        if nWant ~= 0 and nWant ~= nMapID then
        if MikuMapFixIsOnlineLevel(nWant) then
        if MikuMapFixDone ~= nWant then
        MikuMapFixDone = nWant
        local sOption = MikuOnlineLevelOptions or [[]]
        UE4.Timer.Add(1, function()
        Map.Open(nWant, sOption, nil, true)
        end)
        return
        end
        else
        MikuMapFixDone = nil
        end
        end
        if MikuMapFixOldEnter ~= nil then return MikuMapFixOldEnter(nMapID) end
        end
        MikuMapFixHook = function()
        if Map == nil or Map.Enter == nil then return false end
        if Map.Enter == MikuMapFixEnter then return true end
        MikuMapFixOldEnter = Map.Enter
        Map.Enter = MikuMapFixEnter
        return true
        end
        MikuMapFixTick = function()
        MikuMapFixHook()
        UE4.Timer.Add(5, MikuMapFixTick)
        end
        MikuMapFixHook()
        MikuMapFixTick()
        end
        """;

    /// <summary>
    /// Level-end settlement for online levels (v1, 2026-10-08).
    ///
    /// The gap: Launch.PreStart registers the Event.OnLevelFinish handler that collapses the Fight UI
    /// and calls Launch.Class(Launch.GetType()):OnSettlement(...) (Launch.lua:787-801), but the ONLINE
    /// launch class (Launch/Classes/OnlineLogic.lua, 13 lines) defines only OnEnd, so it inherits the
    /// empty tbTemplateLogic.OnSettlement (Launch.lua:740-759) and the level end opens nothing at all.
    /// The client-side branch that would normally open a panel is dead here too: BP_TaskActor.lua:410
    /// gates "UI.Open(Success/Settlement/Fail)" behind "not self:HasAuthority()", and in this
    /// standalone/listen-host world the local player IS the authority. The real service instead has the
    /// dedicated server call Online.Settlement (Online.lua:3045) and C++ open the panel through
    /// BP_PlayerStage:ReceiveSettlement - neither of which exists in this deployment. Observed
    /// 2026-10-08 05:39: the level ended with "AGameTaskActor::LevelFinishBroadCast 结果:0 失败原因:1",
    /// nothing happened for two minutes, and the session died with "Match State Changed from LeavingMap
    /// to Aborted" / "Engine exit requested (reason: Stop Game!!! Aborted)".
    ///
    /// Fix: give the ONLINE launch class an OnSettlement that opens the panel the mode itself names -
    /// OnlineAssaultOps.GetSettlementUIName() returns "OnlineAssaultOpsSettlement" for both the success
    /// and the fail case (OnlineAssaultOps.lua:142-149). The panel is fully client-local (it reads the
    /// local player state and the ForbidZone skill manager, and bails out through DoEnd() when that
    /// manager is missing), so no server payload is needed; its 退出 button calls Launch.End(), which is
    /// what actually returns the player to the lobby once OnlineMapFix has repaired the map id.
    ///
    /// </summary>
    public const string OnlineSettleFix = """
        if MikuOnlineSettleFixVer ~= 1 then
        MikuOnlineSettleFixVer = 1
        MikuSettleFixOpen = function(self, nResult, nTime, nReason)
        local ok, sUI = pcall(Online.GetSettlementUIString)
        if not ok or sUI == nil then sUI = [[OnlineSettlement]] end
        UI.Open(sUI)
        end
        MikuSettleFixApply = function()
        if Launch == nil or LaunchType == nil or Launch.Class == nil then return false end
        local tbClass = Launch.Class(LaunchType.ONLINE)
        if tbClass == nil then return false end
        if tbClass.OnSettlement == MikuSettleFixOpen then return true end
        tbClass.OnSettlement = MikuSettleFixOpen
        return true
        end
        MikuSettleFixTick = function()
        MikuSettleFixApply()
        UE4.Timer.Add(5, MikuSettleFixTick)
        end
        MikuSettleFixApply()
        MikuSettleFixTick()
        end
        """;

    /// <summary>
    /// Listen host treated as standalone (v1, 2026-10-08). EXPERIMENTAL.
    ///
    /// Root cause being tested: the shipped game only ever wrote TWO world states - standalone and
    /// dedicated-server-plus-remote-clients. A "?listen" host is neither. Every gate is spelled
    /// either
    ///   if not self:HasAuthority() or WP.GameMgr:WorldIIsStandalone(world) then  -- client side
    /// or
    ///   if self:HasAuthority() and not WP.GameMgr:WorldIIsStandalone(world) then -- authority side
    /// so on a listen host the first form is false (authority is true, IsStandalone is false) and the
    /// whole client-side initialisation block is skipped for the host's OWN pawn.
    ///
    /// BP_WPCharacter.lua:62-65 is the clearest instance:
    ///   if not self:HasAuthority() or WP.GameMgr:WorldIIsStandalone(self:GetWorld()) then
    ///       WP.EventMgr:AddEventListener(WP.EventKeys.HAS_RepMeshFroup, self, ...)
    ///       WP.EventMgr:SendEvent(WP.EventKeys.PlayerClientBeginPlay, self)
    ///   end
    /// PlayerClientBeginPlay is never sent by the host, so everything hanging off it never runs.
    ///
    /// WPGameMgr.lua:44-47 is a PURE LUA function (UKismetSystemLibrary.IsStandalone is only called
    /// inside it), and .temp/MikuSB/Proto-era probes confirmed the C++-bound UE4 class tables cannot
    /// be overridden (see the CheckIsLocallyControlled notes in ZBugCharFix). So the helper is the
    /// one place where this state can be corrected from Lua: wrap it so that
    ///   IsServer(world) and not IsDedicatedServer(world)
    /// - i.e. exactly a listen host - reports standalone. Standalone worlds and pure guests are
    /// unaffected, and a real dedicated server still reports false.
    ///
    /// Rationale for mapping the unknown state onto the state the game DID write, rather than
    /// teaching the game a third state: all 46 WorldIIsStandalone call sites, plus the three
    /// self.IsStandalone fields (BP_WPCharacter.lua:31, BP_WPPlayerState.lua:11,
    /// BP_WPGameState.lua:10), then behave exactly as they already do in a verified-working
    /// standalone match.
    ///
    /// Scope limit, measured by grep over the 5007-file script tree: about 30 further call sites use
    /// UE4.UKismetSystemLibrary.IsStandalone/IsServer/IsDedicatedServer DIRECTLY and are NOT covered
    /// by this wrapper.
    ///
    /// </summary>
    public const string ZBugStandaloneFix = """
        if MikuStandaloneFixVer ~= 1 then
        MikuStandaloneFixVer = 1
        MikuSACount = 0
        MikuSALib = function()
        if UE4 ~= nil and UE4.UKismetSystemLibrary ~= nil then return UE4.UKismetSystemLibrary end
        if UE ~= nil and UE.UKismetSystemLibrary ~= nil then return UE.UKismetSystemLibrary end
        return nil
        end
        MikuSAIsListenHost = function(world)
        local lib = MikuSALib()
        if lib == nil then return false end
        local okS, isServer = pcall(function() return lib.IsServer(world) end)
        if not okS or not isServer then return false end
        local okD, isDS = pcall(function() return lib.IsDedicatedServer(world) end)
        if not okD or isDS then return false end
        return true
        end
        MikuSADo = function()
        if WP == nil or WP.GameMgr == nil then return false end
        local old = WP.GameMgr.WorldIIsStandalone
        if old == nil then return false end
        if MikuSAWrapped == old then return true end
        local wrapper = function(self, world)
        local r = old(self, world)
        if r then return true end
        if not MikuSAIsListenHost(world) then return r end
        MikuSACount = MikuSACount - (-1)
        return true
        end
        MikuSAWrapped = wrapper
        WP.GameMgr.WorldIIsStandalone = wrapper
        return true
        end
        MikuSATry = function(left)
        if MikuSADo() then return end
        if left ~= 0 then UE4.Timer.Add(1, function() MikuSATry(left - 1) end) end
        end
        MikuSATry(300)
        MikuSAStat = function()
        UE4.Timer.Add(15, MikuSAStat)
        end
        UE4.Timer.Add(15, MikuSAStat)
        MikuSAPawn = function()
        local ok, err = pcall(function()
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        local c = WP.GameMgr:GetCharacter(w)
        if c == nil then return end
        local wp = c:GetCurWeapon()
        local bar = -1
        local fin = false
        if wp ~= nil then
        bar = wp.BulletNum
        fin = wp.isClientInitFinish
        end
        end)
        end
        UE4.Timer.Add(5, MikuSAPawn)
        UE4.Timer.Add(20, MikuSAPawn)
        UE4.Timer.Add(45, MikuSAPawn)
        end
        """;

    /// <summary>
    /// 决战哈姆 listen-host weapon init (2026-10-08). Root cause of "host cannot shoot / lost most
    /// VFX" while the guest plays fine: the client-side weapon initialisation is a REPLICATION
    /// callback and a replica-only code path, so the listen host - which is the authority and
    /// therefore never receives the property - skips all of it.
    ///
    /// Three independent confirmations that this path is client-only:
    ///  - BP_WPCharacter.lua:2559 OnRep_PlayerWeapon() is the weapon's client init. It calls
    ///    self:StopAttack(), StopMontageByName([[shot]]), OnWeaponAttached(), ForceFinishReload(),
    ///    AnimIns:InitZBugWeaponAsset(), and finally
    ///    BP_WPCharacter.lua:2592: if CurWeapon and CurWeapon.isClientInitFinish and
    ///    CurWeapon.BulletNum == 0 and not CurWeapon.isDestroyWhenNoBullet then self:TryReload() end
    ///    so the magazine is only ever filled from this callback.
    ///  - BP_WPCharacter.lua:3081 OnPsRepTeamID() just calls self:OnRep_PlayerWeapon(). TeamID is a
    ///    RepNotify on the player state, so this only ever fires on a replica.
    ///  - BP_WPZBugRunMgr.lua:382 LuaImpl_TickCheckInitForPlayer() runs its
    ///    Actor:CheckInitFinish() drain loop behind "if not self:HasAuthority()" (:387), and
    ///    CheckInitFinish ends at :500-505 with pawn:OnRep_PlayerWeapon(). The authority is excluded
    ///    by construction.
    ///
    /// Measured symptom that matches: the host probe reports
    /// 2026-10-07 probe found that granting bullets to the host directly made it shoot again.
    ///
    /// The patch replays, on the listen host only, the exact calls the client makes: one
    /// OnRep_PlayerWeapon(), then Actor:CheckInitFinish() for the UI / item / skill parts, retried
    /// every 3s up to 20 times. Everything stops as soon as wp.BulletNum is non-zero, so a working
    /// fix costs the host a single StopAttack/ForceFinishReload hiccup at match start. Standalone
    /// worlds, pure guests and real dedicated servers are all no-ops.
    ///
    /// chk n= ok= r= bar= fin= / ok1 ok2 ok3 / done / nomgr / err.
    ///
    /// v1 shipped at 10:16 and immediately exposed two probe bugs, both fixed in v2:
    ///  - WP.GameMgr:GetCharacter(world) is NOT nil outside a match - in the lobby it returns a
    ///    non-character object, so c:GetCurWeapon() raised
    ///    "attempt to call a nil value (method 'GetCurWeapon')" once per second. v2 guards with
    ///    "if c.GetCurWeapon == nil then return end".
    ///  - IsServer/IsDedicatedServer alone also match the lobby world of a listen-host process, so
    ///    the world check now additionally requires "not IsStandalone(world)".
    /// v1's timer chain keeps firing for its remaining ticks and resolves MikuHITick through the
    /// global, so v2 redefines MikuHITick as a no-op (killing the old chain on its next tick) and
    /// drives its own chain through a fresh name, MikuHIV2Tick.
    ///
    /// v3 result (2026-10-08 10:43:42, the first listen match with a guest in the room): the single
    /// for the first time on a listen host, and UWaterParkQAnimIns stopped reporting isInitFinish? 0
    /// for the local player. But v3 stopped watching after that ("done"), so it cannot tell a
    /// working weapon from one that merely has ammo, and it cannot tell whether the state decays.
    ///
    /// v4 therefore never finishes: it samples once a second for the whole match and prints a
    /// is the direct proof that the host can actually fire, which no earlier probe could show.
    /// It also self-heals: three consecutive seconds at BulletNum == 0 (never one, so a normal
    /// reload is left alone - OnRep_PlayerWeapon calls StopAttack and ForceFinishReload) triggers
    ///
    /// v5 (the actual root cause, found on 2026-10-08 with the live /lua channel): every block that
    /// initialises the local player and refreshes its UI sits behind
    /// "if self:CheckIsLocallyControlled() then", and that game-specific helper returns FALSE for the
    /// listen host own pawn, while the engine IsLocallyControlled() says true for the same pawn.
    /// RepNotify handlers additionally never fire on the authority. Both together explain every host
    /// symptom: no ammo (OnRep_PlayerWeapon owns the only TryReload), "0 enemies left" (the manager
    /// really does hold the correct total, the UI never hears about it), the placeholder 999/99999
    /// health (LuaImpl_ClientOnLocalHealthChange is a client hook, and BP_WPZBugRunMgr passes
    /// "curHealth" with a lower-case c while the widget only matches "CurHealth"), and the missing
    /// effects and buff offers (OnRep_CacheLevelUpBuffs and friends).
    ///
    /// The UnLua instance accepts a Lua field write over the UFunction, so the gate is opened by
    /// assigning the pawn field CheckIsLocallyControlled. After that the patch replays what the
    /// authority never receives: the game own mgr:CheckInitFinish(pawn) pump until it reports done,
    /// the pure-display wave RepNotifies only when their value actually changes, OnRep_PlayerWeapon
    /// only when the weapon object identity changes (it calls StopAttack and ForceFinishReload, so a
    /// per-second call would break firing), OnRep_CacheLevelUpBuffs only when the offered buff count
    /// changes, and the health widget directly through the correctly spelled CurHealth key.
    /// OnRep_CurWaveState is deliberately NOT driven: it switches the BGM, plays audio and arms a
    /// refresh timer, so replaying it on a timer would corrupt the match.
    /// </summary>
    public const string ZBugHostInitFix = """
        if MikuLocalVer ~= 1 then
        MikuLocalVer = 1
        MikuHITick = function() return end
        MikuHIV2Tick = function() return end
        MikuHI4Tick = function() return end
        MikuLCPawn = nil
        MikuLCDone = false
        MikuLCLastWp = nil
        MikuLCLastBuffs = -1
        MikuLCLastWave = -1
        MikuLCLastKill = -1
        MikuLCLastBonus = -1
        MikuLCLastCWave = -1
        MikuLCLastHp = -1
        MikuLCLastMaxHp = -1
        MikuLCLib = function()
        if UE4 ~= nil and UE4.UKismetSystemLibrary ~= nil then return UE4.UKismetSystemLibrary end
        if UE ~= nil and UE.UKismetSystemLibrary ~= nil then return UE.UKismetSystemLibrary end
        return nil
        end
        MikuLCIsListenHost = function(world)
        local lib = MikuLCLib()
        if lib == nil then return false end
        local okS, isServer = pcall(function() return lib.IsServer(world) end)
        if not okS or not isServer then return false end
        local okD, isDS = pcall(function() return lib.IsDedicatedServer(world) end)
        if not okD or isDS then return false end
        local okA, isSA = pcall(function() return lib.IsStandalone(world) end)
        if not okA or isSA then return false end
        return true
        end
        MikuLCLocalTrue = function() return true end
        MikuLCStage = function(tick)
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        if not MikuLCIsListenHost(w) then return end
        local c = WP.GameMgr:GetCharacter(w)
        if c == nil then return end
        if c.GetCurWeapon == nil then return end
        if c.CheckIsLocallyControlled ~= MikuLCLocalTrue then
        c.CheckIsLocallyControlled = MikuLCLocalTrue
        end
        if c ~= MikuLCPawn then
        MikuLCPawn = c
        MikuLCDone = false
        MikuLCLastWp = nil
        MikuLCLastBuffs = -1
        MikuLCLastWave = -1
        MikuLCLastKill = -1
        MikuLCLastBonus = -1
        MikuLCLastCWave = -1
        MikuLCLastHp = -1
        MikuLCLastMaxHp = -1
        end
        local mgr = nil
        local library = UE4.UGEZBugRunLibrary
        if library ~= nil then
        local okG, g = pcall(function() return library.GetZBugRunMgr(w) end)
        if okG then mgr = g end
        end
        if mgr ~= nil then
        if not MikuLCDone then
        local okC, res = pcall(function() return mgr:CheckInitFinish(c) end)
        if okC and res then
        MikuLCDone = true
        end
        end
        if mgr.CurWaveTotalKill ~= MikuLCLastWave then
        MikuLCLastWave = mgr.CurWaveTotalKill
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        if mgr.CurWaveKillCount ~= MikuLCLastKill then
        MikuLCLastKill = mgr.CurWaveKillCount
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        if mgr.CurChallengeWave ~= MikuLCLastCWave then
        MikuLCLastCWave = mgr.CurChallengeWave
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        if mgr.BonusWaveTimer ~= MikuLCLastBonus then
        MikuLCLastBonus = mgr.BonusWaveTimer
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local wp = nil
        local okW, vW = pcall(function() return c:GetCurWeapon() end)
        if okW then wp = vW end
        if wp ~= nil and wp ~= MikuLCLastWp then
        MikuLCLastWp = wp
        pcall(function() c:OnRep_PlayerWeapon() end)
        end
        local okB, buffs = pcall(function() return c.CacheLevelUpBuffs:Num() end)
        if okB and buffs ~= nil then
        if buffs ~= MikuLCLastBuffs then
        if buffs ~= -1 then
        pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        end
        MikuLCLastBuffs = buffs
        end
        end
        local ui = UI.GetUI([[PartyZBugRunFight]])
        if ui ~= nil then
        local okM, mx = pcall(function() return c:GetAttributeCurValByName([[Health]]) end)
        if okM and mx ~= nil then
        mx = math.floor(mx)
        if mx ~= MikuLCLastMaxHp then
        MikuLCLastMaxHp = mx
        pcall(function() ui:OnLocalCharHPChange([[Health]], mx) end)
        end
        end
        local okH, hp = pcall(function() return c:GetAttributeCurValByName([[CurHealth]]) end)
        if okH and hp ~= nil then
        hp = math.floor(hp)
        if hp ~= MikuLCLastHp then
        MikuLCLastHp = hp
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], hp) end)
        end
        end
        end
        if wp ~= nil and tick % 60 == 0 then
        end
        end
        MikuLCTick = function(left)
        if left == 0 then return end
        local ok, err = pcall(MikuLCStage, left)
        UE4.Timer.Add(0.5, function() MikuLCTick(left - 1) end)
        end
        MikuLCTick(3600)
        end
        """;

    /// <summary>
    /// WaterPark's EventMgr keeps its listener list in a Lua singleton (WP.EventMgr) that survives
    /// level changes, while the logic-tree nodes it points at do not get cleaned up: leaving a match
    /// releases LT_WP_KillMonsterBySpawnNotify without ever running its BP_TaskNode_Finish, so its
    /// listener on EventKeys.ZBugChallenge_NotifySpawnCountToKillNode (809) leaks into the next match.
    ///
    /// EventMgr:SendEvent walks that list and calls v.Fun(v.Obj, ...) unguarded (WPEventMgr.lua:75),
    /// so the released node raises "attempt to read property 'KillTag' on released object" from
    /// CheckAddKillTargetCondition (LT_WP_KillMonsterBySpawnNotify.lua:59) INSIDE the delivery loop -
    /// which aborts the loop. The live node for the new match therefore never receives the
    /// spawn-count notification, its KillTotalCount stays 0, and CheckFinish() (KillNum >=
    /// KillTotalCount) is trivially true on the first kill, so the node calls FinishTaskFromBP(true)
    /// and the wave is declared cleared. That is the 剩余敌人 shows 0 / one kill clears the wave /
    /// the next wave spawns early family, and SetCurWaveTotalKill is never told the real total.
    ///
    /// Observed live on a listen host, 2026-10-08 11:02: WP.EventMgr.Events[809] held two entries -
    /// slot 1 released (GetName fails with invalid object) but still carrying KillTotalCount=5 from
    /// the previous match, slot 2 the live LT_WP_KillMonsterBySpawnNotify_C_0 with KillTotalCount=0.
    /// Sending 809 by hand reproduced the error and then, with this wrapper in place, purged slot 1
    /// and left n=1.
    ///
    /// The wrapper pcalls every callback and drops the entries that throw, so one stale listener can
    /// no longer silence the rest of the list. Listeners that fail once are removed rather than kept,
    /// which also stops the leaked node from being called again every wave.
    /// </summary>
    public const string WPEventGuard = """
        if MikuEvGuardVer ~= 1 then
        MikuEvGuardVer = 1
        MikuEvWrap = function(self, key, ...)
        local cb = self.Events[key]
        if cb == nil then return end
        local dead = nil
        for k, v in pairs(cb) do
        if v == nil or v.Obj == nil or v.Fun == nil then
        dead = dead or {}
        table.insert(dead, k)
        else
        local okc, errc = pcall(v.Fun, v.Obj, ...)
        if not okc then
        dead = dead or {}
        table.insert(dead, k)
        end
        end
        end
        if dead ~= nil then
        for i = #dead, 1, -1 do table.remove(cb, dead[i]) end
        end
        end
        MikuEvWatch = function(left)
        if left == 0 then return end
        if WP ~= nil and WP.EventMgr ~= nil then
        if WP.EventMgr.SendEvent ~= MikuEvWrap then
        WP.EventMgr.SendEvent = MikuEvWrap
        end
        end
        UE4.Timer.Add(10, function() MikuEvWatch(left - 1) end)
        end
        MikuEvBoot = function(left)
        if WP == nil or WP.EventMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuEvBoot(left - 1) end) end
        return
        end
        WP.EventMgr.SendEvent = MikuEvWrap
        MikuEvWatch(2160)
        end
        MikuEvBoot(600)
        end
        """;

    /// <summary>
    /// 决战哈姆 / 冲关啦 listen host, part two: the clock and the trigger.
    ///
    /// ZBugHostInitFix (v5) opened the gate for every LUA caller by writing CheckIsLocallyControlled
    /// on the pawn instance. That cannot reach the BP and C++ callers: UnLua only wires a Lua function
    /// over a UFunction when the name is present in the Lua class table at the moment the class is
    /// first bound, so an instance field is invisible to anything that is not Lua. Every gate the game
    /// itself evaluates inside a BP graph therefore still answered false on the host - the fire input
    /// handler, TryAttack, the wave and score bookkeeping.
    ///
    /// This patch hooks the UFunction properly: it requires the character Lua module at login, i.e.
    /// before the first pawn of the match is ever bound, and puts CheckIsLocallyControlled on the class
    /// table. The replacement asks the original implementation first and only widens the answer on a
    /// listen host, where the engine already reports IsLocallyControlled true for the host pawn but the
    /// game wrapper says false. Clients keep the exact result they had.
    ///
    /// It also replays two things the host never receives, because both sources are RepNotify
    /// properties whose handler is skipped locally:
    ///   - ZBugChallengeCountDown to GameState_StartGameCD_MuliCast (id 104). In ZBugChallenge the
    ///     GameMode one second tick only handles the pre-ready countdown, so the HUD clock is fed by
    ///     that RepNotify alone and stays frozen at 02:00 for the whole match.
    ///   - ChallengeScore to OnRep_ChallengeScore, so the settle score line updates.
    ///
    /// Finally the host gets a PartyBtnPress bridge: the fire, reload and skill presses DO reach Lua on
    /// the host (unlike PCKeyboardEvent, which never carries the mouse), so the button events drive the
    /// same calls the mobile buttons make.
    /// </summary>
    public const string ZBugHostPlayFix = """
        if MikuHPVer ~= 1 then
        MikuHPVer = 1
        MikuHPNum = function(v)
        if type(v) ~= [[number]] then return -999999 end
        return math.floor(v)
        end
        MikuHPWorld = function(self)
        local ok, w = pcall(function() return self:GetWorld() end)
        if ok then return w end
        return nil
        end
        MikuHPIsListenHost = function(w)
        if w == nil then return false end
        local ok, v = pcall(function() return IsServer(w) and not IsDedicatedServer(w) and not IsStandalone(w) end)
        if ok and v == true then return true end
        return false
        end
        MikuHPEngineLocal = function(self)
        local ok, v = pcall(function() return self:IsLocallyControlled() end)
        if ok and v == true then return true end
        return false
        end
        MikuHPOwnedByLocal = function(self)
        local w = MikuHPWorld(self)
        if w == nil then return false end
        local ok, pc = pcall(function() return WP.GameMgr:GetMyPlayerController(w) end)
        if not ok or pc == nil then return false end
        if self == pc then return true end
        local okp, ps = pcall(function() return pc.PlayerState end)
        if okp and ps ~= nil and self == ps then return true end
        local okc, ch = pcall(function() return pc.Character end)
        if okc and ch ~= nil and self == ch then return true end
        local oko, ow = pcall(function() return self:GetOwner() end)
        if oko and ow ~= nil and ow == pc then return true end
        return false
        end
        MikuHPDepth = 0
        MikuHPCnt = 0
        MikuHPCILC = function(self)
        MikuHPCnt = MikuHPCnt - (-1)
        local eng = MikuHPEngineLocal(self)
        if MikuHPCnt == 1 or MikuHPCnt % 200 == 0 then
        end
        if MikuHPDepth ~= 0 then return eng end
        MikuHPDepth = 1
        local ok, v = pcall(function() return self.Overridden.CheckIsLocallyControlled(self) end)
        MikuHPDepth = 0
        if ok and v == true then return true end
        if eng or MikuHPOwnedByLocal(self) then
        if MikuHPIsListenHost(MikuHPWorld(self)) then return true end
        end
        if ok then return v end
        return eng
        end
        MikuHPPatch = function(name)
        local ok, cls = pcall(require, name)
        if not ok then
        return
        end
        if type(cls) ~= [[table]] then return end
        if cls.CheckIsLocallyControlled == MikuHPCILC then return end
        cls.CheckIsLocallyControlled = MikuHPCILC
        end
        MikuHPLastPawn = nil
        MikuHPLastCd = -1
        MikuHPLastScore = -1
        MikuHPChar = function()
        local w = WP.GameMgr:GetWorld()
        return WP.GameMgr:GetCharacter(w)
        end
        MikuHPStage = function()
        local w = WP.GameMgr:GetWorld()
        local c = WP.GameMgr:GetCharacter(w)
        if c ~= nil and c ~= MikuHPLastPawn then
        MikuHPLastPawn = c
        end
        local mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w)
        if mgr ~= nil then
        local cd = MikuHPNum(mgr.ZBugChallengeCountDown)
        if cd ~= MikuHPLastCd then
        MikuHPLastCd = cd
        WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, mgr.ZBugChallengeCountDown)
        end
        end
        local pc = WP.GameMgr:GetMyPlayerController(w)
        if pc ~= nil then
        local ps = pc.PlayerState
        if ps ~= nil then
        local sc = MikuHPNum(ps.ChallengeScore)
        if sc ~= MikuHPLastScore then
        MikuHPLastScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        end
        end
        MikuHPFire = function(on)
        local c = MikuHPChar()
        if c == nil then return end
        if on then
        if c.Controller ~= nil then c.Controller.CacheFireInput = true end
        pcall(function() c:TryAttack() end)
        else
        if c.Controller ~= nil then c.Controller.CacheFireInput = false end
        pcall(function() c:StopAttack() end)
        end
        end
        MikuHPBridge = function(t, p)
        if t == 5 then
        MikuHPFire(p)
        elseif t == 6 then
        local c = MikuHPChar()
        if c ~= nil then
        pcall(function() c:TryReload() end)
        end
        elseif t == 1 or t == 2 or t == 3 then
        local c = MikuHPChar()
        if c ~= nil then
        pcall(function() c:TryUseSkill(t - 1) end)
        end
        end
        end
        MikuHPBridgeOn = function()
        local ok = pcall(function() EventSystem.On(Event.PartyBtnPress, MikuHPBridge) end)
        end
        MikuHPTick = function(left)
        if left == 0 then return end
        pcall(MikuHPStage)
        UE4.Timer.Add(0.5, function() MikuHPTick(left - 1) end)
        end
        MikuHPBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHPBoot(left - 1) end) end
        return
        end
        MikuHPPatch([[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPBridgeOn)
        MikuHPTick(3600)
        end
        MikuHPBoot(600)
        end
        """;

    /// <summary>
    /// v7 ZBugHostViewFix - listen 主机的客户端表现层与 HUD 供给。
    ///
    /// 根因（2026-10-08 18:00 静态取证）：游戏把"客户端表现"与"本地玩家 HUD 初始化"都写成
    /// 共同判断 `if self:HasAuthority() and not self.IsStandalone then return end`（或
    /// `if not self:HasAuthority() or self.IsStandalone then ... end`）。listen 主机既是权威端
    /// 又是本地玩家：HasAuthority() 为 true 而 IsStandalone 为 false，于是这些分支整块被跳过：
    ///   BP_WPCharacter.lua:1009  PlayEffectByIdNew            所有 Multicast 特效（怪物挥爪/命中）
    ///   BP_WPCharacter.lua:2436  OnGameStateChange(Progress)  客户端侧镜头/UI 初始化
    ///   BP_WPCharacter.lua:2529  ZBugRun_Restart_RPC          重生 UI
    ///   BP_WPCharacter.lua:2679  LuaImpl_LevelUpBuff          单机才自补 OnRep_CacheLevelUpBuffs
    ///   BP_WPCharacter.lua:2803  LuaImpl_GetDamage_MultiCast   命中特效/受击提示
    ///   BP_WPCharacter.lua:3147  LuaImpl_SmokeBomb_Multicast  烟雾弹表现
    ///   BP_WPCharacter.lua:3355  NotifyBossSkillTips_RPC      BOSS 技能提示
    /// self.IsStandalone 只是角色上的一个 Lua 字段（BP_WPCharacter.lua:31 缓存），写它不动引擎
    /// 复制语义，只把这些表现分支打开。
    ///
    /// 第二件事：v5/v6 的 HUD 回放是"仅在数值变化时补发"，一旦 HUD 重建或被别的调用重置
    /// （实测出现过结算/重开后血量条回到 999/99999、buff 列表不再弹出），就再也不会补。
    /// 本补丁把本地玩家的 HUD 数值改为周期刷新：血量、技能充能、武器弹药面板每 2 秒写一次。
    /// 只调用 UI 上的纯显示函数（OnLocalCharHPChange / OnLocalCharSkillPowerChange /
    /// ChangeAmmunitionUI / OnLocalCharCurWeaponChange），不再重复调 CheckInitFinish 与
    /// OnRep_PlayerWeapon（它们内含 StopAttack / ForceFinishReload 等战斗状态副作用）。
    ///
    /// 第三件事：把 v5/v6 的轮询主体停掉（MikuLCStage / MikuHPStage / MikuHPFire 置空），
    /// 只保留 v6 的 CheckIsLocallyControlled 类表 hook（那是对付 BP/C++ 内部调用的唯一手段），
    /// 并把该 hook 扩到 PlayerController 与 PlayerState 两个类表上。
    /// 顺带修掉 v6 在无角色时刷屏的 `WPGameMgr:GetCharacter error not find character`。
    /// 六禁字符（+ &lt; &gt; &amp; 单引号 双引号）已核对为 0，字符串一律 Lua 长括号。
    /// </summary>
    public const string ZBugHostViewFix = """
        if MikuHVVer ~= 1 then
        MikuHVVer = 1
        MikuHVNum = 0
        MikuHVPawn = nil
        MikuHVLastWp = nil
        MikuHVLastCd = -1
        MikuHVLastTK = -1
        MikuHVLastKC = -1
        MikuHVLastWave = -1
        MikuHVLastBonus = -1
        MikuHVLastBuff = -1
        MikuHVScore = -999999
        MikuLCStage = function() return end
        MikuHPStage = function() return end
        MikuHPFire = function() return end
        MikuHVGet = function(c, name)
        local ok, v = pcall(c.GetAttributeCurValByName, c, name)
        if ok then return v end
        return nil
        end
        MikuHVFields = function(c)
        local mx = MikuHVGet(c, [[Health]])
        local hp = MikuHVGet(c, [[CurHealth]])
        local sp = MikuHVGet(c, [[SkillPower]])
        local spm = MikuHVGet(c, [[SkillPowerMax]])
        local ui = UI.GetUI([[PartyZBugRunFight]])
        if ui ~= nil then
        if mx ~= nil then pcall(ui.OnLocalCharHPChange, ui, [[Health]], math.floor(mx)) end
        if hp ~= nil then pcall(ui.OnLocalCharHPChange, ui, [[CurHealth]], math.floor(hp)) end
        if sp ~= nil and spm ~= nil then pcall(ui.OnLocalCharSkillPowerChange, ui, sp, spm) end
        end
        end
        MikuHVStage = function(tick)
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        local c = nil
        local okc, rc = pcall(WP.GameMgr.GetCharacter, WP.GameMgr, w)
        if okc then c = rc end
        if c == nil then return end
        if c ~= MikuHVPawn then
        MikuHVPawn = c
        pcall(function() c.IsStandalone = true end)
        MikuHVLastWp = nil
        MikuHVLastBuff = -1
        MikuHVScore = -999999
        end
        local wp = nil
        local okw, rw = pcall(c.GetCurWeapon, c)
        if okw then wp = rw end
        local ui = UI.GetUI([[PartyZBugRunFight]])
        local mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w)
        if mgr ~= nil then
        local okcd, cd = pcall(function() return mgr.ZBugChallengeCountDown end)
        if okcd and cd ~= nil then
        local icd = math.floor(cd)
        if icd ~= MikuHVLastCd then
        MikuHVLastCd = icd
        pcall(function() WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, cd) end)
        end
        end
        local oktk, tk = pcall(function() return mgr.CurWaveTotalKill end)
        if oktk and tk ~= nil and tk ~= MikuHVLastTK then
        MikuHVLastTK = tk
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        local okkc, kc = pcall(function() return mgr.CurWaveKillCount end)
        if okkc and kc ~= nil and kc ~= MikuHVLastKC then
        MikuHVLastKC = kc
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        local okwv, wv = pcall(function() return mgr.CurChallengeWave end)
        if okwv and wv ~= nil and wv ~= MikuHVLastWave then
        MikuHVLastWave = wv
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        local okbw, bw = pcall(function() return mgr.BonusWaveTimer end)
        if okbw and bw ~= nil and bw ~= MikuHVLastBonus then
        MikuHVLastBonus = bw
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local okbn, bn = pcall(function() return c.CacheLevelUpBuffs:Num() end)
        if okbn and bn ~= nil and bn ~= MikuHVLastBuff then
        MikuHVLastBuff = bn
        if bn ~= 0 then pcall(function() c:OnRep_CacheLevelUpBuffs() end) end
        end
        local okp, ps = pcall(function() return c:GetCharState() end)
        if okp and ps ~= nil then
        local sc = ps.ChallengeScore
        if sc ~= nil and sc ~= MikuHVScore then
        MikuHVScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        if ui ~= nil and wp ~= nil then
        if wp ~= MikuHVLastWp then
        MikuHVLastWp = wp
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        pcall(function() ui:OnLocalCharCurWeaponChange(wp) end)
        end
        if tick % 4 == 0 then
        MikuHVFields(c)
        end
        end
        if tick % 60 == 0 then
        end
        end
        MikuHVTick = function(left)
        if left == 0 then return end
        MikuHVNum = MikuHVNum - (-1)
        local ok, err = pcall(MikuHVStage, MikuHVNum)
        UE4.Timer.Add(0.5, function() MikuHVTick(left - 1) end)
        end
        MikuHVBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHVBoot(left - 1) end) end
        return
        end
        pcall(MikuHPPatch, [[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPPatch, [[WaterPark.Common.PlayerController.BP_WPPlayerController]])
        pcall(MikuHPPatch, [[WaterPark.Common.GameMode.BP_WPPlayerState]])
        MikuHVTick(7200)
        end
        MikuHVBoot(600)
        end
        """;

    /// <summary>
    /// v8, listen-host HUD completion. Supersedes the v7 stage (v7's MikuHVStage is stubbed out).
    ///
    /// v7 proved the feeds work when they are actually invoked: on the host, calling
    /// UI.Call2("PartyZBugRunFight", "ZBugRun_OpenBuffList", char.CacheLevelUpBuffs) opens the buff
    /// list with three valid cards, and both UI.Call2 and Character:OnRep_CacheLevelUpBuffs succeed.
    /// But v7 only fired that replay on a *change* of CacheLevelUpBuffs:Num(), which happens once,
    /// a few seconds into the match - often before the HUD widget exists - so the one-shot trigger is
    /// lost and the character ends the match with WaitingClientSelectBuff still true and 15 unspent
    /// buffs (measured: bf=15 wcb=true csl=false). v8 re-asserts the prompt while the selection is
    /// still pending and no list is open, on an 8-tick cooldown, so it is self-healing.
    ///
    /// The same one-shot problem hides the skill bar: umg_zbugrun_fight.lua:812 NotifyPlayerSkillsChanged
    /// is what fills SkillPanel["Skill"..i] with the key icons, and it is only ever called from
    /// BP_WPZBugRunMgr.lua:497 inside CheckInitFinish, which the host runs once, before the HUD is up.
    /// The log confirms it never ran on this client (grep "本地玩家技能变化" = 0). v8 re-sends the
    /// skill and attached-item notifications whenever the count changes and every 8 seconds otherwise,
    /// which also refreshes the weapon/skill panel the same way.
    /// </summary>
    public const string ZBugHostHudFix = """
        if MikuHHVer ~= 1 then
        MikuHHVer = 1
        MikuHVStage = function() return end
        local function HHGet(w)
        local ok, c = pcall(WP.GameMgr.GetCharacter, WP.GameMgr, w)
        if ok then return c end
        return nil
        end
        MikuHHStage = function(tick)
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        local c = HHGet(w)
        if c == nil then return end
        if c ~= MikuHHPawn then
        MikuHHPawn = c
        pcall(function() c.IsStandalone = true end)
        MikuHHLastWp = nil
        MikuHHLastBuff = -1
        MikuHHScore = -999999
        MikuHHLastCd = -999999
        MikuHHLastTK = -999999
        MikuHHLastKC = -999999
        MikuHHLastWave = -999999
        MikuHHLastBonus = -999999
        MikuHHSkillN = -1
        MikuHHSkillTick = 0
        MikuHHBuffCool = 0
        end
        local ui = UI.GetUI([[PartyZBugRunFight]])
        local mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w)
        if mgr ~= nil then
        local okcd, cd = pcall(function() return mgr.ZBugChallengeCountDown end)
        if okcd and cd ~= nil then
        local icd = math.floor(cd)
        if icd ~= MikuHHLastCd then
        MikuHHLastCd = icd
        pcall(function() WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, cd) end)
        end
        end
        local oktk, tk = pcall(function() return mgr.CurWaveTotalKill end)
        if oktk and tk ~= nil and tk ~= MikuHHLastTK then
        MikuHHLastTK = tk
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        local okkc, kc = pcall(function() return mgr.CurWaveKillCount end)
        if okkc and kc ~= nil and kc ~= MikuHHLastKC then
        MikuHHLastKC = kc
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        local okwv, wv = pcall(function() return mgr.CurChallengeWave end)
        if okwv and wv ~= nil and wv ~= MikuHHLastWave then
        MikuHHLastWave = wv
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        local okbw, bw = pcall(function() return mgr.BonusWaveTimer end)
        if okbw and bw ~= nil and bw ~= MikuHHLastBonus then
        MikuHHLastBonus = bw
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local okbf, bf = pcall(function() return c.CacheLevelUpBuffs:Num() end)
        if not okbf or bf == nil then bf = -1 end
        if bf ~= MikuHHLastBuff then
        MikuHHLastBuff = bf
        if bf ~= 0 then pcall(function() c:OnRep_CacheLevelUpBuffs() end) end
        end
        if MikuHHBuffCool ~= 0 then MikuHHBuffCool = MikuHHBuffCool - 1 end
        if ui ~= nil and bf ~= 0 and bf ~= -1 then
        local waiting = false
        pcall(function() waiting = c.WaitingClientSelectBuff end)
        local open = true
        pcall(function() open = ui.CurSelectList ~= nil end)
        if waiting and not open and MikuHHBuffCool == 0 then
        MikuHHBuffCool = 8
        pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        end
        end
        local ps = nil
        pcall(function() ps = c:GetCharState() end)
        if ps ~= nil then
        local sc = ps.ChallengeScore
        if sc ~= nil and sc ~= MikuHHScore then
        MikuHHScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        if ui ~= nil and ps ~= nil then
        local sk = -1
        pcall(function() sk = ps.Skills:Num() end)
        if MikuHHSkillTick ~= 0 then MikuHHSkillTick = MikuHHSkillTick - 1 end
        if sk ~= MikuHHSkillN or MikuHHSkillTick == 0 then
        MikuHHSkillN = sk
        MikuHHSkillTick = 16
        pcall(function() ps:NotifyLocalPlayerSkillChanged() end)
        pcall(function() ps:NotifyLocalPlayerAttachedItemsChanged() end)
        end
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if ui ~= nil and wp ~= nil and wp ~= MikuHHLastWp then
        MikuHHLastWp = wp
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        pcall(function() ui:OnLocalCharCurWeaponChange(wp) end)
        end
        if ui ~= nil and tick % 4 == 0 then
        local mx = 0
        local hp = 0
        pcall(function() mx = c:GetAttributeCurValByName([[Health]]) end)
        pcall(function() hp = c:GetAttributeCurValByName([[CurHealth]]) end)
        pcall(function() ui:OnLocalCharHPChange([[Health]], math.floor(mx)) end)
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], math.floor(hp)) end)
        local sp = 0
        local spm = 0
        pcall(function() sp = c:GetAttributeCurValByName([[SkillPower]]) end)
        pcall(function() spm = c:GetAttributeCurValByName([[SkillPowerMax]]) end)
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        if tick % 40 == 0 then
        end
        end
        MikuHHTick = function(left)
        if left == 0 then return end
        local ok, err = pcall(MikuHHStage, left)
        UE4.Timer.Add(0.5, function() MikuHHTick(left - 1) end)
        end
        MikuHHBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHHBoot(left - 1) end) end
        return
        end
        pcall(MikuHPPatch, [[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPPatch, [[WaterPark.Common.PlayerController.BP_WPPlayerController]])
        pcall(MikuHPPatch, [[WaterPark.Common.GameMode.BP_WPPlayerState]])
        MikuHHTick(7200)
        end
        MikuHHBoot(600)
        end
        """;

    /// <summary>
    /// v9, scope fix. v7 and v8 had no "am I the listen host" guard, so they also ran on the LAN
    /// guest, where they are both wrong and visible:
    ///   - v7 wrote IsStandalone = true on the guest's own pawn. BP_WPCharacter.lua:2679 then takes
    ///     "if self.IsStandalone then self:OnRep_CacheLevelUpBuffs() end" inside LuaImpl_RefreshBuff,
    ///     so the guest opened its own buff list from the server-side refresh path as well as from the
    ///     intended client path - matching the report that the guest could pick two buffs at match
    ///     start while the host still could not pick any.
    ///   - v8's buff prompt re-opened the list every 4s on both machines.
    /// v9 keeps the whole stage host-only, restores the real WorldIIsStandalone value on non-host
    /// clients, and paces the buff prompt to one attempt every 8s with a cap of six per pawn, each one
    /// followed by a state read so the log shows whether the list actually took
    ///
    /// Crash note (2026-10-08 21:21:42, minidump 2832258d-9b60-4847-8d0e-e8ad564b8859): the dump's
    /// Game.log ends in DXGI_ERROR_DEVICE_REMOVED on RenderThread 1
    /// ("Direct3DDevice->CreateQuery(...) failed ... D3D11Query.cpp:202", "Unreal Engine is exiting due
    /// to D3D device being lost. (Error: 0x887A0005 - 'REMOVED')"). That is a GPU device loss / driver
    /// reset on an AMD Radeon 610M iGPU, not a script fault: the session has zero "Lua error" lines,
    /// and the same machine produced unrelated minidumps at 02:07 and 04:34, hours before any of these
    /// patches existed. v9 therefore only reduces UI churn rather than treating it as the cause.
    /// </summary>
    public const string ZBugHostScopeFix = """
        if MikuH9Ver ~= 1 then
        MikuH9Ver = 1
        MikuHHStage = function() return end
        MikuH9IsHost = function(w)
        local ok1, a = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        local ok2, b = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        local ok3, d = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if ok1 and ok2 and ok3 then
        if a == true and b == false and d == false then return true end
        end
        return false
        end
        MikuH9Stage = function(tick)
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        local host = MikuH9IsHost(w)
        local okc, c = pcall(WP.GameMgr.GetCharacter, WP.GameMgr, w)
        if not okc or c == nil then return end
        if c ~= MikuH9Pawn then
        MikuH9Pawn = c
        MikuH9LastWp = nil
        MikuH9LastBuff = -1
        MikuH9Score = -999999
        MikuH9LastCd = -999999
        MikuH9LastTK = -999999
        MikuH9LastKC = -999999
        MikuH9LastWave = -999999
        MikuH9LastBonus = -999999
        MikuH9SkillN = -1
        MikuH9SkillTick = 0
        MikuH9BuffCool = 0
        MikuH9BuffTry = 0
        if host then
        pcall(function() c.IsStandalone = true end)
        else
        local okr, rv = pcall(function() return WP.GameMgr:WorldIIsStandalone(w) end)
        if okr then pcall(function() c.IsStandalone = rv end) end
        end
        end
        if not host then return end
        local ui = UI.GetUI([[PartyZBugRunFight]])
        local mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w)
        if mgr ~= nil then
        local okcd, cd = pcall(function() return mgr.ZBugChallengeCountDown end)
        if okcd and cd ~= nil then
        local icd = math.floor(cd)
        if icd ~= MikuH9LastCd then
        MikuH9LastCd = icd
        pcall(function() WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, cd) end)
        end
        end
        local oktk, tk = pcall(function() return mgr.CurWaveTotalKill end)
        if oktk and tk ~= nil and tk ~= MikuH9LastTK then
        MikuH9LastTK = tk
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        local okkc, kc = pcall(function() return mgr.CurWaveKillCount end)
        if okkc and kc ~= nil and kc ~= MikuH9LastKC then
        MikuH9LastKC = kc
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        local okwv, wv = pcall(function() return mgr.CurChallengeWave end)
        if okwv and wv ~= nil and wv ~= MikuH9LastWave then
        MikuH9LastWave = wv
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        local okbw, bw = pcall(function() return mgr.BonusWaveTimer end)
        if okbw and bw ~= nil and bw ~= MikuH9LastBonus then
        MikuH9LastBonus = bw
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local okbf, bf = pcall(function() return c.CacheLevelUpBuffs:Num() end)
        if not okbf or bf == nil then bf = -1 end
        if bf ~= MikuH9LastBuff then
        MikuH9LastBuff = bf
        if bf ~= 0 then pcall(function() c:OnRep_CacheLevelUpBuffs() end) end
        end
        if MikuH9BuffCool ~= 0 then MikuH9BuffCool = MikuH9BuffCool - 1 end
        if ui ~= nil and bf ~= 0 and bf ~= -1 and MikuH9BuffTry ~= -1 then
        local waiting = false
        pcall(function() waiting = c.WaitingClientSelectBuff end)
        local open = true
        pcall(function() open = ui.CurSelectList ~= nil end)
        if waiting and not open and MikuH9BuffCool == 0 then
        MikuH9BuffCool = 16
        MikuH9BuffTry = MikuH9BuffTry - (-1)
        if MikuH9BuffTry == 6 then MikuH9BuffTry = -1 end
        pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        UE4.Timer.Add(0.4, function()
        local csl = true
        local vis = -1
        pcall(function() csl = ui.CurSelectList ~= nil end)
        pcall(function() vis = ui.BuffList:GetVisibility() end)
        end)
        end
        end
        local ps = nil
        pcall(function() ps = c:GetCharState() end)
        if ps ~= nil then
        local sc = ps.ChallengeScore
        if sc ~= nil and sc ~= MikuH9Score then
        MikuH9Score = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        if ui ~= nil and ps ~= nil then
        local sk = -1
        pcall(function() sk = ps.Skills:Num() end)
        if MikuH9SkillTick ~= 0 then MikuH9SkillTick = MikuH9SkillTick - 1 end
        if sk ~= MikuH9SkillN or MikuH9SkillTick == 0 then
        MikuH9SkillN = sk
        MikuH9SkillTick = 16
        pcall(function() ps:NotifyLocalPlayerSkillChanged() end)
        pcall(function() ps:NotifyLocalPlayerAttachedItemsChanged() end)
        end
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if ui ~= nil and wp ~= nil and wp ~= MikuH9LastWp then
        MikuH9LastWp = wp
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        pcall(function() ui:OnLocalCharCurWeaponChange(wp) end)
        end
        if ui ~= nil and tick % 4 == 0 then
        local mx = 0
        local hp = 0
        pcall(function() mx = c:GetAttributeCurValByName([[Health]]) end)
        pcall(function() hp = c:GetAttributeCurValByName([[CurHealth]]) end)
        pcall(function() ui:OnLocalCharHPChange([[Health]], math.floor(mx)) end)
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], math.floor(hp)) end)
        local sp = 0
        local spm = 0
        pcall(function() sp = c:GetAttributeCurValByName([[SkillPower]]) end)
        pcall(function() spm = c:GetAttributeCurValByName([[SkillPowerMax]]) end)
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        if tick % 40 == 0 then
        end
        end
        MikuH9Tick = function(left)
        if left == 0 then return end
        local ok, err = pcall(MikuH9Stage, left)
        UE4.Timer.Add(0.5, function() MikuH9Tick(left - 1) end)
        end
        MikuH9Boot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuH9Boot(left - 1) end) end
        return
        end
        pcall(MikuHPPatch, [[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPPatch, [[WaterPark.Common.PlayerController.BP_WPPlayerController]])
        pcall(MikuHPPatch, [[WaterPark.Common.GameMode.BP_WPPlayerState]])
        MikuH9Tick(7200)
        end
        MikuH9Boot(600)
        end
        """;

    /// <summary>
    /// Host-only, v10. Root cause under test: the broadcast prompt path (MikuH9Stage ->
    /// c:OnRep_CacheLevelUpBuffs) never turns the host's buff list visible, even though the very
    /// same UI.Call2 from the live Lua console DOES open it (measured: csl=true vis=4 then, versus
    /// csl=false vis=1 inside the patch). v9 wrapped that replay in a pcall that printed nothing,
    /// so a swallowed error was invisible, and v9 never called ZBugRun_OpenBuffList directly, so
    /// "UI.GetUI returned nil" and "ZBugRun_OpenBuffList bailed at IsValid(char)" were
    /// indistinguishable. umg_zbugrun_fight.lua:1022-1061 ZBugRun_OpenBuffList has exactly one
    /// silent early return (if not IsValid(char) then return end) and only sets CurSelectList and
    /// WidgetUtils.SelfHitTestInvisible(self.BuffList) inside the CurSelectList == nil branch; the
    /// observed vis=1 (Collapsed) proves that branch never ran. This patch prints every pcall
    /// error on the buff path, prints the character/IsValid state, and then, if the widget is
    /// still closed, opens the list itself with the same call sequence the game uses, so the host
    /// can actually pick a buff while the log tells us which link broke.
    /// Guard variable is new (MikuH9Ver is already 1 in the running client, so editing v9 in place
    /// would never re-execute).
    /// </summary>
    public const string ZBugHostBuffFix = """
        if MikuHBVer ~= 1 then
        MikuHBVer = 1
        MikuH9Stage = function() return end
        MikuHBPawn = nil
        MikuHBLastCd = -1
        MikuHBLastKill = -1
        MikuHBLastKillCount = -1
        MikuHBLastWave = -1
        MikuHBLastBonus = -1
        MikuHBLastScore = -999999
        MikuHBLastBuff = -1
        MikuHBLastWp = nil
        MikuHBBuffCool = 0
        MikuHBBuffTry = 0
        MikuHBIsHost = function(w)
        local ok, v = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        if not ok or v ~= true then return false end
        local ok2, v2 = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        if not ok2 or v2 == true then return false end
        local ok3, v3 = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if not ok3 or v3 == true then return false end
        return true
        end
        MikuHBFlo = function(v)
        local ok, r = pcall(function() return math.floor(v) end)
        if ok then return r end
        return -1
        end
        MikuHBOpen = function(ui, c)
        local count = 0
        pcall(function() count = c.CacheLevelUpBuffs:Num() end)
        if count == 0 then
        return
        end
        local n = 3
        if count == 1 or count == 2 then n = count end
        local tbBuff = {}
        for i = 1, n do
        local id = 0
        pcall(function() id = c.CacheLevelUpBuffs:Get(i) end)
        if id ~= 0 then
        local cfg = nil
        local lv = 0
        pcall(function() cfg = UE4.UGESkillLibrary.GetBuffInfoByID(id, c) end)
        pcall(function() lv = UE4.UGESkillLibrary.GetOwnBuffLevel(id, c) end)
        if cfg ~= nil then table.insert(tbBuff, { cfg = cfg, level = lv }) end
        end
        end
        local ok, err = pcall(function()
        WidgetUtils.SelfHitTestInvisible(ui.BuffList)
        ui.CurSelectList = ui.BuffList
        ui.BuffList:OnOpen({ tbBuff = tbBuff, closeFunc = function() ui:CheckOpenNextList() end })
        end)
        local csl = true
        pcall(function() csl = ui.CurSelectList ~= nil end)
        local vis = -1
        pcall(function() vis = ui.BuffList:GetVisibility() end)
        end
        MikuHBStage = function(tick)
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        if not MikuHBIsHost(w) then return end
        local c = nil
        local okc, resc = pcall(WP.GameMgr.GetCharacter, WP.GameMgr, w)
        if okc then c = resc end
        if c == nil then return end
        local name = nil
        pcall(function() name = c:GetName() end)
        if name ~= MikuHBPawn then
        MikuHBPawn = name
        pcall(function() c.IsStandalone = true end)
        MikuHBLastCd = -1
        MikuHBLastKill = -1
        MikuHBLastKillCount = -1
        MikuHBLastWave = -1
        MikuHBLastBonus = -1
        MikuHBLastScore = -999999
        MikuHBLastBuff = -1
        MikuHBLastWp = nil
        MikuHBBuffCool = 0
        MikuHBBuffTry = 0
        end
        local mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w)
        if mgr ~= nil then
        local cd = MikuHBFlo(mgr.ZBugChallengeCountDown)
        if cd ~= MikuHBLastCd then
        MikuHBLastCd = cd
        pcall(function() WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, mgr.ZBugChallengeCountDown) end)
        end
        local tk = MikuHBFlo(mgr.CurWaveTotalKill)
        if tk ~= MikuHBLastKill then
        MikuHBLastKill = tk
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        local kc = MikuHBFlo(mgr.CurWaveKillCount)
        if kc ~= MikuHBLastKillCount then
        MikuHBLastKillCount = kc
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        local cw = MikuHBFlo(mgr.CurChallengeWave)
        if cw ~= MikuHBLastWave then
        MikuHBLastWave = cw
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        local bw = MikuHBFlo(mgr.BonusWaveTimer)
        if bw ~= MikuHBLastBonus then
        MikuHBLastBonus = bw
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local ps = nil
        pcall(function() ps = c:GetCharState() end)
        if ps ~= nil then
        local sc = MikuHBFlo(ps.ChallengeScore)
        if sc ~= MikuHBLastScore then
        MikuHBLastScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        local ui = UI.GetUI([[PartyZBugRunFight]])
        if ui ~= nil then
        if tick % 4 == 0 then
        local mx = -1
        local hp = -1
        local sp = -1
        local spm = -1
        pcall(function() mx = c:GetAttributeCurValByName([[Health]]) end)
        pcall(function() hp = c:GetAttributeCurValByName([[CurHealth]]) end)
        pcall(function() sp = c:GetAttributeCurValByName([[SkillPower]]) end)
        pcall(function() spm = c:GetAttributeCurValByName([[SkillPowerMax]]) end)
        pcall(function() ui:OnLocalCharHPChange([[Health]], math.floor(mx)) end)
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], math.floor(hp)) end)
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil and wp ~= MikuHBLastWp then
        MikuHBLastWp = wp
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        pcall(function() ui:OnLocalCharCurWeaponChange(wp) end)
        local bar = -1
        pcall(function() bar = wp.BulletNum end)
        end
        if ps ~= nil and tick % 16 == 0 then
        local ok1, e1 = pcall(function() ps:NotifyLocalPlayerSkillChanged() end)
        local ok2, e2 = pcall(function() ps:NotifyLocalPlayerAttachedItemsChanged() end)
        end
        local bf = -1
        pcall(function() bf = c.CacheLevelUpBuffs:Num() end)
        if bf ~= MikuHBLastBuff then
        MikuHBLastBuff = bf
        if bf ~= 0 then
        local okR, errR = pcall(function() return c:OnRep_CacheLevelUpBuffs() end)
        end
        end
        if MikuHBBuffCool ~= 0 then MikuHBBuffCool = MikuHBBuffCool - 1 end
        if bf ~= 0 and MikuHBBuffTry ~= -1 then
        local waiting = false
        pcall(function() waiting = c.WaitingClientSelectBuff end)
        local open = true
        pcall(function() open = ui.CurSelectList ~= nil end)
        if waiting and not open and MikuHBBuffCool == 0 then
        MikuHBBuffCool = 16
        MikuHBBuffTry = MikuHBBuffTry - (-1)
        if MikuHBBuffTry == 4 then MikuHBBuffTry = -1 end
        local cilc = nil
        pcall(function() cilc = c:CheckIsLocallyControlled() end)
        local okB, errB = pcall(function() return c:OnRep_CacheLevelUpBuffs() end)
        local csl1 = true
        pcall(function() csl1 = ui.CurSelectList ~= nil end)
        local vis1 = -1
        pcall(function() vis1 = ui.BuffList:GetVisibility() end)
        if not csl1 then
        local chk = nil
        pcall(function() chk = WP.GameMgr:GetCharacter(w) end)
        local iv = nil
        pcall(function() iv = IsValid(chk) end)
        MikuHBOpen(ui, c)
        end
        end
        end
        end
        if tick % 40 == 0 then
        local b2 = -1
        pcall(function() b2 = c.CacheLevelUpBuffs:Num() end)
        local bar2 = -1
        pcall(function() bar2 = c:GetCurWeapon().BulletNum end)
        end
        end
        MikuHBTick = function(left)
        if left == 0 then return end
        local ok, err = pcall(MikuHBStage, left)
        UE4.Timer.Add(0.5, function() MikuHBTick(left - 1) end)
        end
        MikuHBBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHBBoot(left - 1) end) end
        return
        end
        pcall(MikuHPPatch, [[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPPatch, [[WaterPark.Common.PlayerController.BP_WPPlayerController]])
        pcall(MikuHPPatch, [[WaterPark.Common.GameMode.BP_WPPlayerState]])
        MikuHBTick(7200)
        end
        MikuHBBoot(600)
        end
        """;

    /// <summary>
    /// Host-only, v11. Root cause of "host cannot pick buffs", found by v10's own instrumentation:
    /// c:OnRep_CacheLevelUpBuffs() raised no error but returned immediately, because its gate is
    /// "if self:CheckIsLocallyControlled() and self.CacheLevelUpBuffs:Num() > 0 then". The host's own
    /// pawn reported CheckIsLocallyControlled() == false.
    /// The chain behind that: v6's MikuHPCILC (the only thing still forcing that UFunction true after
    /// v7 stubbed v5's instance-level override) asks MikuHPIsListenHost(), and v6 implemented it as
    /// "pcall(function() return IsServer(w) and not IsDedicatedServer(w) and not IsStandalone(w) end)".
    /// Those three are GLOBAL helpers; in the client script environment they are nil, proven live by a
    /// console payload that failed with "attempt to call a nil value (global 'IsServer')". The pcall
    /// therefore failed, MikuHPIsListenHost always returned false, and the CILC hook always fell back
    /// to the blueprint's own (false) answer. The same trap means every "local player" client-side
    /// branch gated on CheckIsLocallyControlled stays shut on the listen host.
    /// v11 replaces MikuHPIsListenHost with the pcall-wrapped engine calls
    /// (UE4.UKismetSystemLibrary.IsServer / IsDedicatedServer / IsStandalone), verified live on the
    /// host pawn: "CIL fix host=true" then "CIL fix eng=true cilc=true" (was cilc=false). It also
    /// calls c:OnRep_PlayerWeapon() once per weapon change, which the class hook now lets past its
    /// CheckIsLocallyControlled gate and which ends in AnimIns:InitZBugWeaponAsset() - the same
    /// animation-instance initialisation that keeps failing with "NoResPath CharPath: 0" and is the
    /// leading suspect for the host's dead trigger. Buff opening keeps v10's verified fallback.
    /// </summary>
    public const string ZBugHostLocalFix = """
        if MikuHLVer ~= 1 then
        MikuHLVer = 1
        MikuHBStage = function() return end
        MikuHPIsListenHost = function(w)
        if w == nil then return false end
        local ok, v = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        if not ok or v ~= true then return false end
        local ok2, v2 = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        if not ok2 or v2 == true then return false end
        local ok3, v3 = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if not ok3 or v3 == true then return false end
        return true
        end
        MikuHLPawn = nil
        MikuHLLastCd = -1
        MikuHLLastKill = -1
        MikuHLLastKillCount = -1
        MikuHLLastWave = -1
        MikuHLLastBonus = -1
        MikuHLLastScore = -999999
        MikuHLLastBuff = -1
        MikuHLLastWp = nil
        MikuHLBuffCool = 0
        MikuHLFlo = function(v)
        local ok, r = pcall(function() return math.floor(v) end)
        if ok then return r end
        return -1
        end
        MikuHLOpen = function(ui, c)
        local count = 0
        pcall(function() count = c.CacheLevelUpBuffs:Num() end)
        if count == 0 then return end
        local n = 3
        if count == 1 or count == 2 then n = count end
        local tbBuff = {}
        for i = 1, n do
        local id = 0
        pcall(function() id = c.CacheLevelUpBuffs:Get(i) end)
        if id ~= 0 then
        local cfg = nil
        local lv = 0
        pcall(function() cfg = UE4.UGESkillLibrary.GetBuffInfoByID(id, c) end)
        pcall(function() lv = UE4.UGESkillLibrary.GetOwnBuffLevel(id, c) end)
        if cfg ~= nil then table.insert(tbBuff, { cfg = cfg, level = lv }) end
        end
        end
        local ok, err = pcall(function()
        WidgetUtils.SelfHitTestInvisible(ui.BuffList)
        ui.CurSelectList = ui.BuffList
        ui.BuffList:OnOpen({ tbBuff = tbBuff, closeFunc = function() ui:CheckOpenNextList() end })
        end)
        local vis = -1
        pcall(function() vis = ui.BuffList:GetVisibility() end)
        end
        MikuHLStage = function(tick)
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        if not MikuHPIsListenHost(w) then return end
        local c = nil
        local okc, resc = pcall(WP.GameMgr.GetCharacter, WP.GameMgr, w)
        if okc then c = resc end
        if c == nil then return end
        local name = nil
        pcall(function() name = c:GetName() end)
        if name ~= MikuHLPawn then
        MikuHLPawn = name
        pcall(function() c.IsStandalone = true end)
        MikuHLLastCd = -1
        MikuHLLastKill = -1
        MikuHLLastKillCount = -1
        MikuHLLastWave = -1
        MikuHLLastBonus = -1
        MikuHLLastScore = -999999
        MikuHLLastBuff = -1
        MikuHLLastWp = nil
        MikuHLBuffCool = 0
        end
        local mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w)
        if mgr ~= nil then
        local cd = MikuHLFlo(mgr.ZBugChallengeCountDown)
        if cd ~= MikuHLLastCd then
        MikuHLLastCd = cd
        pcall(function() WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, mgr.ZBugChallengeCountDown) end)
        end
        local tk = MikuHLFlo(mgr.CurWaveTotalKill)
        if tk ~= MikuHLLastKill then
        MikuHLLastKill = tk
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        local kc = MikuHLFlo(mgr.CurWaveKillCount)
        if kc ~= MikuHLLastKillCount then
        MikuHLLastKillCount = kc
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        local cw = MikuHLFlo(mgr.CurChallengeWave)
        if cw ~= MikuHLLastWave then
        MikuHLLastWave = cw
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        local bw = MikuHLFlo(mgr.BonusWaveTimer)
        if bw ~= MikuHLLastBonus then
        MikuHLLastBonus = bw
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local ps = nil
        pcall(function() ps = c:GetCharState() end)
        if ps ~= nil then
        local sc = MikuHLFlo(ps.ChallengeScore)
        if sc ~= MikuHLLastScore then
        MikuHLLastScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        local ui = UI.GetUI([[PartyZBugRunFight]])
        if ui ~= nil then
        if tick % 4 == 0 then
        local mx = -1
        local hp = -1
        local sp = -1
        local spm = -1
        pcall(function() mx = c:GetAttributeCurValByName([[Health]]) end)
        pcall(function() hp = c:GetAttributeCurValByName([[CurHealth]]) end)
        pcall(function() sp = c:GetAttributeCurValByName([[SkillPower]]) end)
        pcall(function() spm = c:GetAttributeCurValByName([[SkillPowerMax]]) end)
        pcall(function() ui:OnLocalCharHPChange([[Health]], math.floor(mx)) end)
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], math.floor(hp)) end)
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil and wp ~= MikuHLLastWp then
        MikuHLLastWp = wp
        local okR, errR = pcall(function() return c:OnRep_PlayerWeapon() end)
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        pcall(function() ui:OnLocalCharCurWeaponChange(wp) end)
        local bar = -1
        local fin = nil
        pcall(function() bar = wp.BulletNum end)
        pcall(function() fin = wp.isClientInitFinish end)
        end
        if ps ~= nil and tick % 16 == 0 then
        pcall(function() ps:NotifyLocalPlayerSkillChanged() end)
        pcall(function() ps:NotifyLocalPlayerAttachedItemsChanged() end)
        end
        local bf = -1
        pcall(function() bf = c.CacheLevelUpBuffs:Num() end)
        if bf ~= MikuHLLastBuff then
        MikuHLLastBuff = bf
        if bf ~= 0 then
        local okR, errR = pcall(function() return c:OnRep_CacheLevelUpBuffs() end)
        local open0 = true
        pcall(function() open0 = ui.CurSelectList ~= nil end)
        if not open0 then MikuHLOpen(ui, c) end
        end
        end
        if MikuHLBuffCool ~= 0 then MikuHLBuffCool = MikuHLBuffCool - 1 end
        if bf ~= 0 then
        local waiting = false
        pcall(function() waiting = c.WaitingClientSelectBuff end)
        local open = true
        pcall(function() open = ui.CurSelectList ~= nil end)
        if waiting and not open and MikuHLBuffCool == 0 then
        MikuHLBuffCool = 16
        local cilc = nil
        pcall(function() cilc = c:CheckIsLocallyControlled() end)
        local okB, errB = pcall(function() return c:OnRep_CacheLevelUpBuffs() end)
        local csl1 = true
        pcall(function() csl1 = ui.CurSelectList ~= nil end)
        if not csl1 then MikuHLOpen(ui, c) end
        end
        end
        end
        if tick % 40 == 0 then
        local b2 = -1
        local bar2 = -1
        local cilc2 = nil
        local eng2 = nil
        pcall(function() b2 = c.CacheLevelUpBuffs:Num() end)
        pcall(function() bar2 = c:GetCurWeapon().BulletNum end)
        pcall(function() cilc2 = c:CheckIsLocallyControlled() end)
        pcall(function() eng2 = c:IsLocallyControlled() end)
        end
        end
        MikuHLTick = function(left)
        if left == 0 then return end
        local ok, err = pcall(MikuHLStage, left)
        UE4.Timer.Add(0.5, function() MikuHLTick(left - 1) end)
        end
        MikuHLBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuHLBoot(left - 1) end) end
        return
        end
        pcall(MikuHPPatch, [[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPPatch, [[WaterPark.Common.PlayerController.BP_WPPlayerController]])
        pcall(MikuHPPatch, [[WaterPark.Common.GameMode.BP_WPPlayerState]])
        MikuHLTick(7200)
        end
        MikuHLBoot(600)
        end
        """;

    /// <summary>
    /// v12 ZBugHostWeaponFix -- listen host weapon HUD, airdrop weapon state, and animation instance bootstrap.
    /// BP_WPCharacter:OnRep_WeaponList (BP_WPCharacter.lua:3275-3294) is another RepNotify that never reaches
    /// the host's own client: it feeds ui:OnLocalCharWeaponListRefresh (which is what makes the top-right weapon
    /// widget visible -- uw_party_fight_challenge.lua:168-173 calls WidgetUtils.SelfHitTestInvisible) and it is
    /// also the only place that hides and Lua_Tick()s a picked up airdrop weapon. Replaying it on WeaponList
    /// count change restores the weapon icon, the airdrop slots and the weapon hidden state.
    /// UWaterParkQAnimIns::UpdateZBugRunInfo keeps logging isInitFinish? 0 plus
    /// InitZBugWeaponAsset Failed!!! NoResPath CharPath: 0 -- the anim instance bootstrap (InitAssets /
    /// InitZBugRunInfo) is driven from C++ client paths that never run on the listen host, and the shooting
    /// state machine (InZBugShootAnim is always false) hangs off that same animation layer, so this patch
    /// bootstraps it once per actor and prints the result of every call.
    /// It also sweeps IsStandalone = true over every character and monster (the client-presentation gates in
    /// BP_WPCharacter use "if self:HasAuthority() and not self.IsStandalone then return end") and adds
    /// NotifyLocalPlayerItemsChanged, which v11 was missing.
    /// Guard variable MikuKVer is new, because MikuHLVer is already 1 in the running client.
    /// </summary>
    public const string ZBugHostWeaponFix = """
        if MikuKVer ~= 1 then
        MikuKVer = 1
        MikuHLStage = function() return end
        MikuKSeen = {}
        MikuKPawn = nil
        MikuKCd = -1
        MikuKKill = -1
        MikuKKc = -1
        MikuKWave = -1
        MikuKBonus = -1
        MikuKScore = -1
        MikuKWp = nil
        MikuKWl = -1
        MikuKBf = -1
        MikuKSk = -1
        MikuKSp = -1
        MikuKCool = 0
        MikuKTry = 6
        MikuKHost = function(w)
        if w == nil then return false end
        local o1, a = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        local o2, b = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        local o3, d = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if o1 and o2 and o3 and a == true and b == false and d == false then return true end
        return false
        end
        MikuKActors = function(w)
        local res = {}
        if w == nil then return res end
        local arr = UE4.TArray(UE4.AGEGameCharacter)
        local ok = pcall(function() UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEGameCharacter, arr) end)
        if ok then
        for i = 1, arr:Num() do
        local a = arr:Get(i)
        if a ~= nil then table.insert(res, a) end
        end
        end
        local arr2 = UE4.TArray(UE4.AGEGameMonster)
        local ok2 = pcall(function() UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEGameMonster, arr2) end)
        if ok2 then
        for i = 1, arr2:Num() do
        local a = arr2:Get(i)
        if a ~= nil then table.insert(res, a) end
        end
        end
        return res
        end
        MikuKPump = function(a, isPawn)
        local nm = nil
        pcall(function() nm = a:GetName() end)
        if nm == nil then return end
        if MikuKSeen[nm] ~= nil then return end
        MikuKSeen[nm] = true
        local ai = nil
        pcall(function() ai = a:GetWaterParkAnimIns() end)
        if ai == nil then
        return
        end
        local r = {}
        local o, e = pcall(function() ai:InitAssets(a) end)
        table.insert(r, [[ assets=]] .. tostring(o) .. [[/]] .. tostring(e))
        local wp = nil
        pcall(function() wp = a:GetCurWeapon() end)
        o, e = pcall(function() ai:InitZBugRunInfo(a, wp) end)
        table.insert(r, [[ infoWp=]] .. tostring(o) .. [[/]] .. tostring(e))
        if isPawn == true then
        o, e = pcall(function() ai:InitZBugRunInfo(a, false) end)
        else
        o, e = pcall(function() ai:InitZBugRunInfo(a, true) end)
        end
        table.insert(r, [[ infoFl=]] .. tostring(o) .. [[/]] .. tostring(e))
        o, e = pcall(function() ai:InitZBugWeaponAsset() end)
        table.insert(r, [[ weap=]] .. tostring(o) .. [[/]] .. tostring(e))
        end
        MikuKOpen = function(ui, c)
        if ui == nil or c == nil then return end
        local bf = -1
        pcall(function() bf = c.CacheLevelUpBuffs:Num() end)
        if bf == -1 or bf == 0 then return end
        local n = 3
        if bf == 1 or bf == 2 then n = bf end
        local tb = {}
        for i = 1, n do
        local id = 0
        pcall(function() id = c.CacheLevelUpBuffs:Get(i) end)
        if id ~= 0 then
        local cfg = nil
        local lv = 0
        pcall(function() cfg = UE4.UGESkillLibrary.GetBuffInfoByID(id, c) end)
        pcall(function() lv = UE4.UGESkillLibrary.GetOwnBuffLevel(id, c) end)
        if cfg ~= nil then table.insert(tb, { cfg = cfg, level = lv }) end
        end
        end
        if #tb == 0 then
        return
        end
        local o, e = pcall(function()
        WidgetUtils.SelfHitTestInvisible(ui.BuffList)
        local p = {}
        p.tbBuff = tb
        p.closeFunc = function() ui:CheckOpenNextList() end
        ui.CurSelectList = ui.BuffList
        ui.BuffList:OnOpen(p)
        end)
        local vis = -1
        pcall(function() vis = ui.BuffList:GetVisibility() end)
        end
        MikuKStage = function(tick)
        local okW, w = pcall(function() return WP.GameMgr:GetWorld() end)
        if not okW or w == nil then return end
        if not MikuKHost(w) then return end
        if tick % 8 == 0 then
        local list = MikuKActors(w)
        for i = 1, #list do
        local a = list[i]
        pcall(function() a.IsStandalone = true end)
        local isPawn = true
        pcall(function() if a:Cast(UE4.AGEGameMonster) then isPawn = false end end)
        MikuKPump(a, isPawn)
        end
        end
        local gs = nil
        pcall(function() gs = WP.GameMgr:GetGameState(w) end)
        if gs == nil then return end
        local ui = nil
        pcall(function() ui = UI.GetUI([[PartyZBugRunFight]]) end)
        local c = nil
        local okC = pcall(function() c = WP.GameMgr:GetCharacter(w) end)
        if not okC or c == nil then return end
        if c ~= MikuKPawn then
        MikuKPawn = c
        MikuKCd = -1
        MikuKKill = -1
        MikuKKc = -1
        MikuKWave = -1
        MikuKBonus = -1
        MikuKScore = -1
        MikuKWp = nil
        MikuKWl = -1
        MikuKBf = -1
        MikuKSk = -1
        MikuKSp = -1
        MikuKTry = 6
        MikuKCool = 0
        pcall(function() c.IsStandalone = true end)
        local nm = nil
        pcall(function() nm = c:GetName() end)
        local pc = nil
        pcall(function() pc = c.Controller end)
        local t1 = [[-]]
        local t2 = [[-]]
        if pc ~= nil then
        t1 = tostring(type(pc.CanPlayerWeaponFire))
        t2 = tostring(type(pc.Server_ShootEntityBullet))
        end
        end
        local mgr = nil
        pcall(function() mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w) end)
        if mgr ~= nil then
        local cd = -1
        pcall(function() cd = math.floor(mgr.ZBugChallengeCountDown or -1) end)
        if cd ~= MikuKCd then
        MikuKCd = cd
        if cd ~= -1 then WP.EventMgr:SendEvent(WP.EventKeys.GameState_StartGameCD_MuliCast, cd) end
        end
        local v = -1
        pcall(function() v = mgr.CurWaveTotalKill or -1 end)
        if v ~= MikuKKill then
        MikuKKill = v
        pcall(function() mgr:OnRep_CurWaveTotalKill() end)
        end
        v = -1
        pcall(function() v = mgr.CurWaveKillCount or -1 end)
        if v ~= MikuKKc then
        MikuKKc = v
        pcall(function() mgr:OnRep_CurWaveKillCount() end)
        end
        v = -1
        pcall(function() v = mgr.CurChallengeWave or -1 end)
        if v ~= MikuKWave then
        MikuKWave = v
        pcall(function() mgr:OnRep_CurChallengeWave() end)
        end
        v = -1
        pcall(function() v = math.floor(mgr.BonusWaveTimer or -1) end)
        if v ~= MikuKBonus then
        MikuKBonus = v
        pcall(function() mgr:OnRep_BonusWaveTimer() end)
        end
        end
        local ps = nil
        pcall(function() ps = c:GetCharState() end)
        if ps ~= nil then
        local sc = -1
        pcall(function() sc = math.floor(ps.ChallengeScore or -1) end)
        if sc ~= MikuKScore then
        MikuKScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        local wl = -1
        pcall(function() wl = c.WeaponList:Num() end)
        if wl ~= MikuKWl then
        MikuKWl = wl
        local o, e = pcall(function() c:OnRep_WeaponList() end)
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil and wp ~= MikuKWp then
        MikuKWp = wp
        local o, e = pcall(function() c:OnRep_PlayerWeapon() end)
        local b = -1
        pcall(function() b = wp.BulletNum end)
        if ui ~= nil then
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        pcall(function() ui:OnLocalCharCurWeaponChange(wp) end)
        end
        end
        if ui ~= nil then
        local bf = -1
        pcall(function() bf = c.CacheLevelUpBuffs:Num() end)
        if tick % 4 == 0 then
        local hp = -1
        local mx = -1
        pcall(function() hp = math.floor(c:GetAttributeCurValByName([[CurHealth]]) or -1) end)
        pcall(function() mx = math.floor(c:GetAttributeCurValByName([[Health]]) or -1) end)
        if mx ~= -1 then pcall(function() ui:OnLocalCharHPChange([[Health]], mx) end) end
        if hp ~= -1 then pcall(function() ui:OnLocalCharHPChange([[CurHealth]], hp) end) end
        local sp = -1
        local spm = -1
        pcall(function() sp = math.floor(c:GetAttributeCurValByName([[SkillPower]]) or -1) end)
        pcall(function() spm = math.floor(c:GetAttributeCurValByName([[SkillPowerMax]]) or -1) end)
        if sp ~= -1 and sp ~= MikuKSp then
        MikuKSp = sp
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        end
        if tick % 16 == 0 and ps ~= nil then
        local sn = -1
        pcall(function() sn = ps.Skills:Num() end)
        if sn ~= MikuKSk or tick % 32 == 0 then
        MikuKSk = sn
        pcall(function() ps:NotifyLocalPlayerSkillChanged() end)
        pcall(function() ps:NotifyLocalPlayerAttachedItemsChanged() end)
        pcall(function() ps:NotifyLocalPlayerItemsChanged() end)
        end
        end
        if bf ~= MikuKBf then
        MikuKBf = bf
        local o, e = pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        local open = false
        pcall(function() if ui.CurSelectList ~= nil then open = true end end)
        if open == false then MikuKOpen(ui, c) end
        end
        local waiting = false
        pcall(function() waiting = c.WaitingClientSelectBuff end)
        local open2 = false
        pcall(function() if ui.CurSelectList ~= nil then open2 = true end end)
        if waiting == true and open2 == false and MikuKTry ~= -1 then
        if MikuKCool == 0 then
        MikuKCool = 16
        MikuKTry = MikuKTry - 1
        local o, e = pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        local vis = -1
        pcall(function() vis = ui.BuffList:GetVisibility() end)
        if vis ~= 4 then MikuKOpen(ui, c) end
        end
        end
        if MikuKCool ~= 0 then MikuKCool = MikuKCool - 1 end
        end
        if tick % 40 == 0 then
        local b = -1
        local cilc = false
        local eng = false
        pcall(function() b = c:GetCurWeapon().BulletNum end)
        pcall(function() cilc = c:CheckIsLocallyControlled() end)
        pcall(function() eng = c:IsLocallyControlled() end)
        end
        end
        MikuKTick = function(left)
        if left == 0 then return end
        local ok, e = pcall(MikuKStage, left)
        UE4.Timer.Add(0.5, function() MikuKTick(left - 1) end)
        end
        MikuKBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuKBoot(left - 1) end) end
        return
        end
        pcall(MikuHPPatch, [[WaterPark.Common.Character.BP_WPCharacter]])
        pcall(MikuHPPatch, [[WaterPark.Common.PlayerController.BP_WPPlayerController]])
        pcall(MikuHPPatch, [[WaterPark.Common.GameMode.BP_WPPlayerState]])
        MikuKTick(7200)
        end
        MikuKBoot(600)
        end
        """;
    /// <summary>
    /// ZBugHostFireFix v13 - makes the listen host actually shoot.
    ///
    /// v12 instrumentation settled the question: the host's PlayerController exposes
    /// Server_ShootEntityBullet as a callable UFunction (pcShoot=function in the MK probe log) while
    /// CanPlayerWeaponFire / Server_ZBugChangeFireState are C++ internal and invisible to Lua. The
    /// guest's own shots are logged server side as
    ///   LogTemp: Server_ShootEntityBullet, BulletLoc: X=.., FireLoc: X=.., TargetLoc: X=..
    /// and on the host that line never appears: the native input path enters the fire state
    /// (InZBugFireState true) but the shot itself is produced by an animation notify that never fires,
    /// because the host's UWaterParkQAnimIns was never initialised (InitZBugWeaponAsset Failed!!!
    /// NoResPath CharPath: 0, isInitFinish stays 0). Calling the animation init APIs from Lua is
    /// accepted but has no effect, as v12 measured (failure rate unchanged at about 4.5/s).
    ///
    /// So this patch stops waiting for the animation layer and drives the shot directly:
    ///   1. the Event.PartyBtnPress Fire(5) press/release from the HUD sets a held-fire flag;
    ///   2. while held and GameState == Progress a 0.1s timer calls Character:TryAttack() (for the
    ///      local animation/state machine) and PlayerController:Server_ShootEntityBullet(muzzle,
    ///      muzzle, aim) with the aim point derived from PlayerCameraManager:GetCameraLocation() /
    ///      GetCameraRotation() plus UKismetMathLibrary:GetForwardVector() at 10000 units, so the
    ///      entity bullet flies along the crosshair exactly like the guest's does;
    ///   3. the host's own pawn gets the InitZBugChar() call that clients run in BeginPlay and the
    ///      authority branch skips (monsters are deliberately never touched: an earlier monster pump
    ///      made both machines damage-immune);
    ///   4. HP / SkillPower / ammunition HUD feeds move to the same 0.1s timer, which also fixes the
    ///      chunky skill bar drain that v12's 2s sampling introduced.
    ///
    /// Server_ShootEntityBullet line, then monster damage and ChallengeScore starting to move.
    /// </summary>
    public const string ZBugHostFireFix = """
        if MikuFXVer ~= 2 then
        MikuFXVer = 2
        MikuFXFire = false
        MikuFXShots = 0
        MikuFXAmmo = -1
        MikuFXHp = -1
        MikuFXMx = -1
        MikuFXSp = -1
        MikuFXTick = 0
        MikuFXKeys = 0
        MikuFXPawn = nil
        MikuFXDid = false

        MikuFXIsHost = function(w)
        if w == nil then return false end
        local ok, v = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        if not ok then return false end
        if v ~= true then return false end
        local ok2, v2 = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        if ok2 and v2 == true then return false end
        local ok3, v3 = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if ok3 and v3 == true then return false end
        return true
        end

        MikuFXShot = function(c, pc)
        local camLoc = nil
        local camRot = nil
        pcall(function() camLoc = pc.PlayerCameraManager:GetCameraLocation() end)
        pcall(function() camRot = pc.PlayerCameraManager:GetCameraRotation() end)
        if camLoc == nil then pcall(function() camLoc = c:GetActorLocation() end) end
        if camRot == nil then pcall(function() camRot = pc:GetControlRotation() end) end
        if camLoc == nil or camRot == nil then return end
        local fwd = nil
        local okF = pcall(function() fwd = UE4.UKismetMathLibrary.GetForwardVector(camRot) end)
        if not okF or fwd == nil then return end
        local far = UE4.FVector(camLoc.X - (0 - fwd.X * 10000), camLoc.Y - (0 - fwd.Y * 10000), camLoc.Z - (0 - fwd.Z * 10000))
        local muzzle = camLoc
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil then
        local wl = nil
        local okW = pcall(function() wl = wp:GetActorLocation() end)
        if okW and wl ~= nil then muzzle = wl end
        end
        local ok, e = pcall(function() pc:Server_ShootEntityBullet(muzzle, muzzle, far) end)
        MikuFXShots = MikuFXShots - (-1)
        local n = MikuFXShots
        if not ok or n == 1 or n == 2 or n == 3 or n % 20 == 0 then
        end
        end

        MikuFXKey = function(t, p)
        if t ~= 5 then return end
        MikuFXKeys = MikuFXKeys - (-1)
        if p == true or p == 1 then
        MikuFXFire = true
        else
        MikuFXFire = false
        end
        if MikuFXKeys == 1 or MikuFXKeys % 10 == 0 or MikuFXFire == true then
        end
        end

        MikuFXStep = function()
        MikuFXTick = MikuFXTick - (-1)
        local w = nil
        pcall(function() w = WP.GameMgr:GetWorld() end)
        if w == nil then return end
        if not MikuFXIsHost(w) then return end
        local gs = nil
        pcall(function() gs = WP.GameMgr:GetGameState(w) end)
        local c = nil
        pcall(function() c = WP.GameMgr:GetCharacter(w) end)
        if c == nil then return end
        local pc = nil
        pcall(function() pc = c.Controller end)
        if pc == nil then return end
        if MikuFXPawn ~= c then
        MikuFXPawn = c
        MikuFXDid = false
        MikuFXHp = -1
        MikuFXMx = -1
        MikuFXSp = -1
        MikuFXAmmo = -1
        end
        if not MikuFXDid then
        MikuFXDid = true
        local ok, e = pcall(function() c:InitZBugChar() end)
        end
        local gsv = -1
        pcall(function() gsv = gs.GameState end)
        if MikuFXFire == true and gsv == 4 then
        pcall(function() c:TryAttack() end)
        pcall(function() pc.CacheFireInput = true end)
        MikuFXShot(c, pc)
        end
        local ui = nil
        pcall(function() ui = UI.GetUI([[PartyZBugRunFight]]) end)
        if ui ~= nil then
        local mx = nil
        local hp = nil
        local ok1 = pcall(function() mx = c:GetAttributeCurValByName([[Health]]) end)
        local ok2 = pcall(function() hp = c:GetAttributeCurValByName([[CurHealth]]) end)
        if ok1 and ok2 and mx ~= nil and hp ~= nil then
        if mx ~= MikuFXMx or hp ~= MikuFXHp then
        MikuFXMx = mx
        MikuFXHp = hp
        pcall(function() ui:OnLocalCharHPChange([[Health]], math.floor(mx)) end)
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], math.floor(hp)) end)
        end
        end
        local sp = nil
        local spm = nil
        local ok3 = pcall(function() sp = c:GetAttributeCurValByName([[SkillPower]]) end)
        local ok4 = pcall(function() spm = c:GetAttributeCurValByName([[SkillPowerMax]]) end)
        if ok3 and ok4 and sp ~= nil and spm ~= nil then
        if sp ~= MikuFXSp then
        MikuFXSp = sp
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil then
        local bar = nil
        pcall(function() bar = wp.BulletNum end)
        if bar ~= nil and bar ~= MikuFXAmmo then
        MikuFXAmmo = bar
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        end
        end
        end
        if MikuFXTick % 300 == 0 then
        end
        end

        MikuFXTimer = function(left)
        if left == 0 then return end
        local ok, e = pcall(MikuFXStep)
        UE4.Timer.Add(0.1, function() MikuFXTimer(left - 1) end)
        end

        MikuFXBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuFXBoot(left - 1) end) end
        return
        end
        local ok, e = pcall(function() EventSystem.On(Event.PartyBtnPress, MikuFXKey) end)
        MikuFXTimer(120000)
        end
        MikuFXBoot(600)
        end
        """;
    /// <summary>
    /// ZBugHostAssetFix v14 - runs the client's animation-asset preload on the listen host.
    ///
    /// BP_WPZBugRunMgr.lua:7-28 ReceiveBeginPlay is the systemic gate again, and this one explains
    /// every remaining visual symptom:
    ///     if not self:HasAuthority() then
    ///         UE4.UGameLibrary.PreLoadGameAssetByConfig("/Game/PreloadConfig/Misc/ZBugNiagara.ZBugNiagara", "ZBugNiagara")
    ///         ... seven more preload configs ...
    ///         self:PreloadOtherAsset()      -- char / zombie / AS / BS / montage animation assets
    ///     end
    /// PreloadOtherAsset (BP_WPZBugRunMgr.lua:532-600) builds the resource list that UWaterParkQAnimIns
    /// later resolves: nine human character sets (player002_q ... girl013b_q), mon809_q, wp001..wp003,
    /// mlwp001, AS shoot_stand/stand/rush_loop/hit/shoot, BS shootmove/aimoffset, montage shot, the
    /// zombie AS hit_add and nineteen zombie montages, then calls
    /// ULevelLibrary.PreloadCustomAssets(ResArr, "ZBugWeaponAnim"). On the listen host HasAuthority()
    /// is true, so none of it ever runs - which is exactly why the host keeps printing
    /// UWaterParkQAnimIns::InitZBugWeaponAsset Failed!!! NoResPath CharPath: 0, WeaponPath:1 and
    /// UpdateZBugRunInfo isInitFinish? 0 (measured at a steady 4.5/s), i.e. the anim instances never
    /// finish loading, so monster legs never move, hit effects never spawn, and the fire-state
    /// animation notify that would spawn an entity bullet never fires.
    ///
    /// This patch replays that block host side: the eight PreLoadGameAssetByConfig calls plus
    /// PreloadOtherAsset(), once per ZBugRunMgr instance (i.e. once per level), and then a single
    /// sweep that calls InitZBugWeaponAsset() / InitZBugRunInfo() on every character and monster anim
    /// instance so the already-spawned actors pick the assets up. Every call prints its result
    /// the calls are accepted and whether the NoResPath / isInitFinish? 0 rate collapses.
    /// Monsters only get the animation entry points here - never InitZBugChar(), which is the gameplay
    /// init whose earlier repeated invocation made both machines damage-immune.
    /// Guard variable MikuAVer is new.
    /// </summary>
    public const string ZBugHostAssetFix = """
        if MikuAVer ~= 1 then
        MikuAVer = 1
        MikuAMgr = nil
        MikuASweep = 0
        MikuADone = false

        MikuAIsHost = function(w)
        if w == nil then return false end
        local ok, v = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        if not ok then return false end
        if v ~= true then return false end
        local ok2, v2 = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        if ok2 and v2 == true then return false end
        local ok3, v3 = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if ok3 and v3 == true then return false end
        return true
        end

        MikuAPreload = function(mgr)
        local names = {
        [[/Game/PreloadConfig/Misc/ZBugNiagara.ZBugNiagara]],
        [[/Game/PreloadConfig/Misc/ZBugBP.ZBugBP]],
        [[/Game/PreloadConfig/Misc/ZBugUI.ZBugUI]],
        [[/Game/PreloadConfig/Misc/ZBugUMG.ZBugUMG]],
        [[/Game/PreloadConfig/Misc/DLC35ZBugNiagara.DLC35ZBugNiagara]],
        [[/Game/PreloadConfig/Misc/ZBugChallengeBP.ZBugChallengeBP]],
        [[/Game/PreloadConfig/Misc/PartyIcon.PartyIcon]],
        [[/Game/PreloadConfig/Misc/PartyBuffIcon.PartyBuffIcon]] }
        local tags = { [[ZBugNiagara]], [[ZBugBP]], [[ZBugUI]], [[ZBugUMG]], [[DLC35ZBugNiagara]], [[ZBugChallengeBP]], nil, nil }
        for i = 1, 8 do
        local p = names[i]
        local t = tags[i]
        local ok, e = nil, nil
        if t == nil then
        ok, e = pcall(function() UE4.UGameLibrary.PreLoadGameAssetByConfig(p) end)
        else
        ok, e = pcall(function() UE4.UGameLibrary.PreLoadGameAssetByConfig(p, t) end)
        end
        end
        local ok, e = pcall(function() mgr:PreloadOtherAsset() end)
        end

        MikuASweepAnim = function(w)
        local arr = UE4.TArray(UE4.AGEGameCharacter)
        local okA = pcall(function() UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEGameCharacter, arr) end)
        if not okA then return end
        local n = 0
        pcall(function() n = arr:Num() end)
        if n == 0 then pcall(function() n = arr:Length() end) end
        for i = 1, n do
        local c = nil
        pcall(function() c = arr:Get(i) end)
        if c ~= nil then
        local ai = nil
        pcall(function() ai = c:GetWaterParkAnimIns() end)
        local nm = nil
        pcall(function() nm = c:GetName() end)
        if ai == nil then
        else
        local o1, e1 = pcall(function() ai:InitZBugWeaponAsset() end)
        local o2, e2 = pcall(function() ai:InitZBugRunInfo() end)
        end
        end
        end
        end

        MikuAStage = function()
        local w = nil
        pcall(function() w = WP.GameMgr:GetWorld() end)
        if w == nil then return end
        if not MikuAIsHost(w) then return end
        local mgr = nil
        pcall(function() mgr = UE4.UGEZBugRunLibrary.GetZBugRunMgr(w) end)
        if mgr == nil then return end
        if MikuAMgr ~= mgr then
        MikuAMgr = mgr
        MikuADone = false
        MikuAPreload(mgr)
        MikuASweep = 6
        end
        if MikuASweep ~= 0 then
        MikuASweep = MikuASweep - 1
        if MikuASweep == 0 and not MikuADone then
        MikuADone = true
        MikuASweepAnim(w)
        end
        end
        end

        MikuATimer = function(left)
        if left == 0 then return end
        local ok, e = pcall(MikuAStage)
        UE4.Timer.Add(0.5, function() MikuATimer(left - 1) end)
        end

        MikuABoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuABoot(left - 1) end) end
        return
        end
        MikuATimer(24000)
        end
        MikuABoot(600)
        end
        """;
    /// <summary>
    /// ZBugHostClarityFix v15 - three independent, low-risk items found while chasing the host's
    /// remaining visual symptoms.
    ///
    ///    whose value started this whole saga, and 034 measured it returning false for the listen
    ///    host's own pawn while engine IsLocallyControlled() said true. The suspicion now is that it
    ///    is inverted in the other direction too: on the host it may return true for monsters and for
    ///    the guest's proxy pawn, which would make host-side code treat them as "the local player".
    ///    Once per character per level this prints name / class / engine value / original UFunction
    ///    value / hooked value / whether it is the local pawn, so the next log settles it.
    ///
    ///    marker, the damage number and the red enemy HP bar over the victim - gated on
    ///    "launchPawn:CheckIsLocallyControlled()", i.e. on the suspicious function above. If that
    ///    function is true for monsters on the host, every monster hit on the guest lights up the
    ///    host's own crosshair and paints the guest with the enemy bar, which is exactly what the
    ///    host reported. The wrapper only forwards to the original when the launcher really is the
    ///
    ///    weapon's SkeletalMesh for every mode, but only the ZBugRun branch of the reborn flow ever
    ///    restores them (BP_WPCharacter.lua:784-795, a 1.2s timer inside ReStart_MultCast). The
    ///    ZBugChallenge branch of LuaImpl_ZBugRebornFinish (BP_WPCharacter.lua:2973-2995) never
    ///    restores visibility, so a reborn character stays invisible - the vanished model the host
    ///    a 2s poll does the same for any character whose IsReborningFinish flips true without the
    ///    hook being routed to Lua.
    ///
    /// ExMaxMoveSpeed whenever BaseMaxMoveSpeed changes, so a stuck speed modifier shows up in the
    /// log rather than only in the user's description.
    /// Guard variable MikuCBVer is new.
    /// </summary>
    public const string ZBugHostClarityFix = """
        if MikuCBVer ~= 1 then
        MikuCBVer = 1
        MikuCBSeen = {}
        MikuCBRBSeen = {}
        MikuCBSpdLast = -1
        MikuDMSkip = 0
        MikuCBWorld = function(o)
        local ok, w = pcall(function() return o:GetWorld() end)
        if ok then return w end
        return nil
        end
        MikuCBName = function(o)
        if o == nil then return [[nil]] end
        local ok, n = pcall(function() return o:GetName() end)
        if ok then return n end
        return [[?]]
        end
        MikuCBCls = function(o)
        local ok, n = pcall(function() return o:GetClass():GetName() end)
        if ok then return n end
        return [[?]]
        end
        MikuCBIsLocal = function(o)
        if o == nil then return false end
        local ok, v = pcall(function() return o:IsLocallyControlled() end)
        if ok and v == true then return true end
        return false
        end
        MikuCBLocalPawn = function(w)
        if w == nil then return nil end
        local ok, pc = pcall(function() return WP.GameMgr:GetMyPlayerController(w) end)
        if not ok or pc == nil then return nil end
        local ok2, ch = pcall(function() return pc.Character end)
        if ok2 then return ch end
        return nil
        end
        MikuCBOrig = function(c)
        local o = nil
        local d = MikuHPDepth
        MikuHPDepth = 1
        pcall(function() o = c.Overridden.CheckIsLocallyControlled(c) end)
        MikuHPDepth = d
        return o
        end
        MikuCBChars = function(w)
        local arr = UE4.TArray(UE4.AGEGameCharacter)
        pcall(function() UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEGameCharacter, arr) end)
        local n = 0
        pcall(function() n = arr:Length() end)
        if n == 0 then return nil end
        return arr, n
        end
        MikuCBScan = function(w)
        local arr, n = MikuCBChars(w)
        if arr == nil then return end
        local lp = MikuCBLocalPawn(w)
        for i = 1, n do
        local c = nil
        pcall(function() c = arr:Get(i) end)
        if c ~= nil then
        local nm = MikuCBName(c)
        if MikuCBSeen[nm] == nil then
        MikuCBSeen[nm] = 1
        local eng = false
        pcall(function() eng = c:IsLocallyControlled() end)
        local mine = false
        pcall(function() mine = c:CheckIsLocallyControlled() end)
        end
        end
        end
        end
        MikuCBRestore = function(c, tag)
        if c == nil then return end
        local m = nil
        pcall(function() m = c.Mesh end)
        local mv = [[noMesh]]
        if m ~= nil then
        local okv = pcall(function() m:SetVisibility(true) end)
        mv = tostring(okv)
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        local wv = [[noWp]]
        if wp ~= nil then
        local a = pcall(function() if wp.SkeletalMesh ~= nil then wp.SkeletalMesh:SetVisibility(true) end end)
        local b = pcall(function() if wp.WeaponMesh ~= nil then wp.WeaponMesh:SetVisibility(true) end end)
        wv = tostring(a) .. [[/]] .. tostring(b)
        end
        end
        MikuCBRBScan = function(w)
        local arr, n = MikuCBChars(w)
        if arr == nil then return end
        for i = 1, n do
        local c = nil
        pcall(function() c = arr:Get(i) end)
        if c ~= nil then
        local fin = false
        pcall(function() fin = c.IsReborningFinish end)
        local nm = MikuCBName(c)
        if fin == true and MikuCBRBSeen[nm] == nil then
        MikuCBRBSeen[nm] = 1
        MikuCBRestore(c, [[poll]])
        end
        end
        end
        end
        MikuCBSpd = function(w)
        local lp = MikuCBLocalPawn(w)
        if lp == nil then return end
        local base = -1
        pcall(function() base = lp.BaseMaxMoveSpeed end)
        if base == MikuCBSpdLast then return end
        MikuCBSpdLast = base
        local cur = -1
        pcall(function() cur = lp:GetMoveComponent():GetMaxSpeed() end)
        local ex = -1
        pcall(function() ex = lp.ExMaxMoveSpeed end)
        local fly = false
        pcall(function() fly = lp.IsInHitFly end)
        end
        MikuCBHook = function()
        local ok, cls = pcall(require, [[WaterPark.Common.Character.BP_WPCharacter]])
        if not ok or type(cls) ~= [[table]] then
        return
        end
        if cls.MikuCBDMWrapped ~= true then
        local old = cls.LuaImpl_DamageOther
        if old == nil then
        else
        cls.MikuCBDMWrapped = true
        cls.LuaImpl_DamageOther = function(self, a1, a2, a3, a4, a5, a6)
        local w = MikuCBWorld(self)
        local lp = MikuCBLocalPawn(w)
        local gs = nil
        if w ~= nil then pcall(function() gs = WP.GameMgr:GetGameState(w) end) end
        local lch = nil
        if gs ~= nil then pcall(function() lch = gs:GetCharByCharUniqueID(a1) end) end
        if lp ~= nil and lch ~= nil and lch == lp then
        return old(self, a1, a2, a3, a4, a5, a6)
        end
        MikuDMSkip = MikuDMSkip - (-1)
        if MikuDMSkip == 1 or MikuDMSkip % 10 == 0 then
        end
        return
        end
        end
        end
        if cls.MikuCBRBWrapped ~= true then
        local old2 = cls.LuaImpl_ZBugRebornFinish
        if old2 == nil then
        else
        cls.MikuCBRBWrapped = true
        cls.LuaImpl_ZBugRebornFinish = function(self)
        local okr, err = pcall(old2, self)
        MikuCBRestore(self, [[finish]])
        end
        end
        end
        end
        MikuCBTick = function(left)
        if left == 0 then return end
        local ok, err = pcall(function()
        local w = WP.GameMgr:GetWorld()
        if w == nil then return end
        local gs = nil
        pcall(function() gs = WP.GameMgr:GetGameState(w) end)
        if gs == nil then return end
        local st = nil
        pcall(function() st = gs:GetCurState() end)
        if st ~= 4 then return end
        MikuCBScan(w)
        MikuCBRBScan(w)
        MikuCBSpd(w)
        end)
        UE4.Timer.Add(2.0, function() MikuCBTick(left - 1) end)
        end
        MikuCBBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuCBBoot(left - 1) end) end
        return
        end
        MikuCBHook()
        MikuCBTick(900)
        end
        MikuCBBoot(600)
        end
        """;
    /// <summary>
    /// ZBugHostCoreFix v16 - keeps the host's local-player layer alive and actually shoots.
    ///
    /// Three provable bugs from the 2026-10-09 session logs:
    ///
    /// (a) Every earlier host stage died of timer-budget exhaustion (v5 30min, v7/v8 60min, v12 60min,
    ///     v15 30min). MikuKStage (the HUD/weapon-list/score/buff feeder) stopped at 23:21, so the
    ///     00:15 match started with a freshly reset HUD and nothing repopulated it - that is why the
    ///     host lost the skill / item / weapon UI again (MK count in that match = 0). This patch runs
    ///     an unbounded 0.5s stage and repopulates everything whenever the local pawn, the HUD widget
    ///     or the game state changes, then keeps the values flowing.
    ///
    /// (b) v13's fire injection never fired a single shot. EventSystem allows any number of handlers,
    ///     "shots=0 bar=45". This patch uses its own MikuCVDown variable that no old handler writes,
    ///     plus a dedicated 0.1s fire pump that calls TryAttack / CacheFireInput / Server_ShootEntityBullet.
    ///
    /// (c) The v6 CheckIsLocallyControlled wrapper returned true whenever the engine
    ///     IsLocallyControlled() was true - which on a listen server is true for AI monsters (and for
    ///     the host's own player controller). That made monsters take the local-player branches on the
    ///     host, which is what painted the red enemy bar and the hit marker over the guest. Proof:
    ///     The new wrapper is identity based (local PC / player state / pawn / owner) and otherwise
    ///     returns the game's own value unchanged.
    ///
    /// (d) The reborn restore also re-shows SubtituteObj.StaticMesh, and the start hook marks the
    ///     character so the restore works for the guest's proxy pawn as well.
    ///
    /// Guard variable MikuCVVer is new.
    /// </summary>
    public const string ZBugHostCoreFix = """
        if MikuCVVer ~= 1 then
        MikuCVVer = 1
        MikuCVN = 0
        MikuCVShots = 0
        MikuCVDown = false
        MikuCVPawn = nil
        MikuCVUi = nil
        MikuCVState = -1
        MikuCVHp = -1
        MikuCVMx = -1
        MikuCVSp = -1
        MikuCVSpm = -1
        MikuCVAmmo = -1
        MikuCVWl = -1
        MikuCVScore = -1
        MikuCVBf = -1
        MikuCVCool = 0
        MikuCVChars = {}
        MikuCVRb = {}
        MikuCVRepop = 0

        MikuCVHost = function(w)
        if w == nil then return false end
        local ok, v = pcall(function() return UE4.UKismetSystemLibrary.IsServer(w) end)
        if not ok or v ~= true then return false end
        local ok2, v2 = pcall(function() return UE4.UKismetSystemLibrary.IsDedicatedServer(w) end)
        if ok2 and v2 == true then return false end
        local ok3, v3 = pcall(function() return UE4.UKismetSystemLibrary.IsStandalone(w) end)
        if ok3 and v3 == true then return false end
        return true
        end

        MikuCVOrig = function(o)
        if o == nil then return nil end
        local d = MikuHPDepth
        MikuHPDepth = 1
        local r = nil
        pcall(function() r = o.Overridden.CheckIsLocallyControlled(o) end)
        MikuHPDepth = d
        return r
        end

        MikuCVLocal = function(o, w)
        if o == nil or w == nil then return false end
        local ok, pc = pcall(function() return WP.GameMgr:GetMyPlayerController(w) end)
        if not ok or pc == nil then return false end
        if o == pc then return true end
        local ps = nil
        pcall(function() ps = pc.PlayerState end)
        if ps ~= nil and o == ps then return true end
        local ch = nil
        pcall(function() ch = pc.Character end)
        if ch ~= nil and o == ch then return true end
        local ow = nil
        pcall(function() ow = o:GetOwner() end)
        if ow ~= nil and (ow == pc or ow == ch) then return true end
        return false
        end

        MikuCVCilc = function(self)
        local o = MikuCVOrig(self)
        if o == true then return true end
        local w = nil
        pcall(function() w = self:GetWorld() end)
        if w == nil then return o end
        if not MikuCVHost(w) then return o end
        if MikuCVLocal(self, w) then return true end
        return o
        end

        MikuCVHook = function()
        local names = {
        [[WaterPark.Common.Character.BP_WPCharacter]],
        [[WaterPark.Common.Character.BP_WPMonster]],
        [[WaterPark.Common.PlayerController.BP_WPPlayerController]],
        [[WaterPark.Common.GameMode.BP_WPPlayerState]] }
        for k, name in pairs(names) do
        local ok, cls = pcall(require, name)
        if ok and cls ~= nil then
        pcall(function() cls.CheckIsLocallyControlled = MikuCVCilc end)
        end
        end
        local ok2, cls2 = pcall(require, [[WaterPark.Common.Character.BP_WPCharacter]])
        if ok2 and cls2 ~= nil then
        local old = cls2.LuaImpl_ZBugStartReborn
        if old == nil then
        else
        cls2.LuaImpl_ZBugStartReborn = function(self, a1, a2)
        local nm = nil
        pcall(function() nm = self:GetName() end)
        if nm ~= nil then MikuCVRb[nm] = 1 end
        return old(self, a1, a2)
        end
        end
        end
        end

        MikuCVRestore = function(a, tag)
        local nm = nil
        pcall(function() nm = a:GetName() end)
        local mv = false
        pcall(function() a.Mesh:SetVisibility(true) end)
        pcall(function() mv = a.Mesh:IsVisible() end)
        local wp = nil
        pcall(function() wp = a:GetCurWeapon() end)
        if wp ~= nil then
        pcall(function() if wp.SkeletalMesh ~= nil then wp.SkeletalMesh:SetVisibility(true) end end)
        pcall(function() if wp.WeaponMesh ~= nil then wp.WeaponMesh:SetVisibility(true) end end)
        end
        local so = nil
        pcall(function() so = a.SubtituteObj end)
        if so ~= nil then
        pcall(function() so.StaticMesh:SetVisibility(true) end)
        end
        end

        MikuCVMuzzle = function(c)
        local p = nil
        pcall(function() p = c:GetActorLocation() end)
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil then
        local ok, wl = pcall(function() return wp:GetActorLocation() end)
        if ok and wl ~= nil then p = wl end
        end
        return p
        end

        MikuCVAim = function(pc, c)
        local loc = nil
        local rot = nil
        pcall(function() loc = pc.PlayerCameraManager:GetCameraLocation() end)
        pcall(function() rot = pc.PlayerCameraManager:GetCameraRotation() end)
        if loc == nil then pcall(function() loc = c:GetActorLocation() end) end
        if rot == nil then pcall(function() rot = pc:GetControlRotation() end) end
        if loc == nil or rot == nil then return nil end
        local fwd = nil
        local ok = pcall(function() fwd = UE4.UKismetMathLibrary.GetForwardVector(rot) end)
        if not ok or fwd == nil then return nil end
        return UE4.FVector(loc.X - (0 - fwd.X * 10000), loc.Y - (0 - fwd.Y * 10000), loc.Z - (0 - fwd.Z * 10000))
        end

        MikuCVDoShot = function(c, pc)
        local m = MikuCVMuzzle(c)
        if m == nil then return end
        local t = MikuCVAim(pc, c)
        if t == nil then return end
        local ok, e = pcall(function() pc:Server_ShootEntityBullet(m, m, t) end)
        MikuCVShots = MikuCVShots - (-1)
        end

        MikuCVFireTick = function(left)
        if MikuCVDown ~= true then return end
        if left == 0 then return end
        local w = nil
        pcall(function() w = WP.GameMgr:GetWorld() end)
        if w ~= nil and MikuCVHost(w) then
        local c = nil
        pcall(function() c = WP.GameMgr:GetCharacter(w) end)
        if c ~= nil then
        local pc = nil
        pcall(function() pc = c.Controller end)
        local gs = nil
        pcall(function() gs = WP.GameMgr:GetGameState(w) end)
        local st = -1
        if gs ~= nil then pcall(function() st = gs:GetCurState() end) end
        if pc ~= nil and st == 4 then
        pcall(function() c:TryAttack() end)
        pcall(function() pc.CacheFireInput = true end)
        MikuCVDoShot(c, pc)
        end
        end
        end
        UE4.Timer.Add(0.1, function() MikuCVFireTick(left - 1) end)
        end

        MikuCVKey = function(t, p)
        if t ~= 5 then return end
        if p == true or p == 1 then
        MikuCVDown = true
        MikuCVFireTick(6000)
        else
        MikuCVDown = false
        end
        end

        MikuCVSweep = function(w, gs, isFull)
        local arr = UE4.TArray(UE4.AGEGameCharacter)
        pcall(function() UE4.UGameplayStatics.GetAllActorsOfClass(w, UE4.AGEGameCharacter, arr) end)
        local n = 0
        pcall(function() n = arr:Length() end)
        if n == 0 then return end
        for i = 1, n do
        local a = nil
        pcall(function() a = arr:Get(i) end)
        if a ~= nil then
        if isFull then pcall(function() a.IsStandalone = true end) end
        local nm = nil
        pcall(function() nm = a:GetName() end)
        if nm ~= nil and MikuCVChars[nm] == nil then
        MikuCVChars[nm] = 1
        local eng = false
        pcall(function() eng = a:IsLocallyControlled() end)
        local mon = nil
        pcall(function() mon = a:Cast(UE4.AGEGameMonster) end)
        local im = false
        if mon ~= nil and mon ~= false then im = true end
        local loc = MikuCVLocal(a, w)
        local an = [[nil]]
        local ai = nil
        pcall(function() ai = a.Mesh:GetAnimInstance() end)
        if ai ~= nil then pcall(function() an = ai:GetClass():GetName() end) end
        end
        if MikuCVRb[nm] ~= nil then
        local rbn = false
        pcall(function() rbn = a.IsReborning end)
        if rbn == true then
        MikuCVRb[nm] = MikuCVRb[nm] - (-1)
        else
        MikuCVRb[nm] = nil
        MikuCVRestore(a, [[reborn]])
        end
        end
        end
        end
        if isFull and gs ~= nil then pcall(function() gs.IsStandalone = true end) end
        end

        MikuCVStage = function()
        local w = nil
        pcall(function() w = WP.GameMgr:GetWorld() end)
        if w == nil then return end
        if not MikuCVHost(w) then return end
        MikuCVN = MikuCVN - (-1)
        local gs = nil
        pcall(function() gs = WP.GameMgr:GetGameState(w) end)
        local st = -1
        if gs ~= nil then pcall(function() st = gs:GetCurState() end) end
        MikuCVSweep(w, gs, MikuCVN % 4 == 0)
        local c = nil
        pcall(function() c = WP.GameMgr:GetCharacter(w) end)
        if c == nil then return end
        local pc = nil
        pcall(function() pc = c.Controller end)
        if pc == nil then return end
        local ui = nil
        pcall(function() ui = UI.GetUI([[PartyZBugRunFight]]) end)
        if ui == nil then return end
        if MikuCVPawn ~= c or MikuCVUi ~= ui or (MikuCVState ~= st and st == 4) then
        MikuCVPawn = c
        MikuCVUi = ui
        MikuCVState = st
        MikuCVHp = -1
        MikuCVMx = -1
        MikuCVSp = -1
        MikuCVSpm = -1
        MikuCVAmmo = -1
        MikuCVWl = -1
        MikuCVScore = -1
        MikuCVBf = -1
        MikuCVCool = 0
        pcall(function() c.IsStandalone = true end)
        pcall(function() c:OnRep_PlayerWeapon() end)
        pcall(function() c:OnRep_WeaponList() end)
        pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        local ps0 = nil
        pcall(function() ps0 = pc.PlayerState end)
        if ps0 ~= nil then
        pcall(function() ps0:OnRep_ChallengeScore() end)
        pcall(function() ps0:OnRep_Level() end)
        pcall(function() ps0:NotifyLocalPlayerItemsChanged() end)
        pcall(function() ps0:NotifyLocalPlayerSkillChanged() end)
        pcall(function() ps0:NotifyLocalPlayerAttachedItemsChanged() end)
        end
        MikuCVRepop = MikuCVRepop - (-1)
        end
        local mx = nil
        local hp = nil
        local ok1 = pcall(function() mx = c:GetAttributeCurValByName([[Health]]) end)
        local ok2 = pcall(function() hp = c:GetAttributeCurValByName([[CurHealth]]) end)
        if ok1 and ok2 and mx ~= nil and hp ~= nil then
        if mx ~= MikuCVMx or hp ~= MikuCVHp then
        MikuCVMx = mx
        MikuCVHp = hp
        pcall(function() ui:OnLocalCharHPChange([[Health]], math.floor(mx)) end)
        pcall(function() ui:OnLocalCharHPChange([[CurHealth]], math.floor(hp)) end)
        end
        end
        local sp = nil
        local spm = nil
        local ok3 = pcall(function() sp = c:GetAttributeCurValByName([[SkillPower]]) end)
        local ok4 = pcall(function() spm = c:GetAttributeCurValByName([[SkillPowerMax]]) end)
        if ok3 and ok4 and sp ~= nil and spm ~= nil then
        if sp ~= MikuCVSp or spm ~= MikuCVSpm then
        MikuCVSp = sp
        MikuCVSpm = spm
        pcall(function() ui:OnLocalCharSkillPowerChange(sp, spm) end)
        end
        end
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil then
        local bar = nil
        pcall(function() bar = wp.BulletNum end)
        if bar ~= nil and bar ~= MikuCVAmmo then
        MikuCVAmmo = bar
        pcall(function() ui:ChangeAmmunitionUI(wp) end)
        end
        end
        local wl = -1
        pcall(function() wl = c.WeaponList:Num() end)
        if wl ~= -1 and wl ~= MikuCVWl then
        MikuCVWl = wl
        pcall(function() c:OnRep_WeaponList() end)
        end
        local ps = nil
        pcall(function() ps = pc.PlayerState end)
        if ps ~= nil then
        local sc = nil
        pcall(function() sc = ps.ChallengeScore end)
        if sc ~= nil and sc ~= MikuCVScore then
        MikuCVScore = sc
        pcall(function() ps:OnRep_ChallengeScore() end)
        end
        end
        local bf = -1
        pcall(function() bf = c.CacheLevelUpBuffs:Num() end)
        if bf ~= -1 and bf ~= MikuCVBf then
        MikuCVBf = bf
        if bf ~= 0 then MikuCVCool = 0 end
        end
        if MikuCVCool ~= 0 then
        MikuCVCool = MikuCVCool - 1
        else
        local csl = nil
        pcall(function() csl = ui.CurSelectList end)
        local wcb = false
        pcall(function() wcb = c.WaitingClientSelectBuff end)
        if csl == nil and wcb == true and bf ~= 0 and bf ~= -1 then
        MikuCVCool = 20
        local okb, eb = pcall(function() c:OnRep_CacheLevelUpBuffs() end)
        end
        end
        if MikuCVN % 60 == 0 then
        local cilc = false
        pcall(function() cilc = c:CheckIsLocallyControlled() end)
        end
        end

        MikuCVTick = function()
        local ok, e = pcall(MikuCVStage)
        UE4.Timer.Add(0.5, MikuCVTick)
        end

        MikuCVBoot = function(left)
        if WP == nil or WP.GameMgr == nil then
        if left ~= 0 then UE4.Timer.Add(1, function() MikuCVBoot(left - 1) end) end
        return
        end
        MikuCVHook()
        local ok, e = pcall(function() EventSystem.On(Event.PartyBtnPress, MikuCVKey) end)
        MikuCVTick()
        end
        MikuCVBoot(600)
        end
        """;
        /// <summary>
        /// v17 (2026-10-09): the fire pump shipped in ZBugHostCoreFix never sent a single RPC,
        /// because MikuCVMuzzle resolved the muzzle with c:GetActorLocation() - a name that does
        /// NOT exist in this game's Lua bindings (UnLua exposes it as K2_GetActorLocation; the
        /// shipped Script tree calls K2_GetActorLocation 957 times and the bare name 0 times).
        /// The muzzle stayed nil, MikuCVDoShot returned before calling anything, and shots stayed
        /// at 0 for every hold. Proven live with /lua: `CVM loc ok=false e=...attempt to call a
        /// nil value (method GetActorLocation)` versus `CVM k2 ok=true e=X=1028.039 Y=1756.039
        /// Z=551.118`, and after repointing the global on the fly the first real call went
        /// This patch redefines MikuCVMuzzle/MikuCVAim (K2_GetActorLocation, plus the weapon
        /// muzzle socket when it resolves) and repoints the globals ZBugHostCoreFix looks up at
        /// call time, so the already deployed pump starts shooting for real. It also adds a
        /// second release listener so a missed fireup can never leave the host firing forever,
        /// and a two second heartbeat reporting muzzle/aim resolution.
        /// </summary>
        public const string ZBugHostShotFix = """
        if MikuCXVer ~= 1 then
        MikuCXVer = 1
        MikuCXN = 0
        MikuCXMuzzle = function(c)
        if c == nil then return nil end
        local p = nil
        pcall(function() p = c:K2_GetActorLocation() end)
        local wp = nil
        pcall(function() wp = c:GetCurWeapon() end)
        if wp ~= nil then
        local s = nil
        pcall(function() s = wp.SkeletalMesh:GetSocketLocation([[muzzle]]) end)
        if s == nil then pcall(function() s = wp.Mesh:GetSocketLocation([[muzzle]]) end) end
        if s == nil then pcall(function() s = wp.WeaponMesh:GetSocketLocation([[muzzle]]) end) end
        local okS = false
        if s ~= nil then
        local okv, vx = pcall(function() return s.X end)
        if okv and vx ~= nil and (vx ~= 0 or s.Y ~= 0 or s.Z ~= 0) then okS = true end
        end
        if okS == true then
        p = s
        else
        local wl = nil
        local ok2 = pcall(function() wl = wp:K2_GetActorLocation() end)
        if ok2 and wl ~= nil then p = wl end
        end
        end
        return p
        end
        MikuCXAim = function(pc, c)
        local loc = nil
        local rot = nil
        pcall(function() loc = pc.PlayerCameraManager:GetCameraLocation() end)
        pcall(function() rot = pc.PlayerCameraManager:GetCameraRotation() end)
        if loc == nil then pcall(function() loc = c:K2_GetActorLocation() end) end
        if rot == nil then pcall(function() rot = pc:GetControlRotation() end) end
        if loc == nil or rot == nil then return nil end
        local fwd = nil
        local ok3 = pcall(function() fwd = UE4.UKismetMathLibrary.GetForwardVector(rot) end)
        if not ok3 or fwd == nil then return nil end
        return UE4.FVector(loc.X - (0 - fwd.X * 10000), loc.Y - (0 - fwd.Y * 10000), loc.Z - (0 - fwd.Z * 10000))
        end
        MikuCVMuzzle = MikuCXMuzzle
        MikuCVAim = MikuCXAim
        MikuCXWatch = function(t, p)
        if t == 5 and (p == 0 or p == false) then MikuCVDown = false end
        end
        pcall(function() EventSystem.On(Event.PartyBtnPress, MikuCXWatch) end)
        MikuCXStage = function()
        MikuCXN = MikuCXN - (-1)
        local w = nil
        pcall(function() w = WP.GameMgr:GetWorld() end)
        if w ~= nil and MikuCVHost ~= nil then
        local okh, host = pcall(MikuCVHost, w)
        if okh and host == true then
        local c = nil
        pcall(function() c = WP.GameMgr:GetCharacter(w) end)
        if c ~= nil then
        local m = MikuCXMuzzle(c)
        local pc = nil
        pcall(function() pc = WP.GameMgr:GetMyPlayerController(w) end)
        local t = nil
        if pc ~= nil then t = MikuCXAim(pc, c) end
        if MikuCXN % 20 == 0 then
        end
        end
        end
        end
        UE4.Timer.Add(2, MikuCXStage)
        end
        MikuCXStage()
        end
        """;
    /// <summary>
    /// 2026-10-09 freeze - the consolidated host layer. This is NOT new code: it is the byte-exact
    /// concatenation of the three host layers that used to be pushed separately, in dependency order
    /// (ClarityFix v15 -> CoreFix v16 -> ShotFix v17). Composing it here keeps one maintained copy of
    /// each layer instead of triplicating 20k of Lua, while the wire payload stays a single script with
    /// no forbidden characters (validated by .temp/compose_final.py). Guard variables are unchanged, so
    /// a client that already executed v15/v16/v17 simply skips the resent code.
    /// </summary>
    public const string ZBugHostFinalFix = ZBugHostClarityFix + "\n" + ZBugHostCoreFix + "\n" + ZBugHostShotFix;
}
