-- EraAudit Lua harness (L1.5).
--
-- Executes the payloads emitted by
--   dotnet run --project tests/EraAudit -c Release -- --emit tests/EraAudit/out
-- inside a mocked client table set, so the shipped Lua is parsed and run for real instead of
-- being string-matched. The runtime is Lua 5.3 (fengari); the payload subset used here is
-- 5.1-compatible (no goto, no integer division, no bitwise operators), which is why the same
-- source runs on the client's Lua 5.1.
--
-- Each scenario builds a fresh environment table so `loadfile(path, "t", env)` binds the payload's
-- _ENV to it; the payload's global assignments (MikuTryRaw, MikuPoolFloor, ...) stay per-scenario.

local DIR = ERA_OUT_DIR or "tests/EraAudit/out"

local FLOOR = 1769630400 -- DLC 35 EnterStartTime, rendered into every payload
local STOP = 1770000000
local NOW = 1769700000

local passed = 0
local failures = {}

local function check(name, ok, detail)
    if ok then
        passed = passed + 1
        return
    end
    failures[#failures + 1] = detail ~= nil and (name .. ": " .. tostring(detail)) or name
end

local function scenario(name, fn)
    local ok, err = pcall(fn)
    if not ok then
        failures[#failures + 1] = name .. " raised: " .. tostring(err)
    end
end

local function env(t)
    return setmetatable(t or {}, { __index = _G })
end

local function run(file, e)
    local chunk, err = loadfile(DIR .. "/" .. file, "t", e)
    if not chunk then
        error(file .. ": " .. tostring(err), 0)
    end
    local ok, raised = pcall(chunk)
    if not ok then
        error(file .. " raised: " .. tostring(raised), 0)
    end
end

local function fmt(t)
    return os.date("%Y%m%d%H%M", t)
end

local function parse(s)
    if type(s) ~= "string" or #s ~= 12 then
        error("bad time " .. tostring(s), 0)
    end
    return os.time({
        year = tonumber(s:sub(1, 4)),
        month = tonumber(s:sub(5, 6)),
        day = tonumber(s:sub(7, 8)),
        hour = tonumber(s:sub(9, 10)),
        min = tonumber(s:sub(11, 12)),
        sec = 0,
    })
end

-- ---------------------------------------------------------------------------
-- GlobalTimeWindows: [floor, stop) membership, keep the shipped gate otherwise
-- ---------------------------------------------------------------------------
scenario("GlobalTimeWindows", function()
    local loads = 0
    local e = env({
        IsInTime = function(s) return s == 4242 end, -- only this window is open in the shipped client
        GetTime = function() return NOW end,
        Activity = { LoadActivitiesConfig = function() loads = loads + 1 end },
    })
    run("GlobalTimeWindows.lua", e)

    check("gtw reloads the activity table once", loads == 1, loads)
    check("gtw keeps an already-open window open", e.IsInTime(4242, FLOOR + 10, NOW) == true)
    check("gtw reopens an expired window inside the era", e.IsInTime(FLOOR - 100, FLOOR + 100, NOW) == true)
    check("gtw keeps a window ending at the era floor closed", e.IsInTime(FLOOR - 200, FLOOR, NOW) == false)
    check("gtw keeps a window starting at the era stop closed", e.IsInTime(STOP, STOP + 100, NOW) == false)
    check("gtw reopens a window spanning the era", e.IsInTime(FLOOR - 10, STOP + 10, NOW) == true)
    check("gtw rejects an open-ended start", e.IsInTime(-1, STOP - 1, NOW) == false)
    check("gtw rejects a missing end", e.IsInTime(FLOOR, nil, NOW) == false)
    check("gtw records the era it pinned", e.Activity.bMikuEraReset == 35, e.Activity.bMikuEraReset)

    run("GlobalTimeWindows.lua", e)
    check("gtw is idempotent on a repeat push", loads == 1, loads)

    local off = env({
        IsInTime = function() return false end,
        GetTime = function() return NOW end,
        Activity = { LoadActivitiesConfig = function() end },
    })
    run("GlobalTimeWindows.off.lua", off)
    check("gtw off never reopens anything", off.IsInTime(FLOOR, FLOOR + 100, NOW) == false)
end)

-- ---------------------------------------------------------------------------
-- ActivityFaceQuiet: (era, faceId) dedupe written only after a real show
-- ---------------------------------------------------------------------------
local function faceEnv(showFn, rawFn)
    return env({
        MikuRawIsInTime = rawFn or function() return false end,
        GetTime = function() return NOW end,
        Activityface = {
            GetConfig = function(id) return _G.ERA_FACES[id] end,
            CheckDateTime = showFn or function() return true end,
            tbPopFaceId = {},
        },
    })
end

scenario("ActivityFaceQuiet", function()
    ERA_FACES = {
        [5001] = { tStarttime = FLOOR + 10, tEndtime = STOP - 10 },  -- in era, expired
        [5002] = { tStarttime = FLOOR - 200, tEndtime = FLOOR },     -- ends at the floor
        [5003] = { tStarttime = STOP, tEndtime = STOP + 50 },        -- starts at the stop
        [5004] = { tStarttime = FLOOR + 1, tEndtime = STOP - 1 },    -- open in the shipped table
    }

    local e = faceEnv()
    run("ActivityFaceQuiet.lua", e)

    check("afq shows an in-era splash once", e.Activityface.CheckDateTime(5001) == true)
    check("afq suppresses the same splash on the second query", e.Activityface.CheckDateTime(5001) == false)
    check("afq keys the mark by era and face", e.Activityface.tbMikuEraFaceShown["355001"] == true)
    check("afq refuses a splash ending at the floor", e.Activityface.CheckDateTime(5002) == false)
    check("afq refuses a splash starting at the stop", e.Activityface.CheckDateTime(5003) == false)

    e.MikuRawIsInTime = function(s) return s == FLOOR + 1 end
    check("afq passes a shipped-open splash through", e.Activityface.CheckDateTime(5004) == true)
    check("afq does not mark a shipped-open splash", e.Activityface.tbMikuEraFaceShown["355004"] == nil)
    check("afq still passes it on the second query", e.Activityface.CheckDateTime(5004) == true)

    local quiet = faceEnv(function(id) return id ~= 5001 end)
    run("ActivityFaceQuiet.lua", quiet)
    check("afq does not consume the mark when nothing showed",
        quiet.Activityface.CheckDateTime(5001) == false and quiet.Activityface.tbMikuEraFaceShown["355001"] == nil)

    local broken = faceEnv(function() error("boom", 0) end)
    run("ActivityFaceQuiet.lua", broken)
    local ok, err = pcall(broken.Activityface.CheckDateTime, 5001)
    check("afq propagates the shipped error", not ok and tostring(err):find("boom", 1, true) ~= nil, err)
    check("afq does not mark after an error", broken.Activityface.tbMikuEraFaceShown["355001"] == nil)

    e.Activityface.tbPopFaceId = { 7 }
    e.Activityface.bLoginFirst = true
    run("ActivityFaceQuiet.lua", e)
    check("afq clears the popup queue on a repeat push",
        next(e.Activityface.tbPopFaceId) == nil and e.Activityface.bLoginFirst == false)

    local off = faceEnv()
    run("ActivityFaceQuiet.off.lua", off)
    check("afq off refuses an expired splash", off.Activityface.CheckDateTime(5001) == false)
end)

-- ---------------------------------------------------------------------------
-- GachaTryWindows: only the assigned ids, restore before every re-stamp
-- ---------------------------------------------------------------------------
local function tryRows()
    return {
        [561] = { nId = 561, nStartTime = FLOOR + 10, nEndTime = STOP - 10, tbCondition = { a = 1 } },
        [576] = { nId = 576, nStartTime = FLOOR - 100, nEndTime = FLOOR, tbCondition = { b = 2 } },
        [700] = { nId = 700, nStartTime = FLOOR + 10, nEndTime = STOP - 10, tbCondition = { c = 3 } },
    }
end

local function tryEnv(rows)
    return env({
        GetTime = function() return NOW end,
        Activity = { tbConfig = rows, GetOpenTime = function() return 0 end, LoadActivitiesConfig = function() end },
        DLC_Logic = { GetCurConf = function() return {} end, InitFuncObj = function() end },
    })
end

scenario("GachaTryWindows", function()
    local rows = tryRows()
    local e = tryEnv(rows)
    run("GachaTryWindows.lua", e)

    check("try stamps the assigned row", rows[561].nStartTime == NOW - 3600 and rows[561].nEndTime == NOW + 2592000,
        tostring(rows[561].nStartTime))
    check("try clears the condition of the assigned row", next(rows[561].tbCondition) == nil)
    check("try leaves a row ending at the floor closed",
        rows[576].nStartTime == FLOOR - 100 and rows[576].nEndTime == FLOOR)
    check("try keeps that row's condition", rows[576].tbCondition.b == 2)
    check("try never touches a row outside the id list",
        rows[700].nStartTime == FLOOR + 10 and rows[700].tbCondition.c == 3)
    check("try snapshots only the assigned row",
        e.MikuTryRaw[561] ~= nil and e.MikuTryRaw[576] == nil and e.MikuTryRaw[700] == nil)

    e.Activity.GetOpenTime = function() return FLOOR + 20 end
    e.MikuTryWin2 = nil
    run("GachaTryWindows.lua", e)
    check("try prefers the server open time", rows[561].nStartTime == FLOOR + 20, rows[561].nStartTime)

    local before = rows[561].nStartTime
    run("GachaTryWindows.lua", e)
    check("try is idempotent for the same snapshot", rows[561].nStartTime == before)

    rows[561].nStartTime = 12345
    e.Activity.LoadActivitiesConfig()
    check("try reload restores then re-stamps", rows[561].nStartTime == FLOOR + 20, rows[561].nStartTime)

    local offRows = tryRows()
    local off = tryEnv(offRows)
    run("GachaTryWindows.lua", off)
    run("GachaTryWindows.off.lua", off)
    check("try off restores the assigned row",
        offRows[561].nStartTime == FLOOR + 10 and offRows[561].nEndTime == STOP - 10 and offRows[561].tbCondition.a == 1,
        tostring(offRows[561].nStartTime))
    check("try off drops the snapshot", off.MikuTryRaw ~= nil and next(off.MikuTryRaw) == nil)

    local empty = tryEnv(tryRows())
    run("GachaTryWindows.empty.lua", empty)
    check("try with no ids never stamps a row", empty.MikuTryRaw ~= nil and next(empty.MikuTryRaw) == nil)
end)

-- ---------------------------------------------------------------------------
-- GachaPoolWindows: membership, restore, module-absent require hook
-- ---------------------------------------------------------------------------
local function poolEnv(pools, extra)
    local t = {
        GetTime = function() return NOW end,
        ParseTime = parse,
        Gacha = { tbGacha = pools, LoadCfg = function() end },
    }
    for k, v in pairs(extra or {}) do t[k] = v end
    return env(t)
end

scenario("GachaPoolWindows", function()
    local pools = {
        [1] = { tbPoolTime = { fmt(FLOOR - 100), fmt(FLOOR + 100) } },   -- in era
        [2] = { tbPoolTime = { fmt(FLOOR - 5000), fmt(FLOOR - 1000) } }, -- older era
        [3] = {},                                                        -- shipped without a window
        [4] = { tbPoolTime = { fmt(FLOOR + 100), fmt(FLOOR + 100) } },   -- zero length
    }
    local lo, hi = fmt(NOW - 3600), fmt(NOW + 2592000)
    local e = poolEnv(pools)
    run("GachaPoolWindows.lua", e)

    check("pool opens the in-era banner",
        pools[1].tbPoolTime[1] == lo and pools[1].tbPoolTime[2] == hi, pools[1].tbPoolTime[1])
    check("pool restores an older banner instead of dropping it",
        pools[2].tbPoolTime[1] == fmt(FLOOR - 5000) and pools[2].tbPoolTime[2] == fmt(FLOOR - 1000))
    check("pool leaves a banner with no window alone", pools[3].tbPoolTime == nil)
    check("pool keeps a zero-length banner closed", pools[4].tbPoolTime[1] == fmt(FLOOR + 100))

    run("GachaPoolWindows.off.lua", e)
    check("pool off restores the shipped window",
        pools[1].tbPoolTime[1] == fmt(FLOOR - 100) and pools[1].tbPoolTime[2] == fmt(FLOOR + 100))

    run("GachaPoolWindows.lua", e)
    e.Gacha.LoadCfg()
    check("pool reload re-applies after clearing the guard",
        e.MikuPoolFloor == FLOOR and pools[1].tbPoolTime[1] == lo)

    local stub = function() return {} end
    local absent = poolEnv(nil, { require = stub })
    absent.Gacha = { LoadCfg = function() end } -- module table not built yet: tbGacha is absent
    run("GachaPoolWindows.lua", absent)
    check("pool hooks require when the module is absent", absent.MikuPoolHook == true and absent.require ~= stub)
    absent.Gacha.tbGacha = { [9] = { tbPoolTime = { fmt(FLOOR - 3600), fmt(FLOOR + 3600) } } }
    absent.require("Gacha")
    check("pool applies on the next require", absent.Gacha.tbGacha[9].tbPoolTime[1] == lo)
    check("pool unhooks require after a successful apply", absent.require == stub)
end)

-- The patched tables re-time the whole gacha.txt and drop the pinned era's group rows, so the payload
-- has to supply both: the 24 pool windows of DLC 35 and the five group rows that name them. This is the
-- regression that put every later banner in the tree under one borrowed group (the 170+ rows the old
-- fallback appended), so the later pools below must stay shut and the roster must be exactly 24 rows.
scenario("GachaPoolWindows roster", function()
    local rosterIds = {
        191, 192, 91, 92, 193, 194, 93, 94, 179, 180, 79, 80,
        181, 182, 81, 82, 195, 196, 95, 96, 197, 198, 97, 98,
    }
    local pools = {}
    for id = 1, 210 do
        -- Everything the shipped table carries sits in a later window, i.e. outside this era.
        pools[id] = { tbPoolTime = { "202604020400", "202604090400" } }
    end

    local e = poolEnv(pools)
    e.Gacha.tbDisplayGroup = {}
    local disp = e.Gacha.tbDisplayGroup
    run("GachaPoolWindows.lua", e)

    local lo, hi = fmt(NOW - 3600), fmt(NOW + 2592000)
    check("roster opens every pool of the era", pools[191].tbPoolTime[1] == lo and pools[98].tbPoolTime[2] == hi,
        pools[191].tbPoolTime[1])
    local wantCount = 0
    for _ in pairs(e.MikuRosterWant) do wantCount = wantCount + 1 end
    check("roster opens exactly the 24 ids", #e.MikuRosterRows == 24 and wantCount == 24,
        tostring(#e.MikuRosterRows) .. "/" .. tostring(wantCount))
    check("roster leaves a later pool closed", pools[210].tbPoolTime[1] == "202604020400",
        pools[210].tbPoolTime[1])
    check("roster adds 24 display rows", #disp == 24, tostring(#disp))
    check("roster keeps file order and prepends", disp[1].nGachaId == 191 and disp[1].sGroupName == "card.girl013_suits",
        tostring(disp[1] and disp[1].nGachaId))
    check("roster marks the limited groups", disp[1].nGropMark == 1 and disp[1].nGroupIcon == 130510006)
    check("roster leaves the unmarked groups unmarked", disp[9].nGropMark == nil and disp[9].nGroupIcon == 130470004)
    check("roster names the multi pool banner", disp[17].sGroupName == "gacha.diyup3" and disp[17].nGropMark == 2
        and disp[17].nGachaId == 195, tostring(disp[17] and disp[17].sGroupName))
    check("roster rows never share an id", disp[1].nId ~= disp[2].nId and disp[1].nId < 0)

    run("GachaPoolWindows.lua", e)
    check("roster is idempotent", #disp == 24, tostring(#disp))

    e.Gacha.LoadCfg()
    check("roster survives a LoadCfg rebuild", #disp == 24 and disp[1].nGachaId == 191, tostring(#disp))

    run("GachaPoolWindows.off.lua", e)
    check("roster off removes every row it added", #disp == 0, tostring(#disp))
end)


-- ---------------------------------------------------------------------------
-- IbshopWindows / FarmWarWindows: snapshot, restore, rebuilt-table handling
-- ---------------------------------------------------------------------------
scenario("IbshopWindows", function()
    local list = { [1] = { nStartTime = 1, nEndTime = 2, nShowStartTime = 3, nShowEndTime = 4 } }
    local group = { [10] = { { nStartTime = 5, nEndTime = 6 } } }
    local goods = { [100] = { nStartTime = 7, nEndTime = 8 } }
    local e = env({
        GetTime = function() return NOW end,
        IBLogic = { tbIbShopList = list, tbIbShopGroup = group, tbIbGoods = goods },
    })
    local lo, hi = NOW - 3600, NOW + 2592000
    run("IbshopWindows.lua", e)

    check("ibshop stamps the shop list", list[1].nStartTime == lo and list[1].nEndTime == hi)
    check("ibshop stamps the show window too", list[1].nShowStartTime == lo and list[1].nShowEndTime == hi)
    check("ibshop stamps the group and goods", group[10][1].nStartTime == lo and goods[100].nEndTime == hi)
    check("ibshop snapshots every stamped row", #e.IBLogic.bMikuWindowRaw == 3, #e.IBLogic.bMikuWindowRaw)

    run("IbshopWindows.off.lua", e)
    check("ibshop off restores the shop list", list[1].nStartTime == 1 and list[1].nShowStartTime == 3)
    check("ibshop off restores group and goods", group[10][1].nEndTime == 6 and goods[100].nStartTime == 7)

    run("IbshopWindows.lua", e)
    local rebuilt = { [2] = { nStartTime = 11, nEndTime = 12 } }
    e.IBLogic.tbIbShopList = rebuilt
    run("IbshopWindows.lua", e)
    check("ibshop re-stamps a rebuilt table", rebuilt[2].nStartTime == lo)
    check("ibshop restores the previous table before dropping it", list[1].nStartTime == 1)
end)

scenario("FarmWarWindows", function()
    local act = { [1] = { nStartTime = 1, nEndTime = 2 } }
    local chap = { [1] = { { nStartTime = 3, nEndTime = 4 } } }
    local lvl = { [1] = { nStartTime = 5, nEndTime = 6 } }
    local e = env({
        GetTime = function() return NOW end,
        FarmWarActivityCfg = { tbActivityCfg = act, tbChapter = chap, tbLevel = lvl, LoadCfg = function() end },
    })
    local lo, hi = NOW - 3600, NOW + 2592000
    run("FarmWarWindows.lua", e)

    check("farm stamps every window",
        act[1].nStartTime == lo and chap[1][1].nEndTime == hi and lvl[1].nStartTime == lo)

    run("FarmWarWindows.off.lua", e)
    check("farm off restores every window",
        act[1].nStartTime == 1 and chap[1][1].nEndTime == 4 and lvl[1].nEndTime == 6)

    run("FarmWarWindows.lua", e)
    e.FarmWarActivityCfg.LoadCfg()
    check("farm reload re-stamps", act[1].nStartTime == lo)
end)

-- ---------------------------------------------------------------------------
print(string.format("EraAudit(Lua): %d check(s) passed, %d failed.", passed, #failures))
for _, failure in ipairs(failures) do
    print("  FAIL " .. failure)
end

ERA_AUDIT_FAILED = #failures
