# 调试流程（dev 环）

> 目标：客户端上的现象由**我**取证据、由**自动化**判定，人只做“点登录 / 进关卡”这一步。
> 本文件是流程约定，不是一次性笔记；改流程请直接改这里。

## 1. 谁负责什么

| 角色 | 做什么 | 不应该做 |
| --- | --- | --- |
| Agent | 解析路径、读 `Game.log` / `Server.log` / `Miku.db`、写并跑自动化断言、给出结论与补丁 | 要求人贴日志、要求人描述界面细节（能自己取的都自己取） |
| 人 | 启动服务端、点登录、必要时进一次关卡 | 手工判读日志 |

例外只有两种情况需要人来点：**需要客户端渲染的东西**（本流程已经用 `MIKU_CAP` 把它转成日志）和**需要真人手持操作才能触发的时序**。

## 2. 路径从配置来，不硬编码

```bash
python tools/devlog.py paths
```

解析顺序：`Config/Config.json` 的 `Loader.GamePath` → `Config/Config.Local.json` → `Config/Setup.json` / `Config/Loader.json` → 兜底常见安装位置。
输出的三行就是全部证据源：

- 客户端：`<game>/data/game/Saved/Logs/Game.log`（本机当前 `<game root>\...`）
- 服务端：`<server>/Config/Logs/Server.log`
- 数据库：`<server>/Config/Database/Miku.db`

`Loader.GamePath` 为空时说明向导没跑过，此时上面的兜底仍然能命中；**不要**把绝对路径写进代码或文档。

## 3. 证据分层

| 层 | 命令 | 看什么 |
| --- | --- | --- |
| 客户端行为 | `python tools/devlog.py errors` | `SetTexture Error`（缺图素）、`[XGSDK]`（渠道退出）、`LogWindows: Error`（真崩溃） |
| 客户端采集 | `python tools/devlog.py cap` | `MIKU_CAP <tag> <value>`，见第 4 节 |
| 会话时间线 | `python tools/devlog.py watch --since 10m` | 最近 10 分钟的原始行，先定位“用户点的那一下” |
| 两端对齐 | `python tools/devlog.py pair --around 02:15` | 同一时刻的客户端 ±25 行 + 服务端整分钟 |
| 服务端链路 | `grep -n "ReqOnlineRoomStart\|NtfOnlineStart\|No handler" Config/Logs/Server.log` | 发了什么包、哪一步没有实现 |
| 数据侧 | `Miku.db` 的 `Player.Attrs`（`[{"Gid","Sid","Val"}]` 数组） | 是否真写进库、前后是否一致 |

## 4. `MIKU_CAP`：把“要问人”变成“读日志”

dev 服务端（`ServerOption.EnableDebug = true`，默认配置即开）在登录时会自动下发一条**只读**载荷
`DlcClientPatches.DevDiagnostics`，由客户端 `Misc/GM.lua` 的 `s2c.Register('GM', ...)` 用 `load` 执行，
`print` 落到 `Game.log` 的 `LogUnLua:` 行。约定格式：

```
LogUnLua: MIKU_CAP gacha_ids 1,2,501,900
LogUnLua: MIKU_CAP display 900=900,501000=501
LogUnLua: MIKU_CAP curdlc 35
LogUnLua: MIKU_CAP online16_level [[88]]
LogUnLua: MIKU_CAP boss5 5
LogUnLua: MIKU_CAP done 1
```

规则：

1. **只读**——`DevDiagnostics` 只 `print`，绝不写客户端表，所以可以常开；
2. **加新事实就加一行**，tag 用小写蛇形，值保持一行内可解析；
3. 外面套 `pcall`，任何一项失败打 `MIKU_CAP error <msg>` 而不是中断登录；
4. 解析放在 `tools/devlog.py cap`，不要在会话里手抄。

需要临时探针（例如某个 UI 方法的入参）时，同样走这个通道：加一条 `DevDiagnostics*` 载荷，
**不要**为了调试往 `MikuSB-Patch`（注入 DLL）里加逻辑——那个 DLL 只负责连通。

## 5. 自动化测试层（提交前必须全绿）

一条命令跑完全部可自动化的层（服务端没起时自动跳过 L2）：

```powershell
pwsh -File tools/test.ps1          # gate + L1 + L1.5（+ L2）
pwsh -File tools/test.ps1 -Caps    # 再用 tools/caps.json 校验客户端 MIKU_CAP
```

结果写 `build/test-results.json`，`build.ps1` 会把它原文写进 `BUILD-INFO.txt`。
接口细节、载荷规范与故障排查见 [`testing-automation.md`](testing-automation.md)。

| 层 | 命令 | 覆盖 |
| --- | --- | --- |
| gate 字符集 | `python tools/check_client_patches.py` | 载荷可解码性（43 条） |
| L1 静态 | `dotnet run --project tests/EraAudit/EraAudit.csproj -c Release` | 区间算术、载荷内容、字符集（385 断言） |
| L1.5 执行 | `node tests/EraAudit/lua/run.js --out tests/EraAudit/out --fengari <node_modules>` | 真跑 Lua 载荷（56 断言，fengari = Lua 5.3 代跑 5.1） |
| L2 投递 | `dotnet run --project tests/EraSmoke -- <token> 12` | 真登录后客户端**收到**什么（37 断言） |
| L3 人眼 | 手工用例见 `docs/progress/070` | 渲染、动画、手感 |

L2 用 `.temp/smoke` 的 4 字节 LE 帧工具，token 从 `Miku.db` 的 `Account.ComboToken` 取（不进版本库）。

## 6. 标准排障循环

1. `python tools/devlog.py paths` —— 确认客户端/服务端日志都在写。
2. `python tools/devlog.py cap` —— 先看采集值，能直接回答的当场回答。
3. `python tools/devlog.py errors` + `watch --since 10m` —— 定位现象发生的那几秒。
4. `pair --around HH:MM` —— 服务端发了什么、客户端收到什么，两边对齐。
5. 结论分三级写清楚：**A 客户端源码/日志直接读到**、**B 推断**、**C 猜**；B/C 必须给判定手段。
6. 只有 A/B 级且有判定手段的才动代码；否则先加一条 `MIKU_CAP`。

## 7. 载荷约束（写补丁前先读）

- 客户端是 **Lua 5.1**：没有 `goto`、`//`、位运算符；
- 载荷里**禁用** `+ < > & ' "` 六个字符（`System.Text.Json` 会转义成 `\uXXXX`，客户端旧 JSON 解码器会崩），
  所以字符串用 `[[...]]`，累加用 `table.insert(o, x)`，比较用 `~=`；
- 门禁：`python tools/check_client_patches.py`（当前 43 条载荷，0 违禁）；
- 登录时目标模块可能还没 `require`，载荷必须自带重试钩子（见 `GachaPoolWindows` 的 `MikuPoolHook`）。

## 8. 产物与提交

- 内部包：`pwsh -File build.ps1`（服务端在跑就用 `-OutName MikuSB-win-x64-<HHmm>`），
  自动带 `Patch/` 两个 DLL 与 `BUILD-INFO.txt`（commit、时间、`MikuGameServer.dll` sha256）；
- 提交信息只写英文，正文写清“为什么”和实测数字；
- 文档站（本仓 `docs/`）与外部记录（主仓 `docs/progress/`）分开：过程记录进 progress，约定进这里。
