# 测试自动化与接口能力手册

> 状态：2026-11-11 实测。本文档里的每条命令都在本机跑过，括号里的数字是当日结果。
> 相关文档：[开发排障流程](dev-debug-workflow.md)（取证循环）、主仓 `docs/contracts/`（模块契约）。
> 本文只描述**已经存在**的接口；尚未实现的项集中在 §9。

---

## 0 一条命令

```powershell
pwsh -File tools/test.ps1                 # gate + L1 + L1.5；服务端在跑时自动加 L2
pwsh -File tools/test.ps1 -SkipL2         # 明确不要 L2
pwsh -File tools/test.ps1 -Caps           # 再加一步：用 tools/caps.json 校验客户端 MIKU_CAP
pwsh -File tools/test.ps1 -Token <t> -L2Seconds 15
```

- 退出码：`0` 全部执行过的步骤通过（skip 不算失败），`1` 任一步失败。
- 结果同时写到 `build/test-results.json`，`build.ps1` 会把它原文写进 `BUILD-INFO.txt`
  ——发布包里的“测试跑过没有”从此来自实跑记录，而不是手写的一句话。
- 跳过的步骤一定打印原因（“没有服务端在 21000”“找不到 fengari”“-SkipL2”），不会静默通过。

---

## 1 分层与边界

| 层 | 入口 | 需要 | 当日结果 | 能证明 | 不能证明 |
| --- | --- | --- | --- | --- | --- |
| gate | `python tools/check_client_patches.py` | Python | 43 载荷 / 0 违禁 | 载荷能被客户端解码 | 载荷逻辑对不对 |
| L1 | `tests/EraAudit` | .NET 10 | 378 断言通过 | 时间窗算术、载荷结构、字符集、占位符 | 客户端真的会执行 |
| L1.5 | `tests/EraAudit/lua/run.js` | Node + fengari | 56 断言通过 | 载荷在 Lua VM 里的行为 | Lua 5.1 与 5.3 的差异 |
| L2 | `tests/EraSmoke` | 运行中的服务端 + token | 需起服 | 登录后**线上字节**里的载荷与属性 | 运行期换期、发奖、DB 落库 |
| 冒烟 | `.temp/smoke` | 运行中的服务端 + token | 需起服 | api 可达、回包帧数 | 回包字段语义 |
| 静态审计 | `scripts/*_audit.py` | 客户端脚本 + 服务端源码 | 按需 | 客户端读点 vs 服务端写点 | 运行期状态 |
| 日志 | `tools/devlog.py` + `MIKU_CAP` | 一次真实登录 | 已连通 | 客户端内部事实、崩溃/贴图错误 | 无断言，需人判读 |
| L3 | 人眼 + `.temp/gd.ps1` | 客户端窗口 | 手工 | 最终可见效果 | 不可回归 |

设计原则：**每一层只断言它能证明的东西**。L1 不假装能证明渲染，L2 不假装能证明 DB，
L3 不做成自动门禁。

---

## 2 接口 A：GM Lua 通道

### 2.1 线上格式

服务端通过 `CallGSRouter.SendScript(connection, "GM", payload)` 下发，落到客户端是
`CmdIds.NtfScript = 1010`，`Api = "GM"`，`Arg` 是 JSON 文本，形状固定为：

```json
{"code": "<要执行的 Lua 源码>"}
```

### 2.2 客户端执行点

`Script/Misc/GM.lua:9`（客户端 3.6 解包副本：`.temp/lua_probe/script_all/Game/Content/Script/Misc/GM.lua`）：

```lua
s2c.Register('GM', function(tbParam)
    if not string.find(tbParam.code, "--TDebug Callback:") then
        assert(load(tbParam.code))()
    else
        _G.oldPrint = _G.print
        xpcall(GM.DoScript, GM.ErrorTraceback, tbParam)
        _G.print = _G.oldPrint
    end
end)
```

也就是说：**服务端发什么，客户端就以自己的权限执行什么**。`print` 会进 `Game.log` 的
`LogUnLua:` 行——这是 §4 诊断通道的基础。

### 2.3 服务端两个发送入口

| 入口 | 位置 | 用途 |
| --- | --- | --- |
| 登录批量下发 | `GameServer/Server/Packet/Recv/Login/HandlerReqLogin.cs` 的 `SendClientPatch`（53 处调用） | 修 era 窗口、卡池、商店、活动面板等 |
| 运行期单发 | `GameServer/Command/Commands/CommandLua.cs` → `/lua <lua code> [@uid]` | 代替鼠标：开界面、结算关卡、临时探针 |

`/lua` 的注释写明了它存在的理由：*合成鼠标输入在独占全屏下很脆弱，让客户端按命令开 UI 或结束关卡，
比按像素点击可靠*。权限为 `Admin` / `Support`，目标必须在线。

### 2.4 三重丢弃（静默失败的来源）

`SendClientPatch(connection, name, lua)`（`HandlerReqLogin.cs:1132`）在发送前拒绝三类载荷，
**只往 `Server.log` 打一行 Error 然后 return**，客户端侧表现为“补丁没生效”而没有任何报错：

| 条件 | 日志关键字 | 原因 |
| --- | --- | --- |
| 含 `+ < > & ' "` 任一字符 | `contains '<c>', which the serializer escapes to \uXXXX` | `System.Text.Json` 转义，客户端旧解码器崩 |
| 含非 ASCII（`ch > 0x7E`） | `contains non-ASCII characters` | 同上 |
| 仍有未替换的 `MIKU_` 占位符 | `still holds the unsubstituted placeholder` | 渲染参数没传全 |

排查：`grep -n "dropping" Config/Logs/Server.log`。

---

## 3 载荷编写规范

客户端是 **Lua 5.1**（无 `goto`、无 `//`、无位运算）。载荷必须同时满足：

| 约束 | 做法 |
| --- | --- |
| 禁用 `+ < > & ' "` | 字符串用 `[[...]]`；比较用 `~=`；累加用 `table.insert(o, x)`；拼接用 `..` |
| 纯 ASCII | 注释也用英文 |
| 模板占位符必须替换 | `MIKU_ACT_FLOOR` 等由 C# 侧 `Replace`，`EraAudit` 会断言渲染结果里没有 `MIKU_` |
| 目标模块可能还没 `require` | 用 require 钩子 + 幂等守卫（`MikuPoolHook`、`MikuTryHooked2`、`bMikuFarmHook`） |
| 一次性操作要能重试 | `UE4.Timer.Add(N, function() ... end)` 自驱重试，例：`DlcClientPatches.cs:2202` |

最小模板（`Miku` 前缀的全局变量就是幂等守卫）：

```lua
local function mikuTry()
if not SomeModule or not SomeModule.Logic then
UE4.Timer.Add(1, function() mikuTry() end)
return
end
if MikuSomeHooked then return end
MikuSomeHooked = true
-- 只读地观察，或按需改窗口
print([[MIKU_CAP some_fact ]] .. tostring(SomeModule.Logic.Value))
end
mikuTry()
```

门禁：`python tools/check_client_patches.py`（扫描 `DlcClientPatches.cs` 里所有
`const string NAME = """` 块，逐字符比对禁用表）。新增载荷后必须跑它，或直接跑 `tools/test.ps1`。

---

## 4 接口 B：`MIKU_CAP` 诊断通道

### 4.1 产生与落点

`DlcClientPatches.DevDiagnostics`（`DlcClientPatches.cs:1920`）在登录时无条件下发，
内部统一用：

```lua
local function mcap(tag, body) print([[MIKU_CAP ]] .. tostring(tag) .. [[ ]] .. tostring(body)) end
```

`print` → `Game.log` 的 `LogUnLua:` 行 → `python tools/devlog.py cap` 解析成 `<tag> <value>`。

### 4.2 块语义

一次登录打印一个块，以 `done 1` 结尾；如果外层 `pcall` 失败，会打印 `MIKU_CAP error <msg>`
并且**没有** `done`。`devlog.py cap` 只判读**最后一个块**（最后两个 `done` 之间到文件末尾），
所以旧会话的采集值不会污染当前结论。

### 4.3 现役 tag

| tag | 类型 | 含义 |
| --- | --- | --- |
| `gacha_ids` | id 列表 | `Gacha.tbGacha` 的键集合；空表 = 卡池配置没加载 |
| `display` | `k=v` 映射 | `tbDisplayGroup` → `nGachaId`；指向不存在的池 = 空 banner |
| `curdlc` | int | `DLC_Logic.GetCurConf().nId`，客户端实际开出来的时代 |
| `online16_level` | int / `nil` | `Online.GetConfig(16).ResetLevel`；`nil` = 该行缺失 |
| `boss5` | int | `OnlineBoss.GetConfig(5).nId` |
| `pool_in_time` | id 列表 | 客户端当前认为在时间窗内的池；空是合法答案 |
| `pool_floor` | int / `nil` | `MikuPoolFloor`，由 `GachaPoolWindows` 写入 |
| `done` | 固定 `1` | 块结束哨兵 |
| `error` | 文本 | 仅在 pcall 失败时出现 |

### 4.4 契约校验

契约文件 `tools/caps.json`，每条声明 `type`（`int` / `id_list` / `map` / `flag` / `bool` / `text`）、
`required`、`nullable`、`min`/`max`、`allow_empty`、`forbidden`、`value`。

```powershell
python tools/devlog.py cap --expect                     # 默认读 tools/caps.json，退出码 0/1
python tools/devlog.py cap --expect tools/caps.json --json build/caps.json --strict
```

判定规则：必需项缺失 → FAIL；`forbidden` 项出现 → FAIL；类型/范围不符 → FAIL；
契约里没登记的 tag → WARN（加 `--strict` 变 FAIL）。当日实测：8 行、0 failure。

### 4.5 加一个 tag

1. 在 `DevDiagnostics` 里加一行 `mcap([[new_tag]], ...)`，只读、`tostring`、单行；
2. 在 `tools/caps.json` 的 `tags` 里登记类型与 `why`；
3. 在本节表格加一行；
4. 登录一次，`python tools/devlog.py cap --expect` 应 PASS。
   临时探针也走这条通道——**不要**往 `MikuSB-Patch` 注入 DLL 里加调试逻辑。

---

## 5 日志与视觉接口

### 5.1 `tools/devlog.py`

| 子命令 | 作用 |
| --- | --- |
| `paths` | 解析客户端路径（`Loader.GamePath` → `Config.Local.json` → `Setup.json`/`Loader.json` → 常见安装位），列出三份证据源是否存在 |
| `cap` / `cap --expect` | 采集值 / 契约校验（§4） |
| `tail --lines N` | `Game.log` 末尾 N 行 |
| `watch --since 10m` | 最近 N 分钟的增量（按行内时间戳） |
| `errors` | 按 `SetTexture Error` / `[XGSDK]` / `LogWindows: Error` 分组计数并给 5 条样本 |
| `pair --around HH:MM` | 同一时刻客户端与服务端两侧对齐 |

全部只读：不碰进程、不写文件（`--json` 除外）。

### 5.2 `.temp/gd.ps1`（游戏驱动 + 截图）

| 参数 | 说明 |
| --- | --- |
| `-Click x,y` | 真实屏幕像素点击（窗口在 0,0，2560x1600） |
| `-Keys "esc"` | 发送按键 |
| `-Crop "0,0,700,500"` | 截图裁剪区域 |
| `-Tag s` | 截图文件名标签 |
| `-NoFocus` / `-NoShot` | 不抢焦点 / 只操作不截图 |
| `-Wait 900` / `-MaxWidth 1600` | 操作后等待毫秒数 / 截图最大宽度 |

`captureloop.ps1 -Count 24 -Every 7` 是它的批处理封装：每 7 秒抓一次，用来抓“挂一会儿才出现”的现象。

---

## 6 协议层测试接口

### 6.1 `tests/EraAudit`（L1）

```powershell
dotnet run --project tests/EraAudit/EraAudit.csproj -c Release -- --emit build/eraaudit-out
```

- `EnableDefaultCompileItems=false`，直接 `Compile Include` 生产源码（`DlcClientPatches.cs`、
  `DlcEraWindow.cs`），所以断言跑的是**真实现**而不是副本；
- 覆盖：半开区间边界表（`floor=1769630400`、`stop=1770000000`）、每个载荷的禁用字符与 ASCII、
  6 个 era 载荷渲染后无 `MIKU_` 占位符、`GachaTryWindows` 的集合结构、Off 分支的早退、
  `AuditEraPayloads()` 为空；
- `--emit` 落 13 个真实下发载荷，供 L1.5 执行。

### 6.2 `tests/EraAudit/lua/run.js`（L1.5）

```powershell
node tests/EraAudit/lua/run.js --out <绝对路径> --fengari <node_modules 绝对路径>
```

- 用 fengari（Lua 5.3 代跑 5.1）加载 `--emit` 产物，每个场景独立 `_ENV`；
- 必须传绝对路径：`run.js` 用 `require(path.join(fengari,'fengari'))`，相对路径会被当成模块名 →
  `MODULE_NOT_FOUND`；
- 退出码：0 通过 / 1 断言失败 / 2 harness 错误；
- 已知限制：5.3 语义 ≠ 5.1 语义（例如整型除法、`goto` 相关行为），Lua 5.1 真机仍属未运行。

### 6.3 `tests/EraSmoke`（L2）

```powershell
dotnet run --project tests/EraSmoke -- <comboToken> 12
```

- 4 字节小端帧（`[u32 LE = 2+len][u16 cmdId][protobuf]`），真实 `ComboToken` 登录；
- 只观察服务端**主动推送**：`NtfScript(1010)` 且 `Api=="GM"`、`NtfSyncAttr(1004)`；
- 断言针对上线字节：载荷不含禁用字符、不含 `MIKU_`、信封是 `{"code":`、6 个 era 标记载荷到齐、
  试用载荷携带的是集合而不是序列；
- 依赖 `MikuSB/bin/<Configuration>/net10.0/MikuProto.dll`（先构建 `MikuSB/MikuSB.csproj`）；
- 限制（README 自述）：不驱动 `/dlc` 运行期换期、不校验 DB、不覆盖发奖。

### 6.4 `.temp/smoke`（协议冒烟）

```powershell
dotnet run --project .temp/smoke -- <loginToken> [labelFilter] [weaponOverride] [holdSeconds]
```

240 个帧用例，覆盖客户端为已修/新增链路实际发出的 CallGS；输出每个 cmd 的回包帧数与
“其它脚本”列表。它断言**可达性**，不校验字段语义。

### 6.5 静态审计

| 脚本 | 回答的问题 | 产物 |
| --- | --- | --- |
| `scripts/proto_audit.py` | 客户端调了、服务端没有 handler 的 api；结算 api 有没有路由分支 | `.temp/proto_audit_report.txt` |
| `scripts/reply_contract_audit.py` | 客户端回调读哪些字段、服务端发哪些字段；缺字段/无 nil 守卫 | 终端 + `--json` |
| `scripts/attr_writer_audit.py` | 客户端在读但服务端从没写过的 `(gid,sid)` | `docs/contracts/coverage.md` |
| `scripts/resource_load_assert.py` | `Loaded N` 与资源表行数是否一致 | 终端 |
| `scripts/vircapture_contract_audit.py` | 新星开拓 api 集合与基线 `docs/contracts/vircapture_baseline.json` 的差异 | 终端，退出码 2 = 回归 |

---

## 7 扩展指南

**A. 加一条 L1 断言**：在 `tests/EraAudit/Program.cs` 的 `Check(name, ok, detail)` 链上加一行，
用真实生成函数（如 `DlcClientPatches.XxxWindows(...)`）的输出做输入；失败要打印可定位的 detail。

**B. 加一个 L1.5 场景**：先在 L1 里 `--emit` 一个载荷文件，再在 `run.lua` 里加一个场景
（独立 `_ENV` + `loadfile`），断言载荷对替身模块的副作用。

**C. 加一个 `MIKU_CAP` tag**：见 §4.5。

**D. 加一条 L2 断言**：在 `EraSmoke/Program.cs` 的 `Verify(ok, what)` 链上加；只能断言
“服务端主动推了什么”，不要在这里发请求去驱动业务流程。

**E. 加一个协议冒烟用例**：在 `.temp/smoke/Program.cs` 的 `BuildCases()` 里 `yield return`
一个 `(label, api, arg)`；跑一次确认回包帧数 > 0。

---

## 8 故障排查

| 现象 | 原因 / 处理 |
| --- | --- |
| `MODULE_NOT_FOUND`（run.js） | `--fengari` 传了相对路径；改用绝对路径 |
| `MSB4276` / `MSB4186` | 便携 SDK 缺 workload locator；加 `-m:1 -nodeReuse:false -p:MSBEnableWorkloadResolver=false`（`tools/test.ps1` 已内置） |
| L2 被 skip：`no server on 127.0.0.1:21000` | 先起服务端；或 `-SkipL2` |
| L2 被 skip：`no ComboToken` | `-Token <t>`，或确认 `Config/Database/Miku.db` 的 `Account.ComboToken` 非空 |
| 载荷“发了但没生效” | `grep -n "dropping" Config/Logs/Server.log`，多半是 §2.4 的三类拒绝 |
| `cap --expect` 报 `missing (required)` | 客户端没走到那段代码（模块未加载 / 登录未完成），或 payload 被 drop |
| `cap --expect` 报 `error` 出现 | 载荷 pcall 失败，`MIKU_CAP error` 后面就是 Lua 报错原文 |
| BUILD-INFO 显示 `not run` | 先跑 `pwsh -File tools/test.ps1`；超过 24 小时会标 `STALE` |

---

## 9 未覆盖与下一步

1. **CI 还没有测试步骤**：`.github/workflows/release.yml` 只有两个 publish job。
   建议加一个独立 job（不影响发布）：`setup-dotnet 10.0.x` + `setup-node` + `npm i fengari`，
   然后 `python tools/check_client_patches.py`、`dotnet run --project tests/EraAudit -c Release`、
   `node tests/EraAudit/lua/run.js ...`。L2 需要服务端与账号库，不适合放进 CI。
2. **L2 只覆盖登录即推**：运行期换期（`PushDlcEraSwitch`）、领奖、房间、结算都没有 headless 覆盖。
3. **冒烟不断言字段**：`.temp/smoke` 只看回包帧数；把 `reply_contract_audit.py` 的静态结论下沉成
   运行期断言（必填字段存在、非空、类型正确）是下一步性价比最高的补强。
4. **没有 DB 前后 diff**：登录会重新播种属性；建议冒烟前后各 dump 一次 `Player.Attrs`/`Items` 做结构化 diff。
5. **UI 仍靠人眼**：`gd.ps1` 目前是“手动点击 + 截图”，没有可重放的场景脚本。

---

## 10 实跑记录（2026-11-11）

`pwsh -NoProfile -File tools/test.ps1 -Token <t> -L2Seconds 12 -Caps` 在本机跑通五层：

| 层 | 结果 |
| --- | --- |
| gate `check_client_patches.py` | `43 payload(s) checked, 0 forbidden character(s).` |
| L1 `EraAudit` | `378 check(s) passed, 0 failed.` |
| L1.5 `EraAudit(Lua)` | `56 check(s) passed, 0 failed.` |
| L2 `EraSmoke` | `all checks passed`（服务端 `build/MikuSB-win-x64-dev12`，`dotnet MikuSB.dll` 无窗口启动即可） |
| caps contract | `0 failure(s)` |

跑之前要知道的两件事（都已修进工具/测试，这里只是记录病因）：

1. **L2 的 token 必须来自正在跑的那份库**。`tools/test.ps1` 默认读仓库根的
   `Config/Database/Miku.db`；若服务端跑的是 `build/<name>/`，那边有自己的一份库与账号，
   token 对不上就只看到 `FAIL RspLogin received`。用 `-Token` 传那个库的
   `Account.ComboToken`（Uid 对应的账号）。
2. **L2 按 marker 认载荷**，而 dev-only 的 `DevDiagnostics` 载荷会说
   `MIKU_CAP pool_floor ...`，字符串里带了 `MikuPoolFloor`，于是被误认成
   `GachaPoolWindows`，再因为自己打印的 `MIKU_CAP` 触发“载荷不得含 `MIKU_`”而报错。
   `tests/EraSmoke/Program.cs` 现在先按 `MIKU_CAP ` 字样把诊断载荷剔出去，六份真载荷
   仍然照常校验占位符与字符集。
