# Handoff：抽卡树（gacha tree）修复

> 写于 2026-11-11 20:06。下一个会话从这里接手，不要重新考古。
> 服务端开发树：`.temp/MikuSB`
> 客户端安装：`<game root>`；客户端 Lua 明文：`.temp/lua_probe/script_all/Game/Content/Script`
> 客户端配置表：`客户端原始PAK数据/Android_3.6.0.199_20260715/Game/Content/Settings/gacha/`

## 1 现在在哪一步

- **dev13 已构建**：`build/MikuSB-win-x64-dev13/`（zip 同目录，sha256 `c7447b8e…b2f37e`，commit `287b83c`）。
  `Config/` 从 dev12 复制的：`PinDlcActivityId: 35`、两份 `Config.Local.json` 的地址都是 `127.0.0.1`。
- 用户实测反馈：**抽卡页能开了**，但下拉框结构不对（截图：`トト-灵鉴` 占据“版本池”位置，下面 `奈莉德-冥河代理人`/`夕潮` 各出现 50% 与 100% 两行）。
  用户口径：正常应是 **8 个池**（版本池 + 四条 × 两档保底），**不歪（100%）要分开**。
- 用户要求：**按 group 重建卡池**。当前代码仍是「按池补卡 + 就近兜底 + 强行 IsOpen」，即错误来源。

## 2 抽卡表结构（已核实，勿再猜）

| 表 | 规模 | 作用 |
| --- | --- | --- |
| `gacha.txt` | 204 数据行，42 列 | 池定义。第 1 列 = 池 id，第 3 列 = 窗口 `{开始,结束}`（12 位 `yyyymmddHHMM`），第 4 列 = `{}` 包裹的类型参数 |
| `gacha_group.txt` | 27 张卡（+1 表头），**5 列，最后一列 = `gachaid`** | 下拉框的卡。卡名形如 `card.girl241_suits` = 主角色战损立绘 |
| `gacha/pool/*.txt` | 按池名 | 每池的权重 / `UPTag` / `Weight` 子表 |
| `probability.txt` | 2 行 | 只有全局星级表，**不含** 50%/100% 信息 |

客户端消费链路：
`gacha_group.txt` → `Gacha.tbDisplayGroup`（**数组**，`{nId,sGroupName,nGroupIcon,nGropMark,nGachaId}`）→ `UMG/Gacha/umg_gacha.lua:296` 用 **`ipairs`** 遍历 → `Gacha.GetCfg(nGachaId)` → `cfg:IsInTime()` → `Gacha.Logic.IsOpen()`（还查新鲜度/宝箱计数）。

**关键结论**：卡（group 行）才是 UI 单位；池（gacha.txt 行）只是被卡引用的内容。一期通常有 8 张卡 = 4 个 UP 对象 × 2 档保底，`poolid` 排 1..8。

## 3 为什么 DLC35 没有卡

已算清（`gacha.txt` 全部不同窗口）：

```
... 20251106 -> 20251218 (pool 153/163)
    20251218 -> 20260129 (pool 55/56/89/90/155/156/189/190)   ← 结束时刻正好 = DLC35 起点
    ── 空档 2026-01-29 → 2026-04-02 ──
    20260402 -> 20260409 (pool 179..198 等，只一周)
    20260508 -> 20260619 / 20260529 -> 20260619 / 20260619 -> 20260716
```

- 没有任何池窗口落在 DLC35（`202601290400 → 202603120400`）之内；两份 Android 3.6 快照（`20260619`/`20260715`）与 `Resources/gacha/gacha.json`（192 行）一致。
- `gacha_group.txt` 的 27 张卡只覆盖 `1/2/3`（常驻，2021→2030）与 `2026-05-08`、`2026-05-29`、`2026-06-19` 三个周期 —— **发版时旧卡被裁掉**，这是 Live 服务的常规裁剪，不是数据缺失。
- 本地**没有 PC 3.6 包**（只有 `PC_1.8.91`、`PC_2.0.90.79`，均 2024 年），无法交叉验证。
- DLC35 的主推角色可从试用表拿到：`Resources/activity/gacha_try/gacha_try.json` 的 `ActId 580 → TryGirl [134]`（`580` 是 35 期的 `gacha_try` 活动，`EndTime 202603110400`）。

## 4 当前 `GachaPoolWindows` 载荷做了什么（要改的地方）

文件：`GameServer/Game/Dlc/DlcClientPatches.cs`（约 1740–1960 行，token `MIKU_ACT_FLOOR` / `MIKU_ACT_STOP`）。

1. `MikuPoolNum(v)`：把单元格里的数字串 `gsub(v,[[%D]],...)` 后 `tonumber`，取代 `ParseTime`（后者对实际单元格返回 nil）。
2. `MikuPoolRaw[id]`：按需补快照，冷登录时 `Gacha.LoadCfg` 尚未跑也能补全（提交 `7b40246`）。
3. `MikuPickBan`：期内没有池“开始”时，取“结束时间最接近 floor”的池（提交 `a043e58`）——**这是把 3.4 池当 DLC35 开的根源**。
4. 包装 `Gacha.Logic.IsOpen`：`MikuPoolFloor ~= 0 and self:IsInTime()` 就返回 true（提交 `568aaee`）。
5. `MikuDispApply()`：给每个“已打开且没有卡”的池 `table.insert` 一张卡，**卡名/图标借用表里第一张卡**（提交 `36add93` 修好了数组键问题，但借名逻辑没修）——**这是截图里出现角色名占位的原因**。

## 5 已完成：按 era 名册重建卡池（2026-11-11）

上面第 1 条的“按 group 重建”没有按「克隆结构最接近的真池」做——取证后发现有更硬的依据：
**客户端 base pak（`PAK_Game_Settings_0`）里就带着每个历史版本的原始卡池行与 group 行**，
只是运行中的 `Patch_NonUFS-WindowsNoEditor_11_P.pak` 把整表窗口压到了 2026-04 之后。
所以 era 35 的阵容直接照抄客户端自带的名册（24 行 = 5 组），不发明新池。

落地方式与验证命令见 [`gacha-era-roster.md`](gacha-era-roster.md)，要点：

1. 删掉三块冒充逻辑：`MikuPickBan` 兜底、`IsOpen` 强制包装、`MikuDispApply` 的按池借名卡。
2. 新增 C# 侧 `GachaRosters`（era → `GachaRosterRow(PoolId, GroupName, GroupIcon, GroupMark, GachaId)`），
   经 `MIKU_GACHA_ROSTER` token 注入载荷；名册内的 gachaId 盖 era 窗口，名册外的还原出厂窗口。
3. `Gacha.tbDisplayGroup` 仍按数组处理：`table.insert(..., 1, row)` 保持文件顺序，删除按引用 `table.remove`；
   `nId` 取 `poolid - 1000`（负数）避免与出厂行撞号。
4. `tests/EraAudit/lua/run.lua` 加了 `GachaPoolWindows roster` 失败用例（210 个期外假池 → 只开名册 24 个），
   `AuditEraPayloads` 加了 `#35` 变体，三层验证（gate / L1+L1.5 / L2 EraSmoke）全绿。

**推广到其它 era**：给 `GachaRosters` 加一条（poolId / poolname / icon / mark / gachaid），
数据同样从 base pak 的 `gacha.txt` + `gacha_group.txt` 取对应窗口那几行即可，载荷不用改。

## 6 抽卡记录（另一个待办）

用户点“抽卡记录”后客户端卡在“连接中”。

- 已核实：**不是 CallGS**。Lua 里只有 `Misc/Adjust.lua` 的 `Adjust.GachaRecord(nId,nTime)` / `Adjust.GachaMainRecord()`，属 Adjust 分析上报；`umg_gacha.lua:606`、`umg_gacha_tap.lua:739` 都是上报。
- 记录页是 native/webview 打开的外部地址，`MikuSB-Patch` 的 `TARGET_DOMAINS` 没覆盖它，于是挂住。
- 开工前先拿证据：点一次记录页，然后 `python tools/devlog.py errors --since 3m`（或看 `Game.log` 里 `LogHttp`/`LogWebView`/URL 行），确定真实 host，再决定「把 host 指到本地并供货」还是「改按钮走自建接口」。

## 7 环境与工具（省时间用）

- 跑测试：`pwsh -NoProfile -File tools/test.ps1 -Token <t> -L2Seconds 15 -Caps`；token 必须来自**正在跑的那份库**的 `Account.ComboToken`（Uid=1）。
- 服务端可无窗口起：`cd build/MikuSB-win-x64-devN && dotnet MikuSB.dll`（系统 dotnet 8 不行，要便携 SDK 10）。
- 客户端日志：`<game root>\data\game\Saved\Logs\Game.log`，Lua `print` 落在 `LogUnLua:`。
- 载荷约束：Lua 5.1；**禁用 `+ < > & ' "`**（`tools/check_client_patches.py`，43 条）；载荷里不能出现裸 `MIKU_` 开头的新标记（会被判成未替换占位符，`AuditEraPayloads` 会红）。
- 构建：`pwsh -NoProfile -File build.ps1 -OutName MikuSB-win-x64-devN`（`-Public` 会断言 dev 诊断被编掉）。
- 已提交的相关 commit：`568aaee`、`33f6643`、`7617228`、`7b40246`、`69ef0d5`、`a043e58`、`36add93`、`6c66e65`（L2 修正）、`287b83c`（测试文档）。

## 8 不要做的事

- 不要用 `ParseTime` 解析池窗口（它返回 nil）。
- 不要按池 id 写 `tbDisplayGroup[id]`（数组，UI 用 `ipairs` 看不见）。
- 不要把 3.4 / 5 月那批池“就地改窗口”冒充当期（用户已经明确否掉）。
- 不要在没有证据前改 `Adjust.lua` 或猜记录页域名。
