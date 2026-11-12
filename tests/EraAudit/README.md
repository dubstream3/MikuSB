# EraAudit — 活动时代载荷的两层验证

这两个 harness 验证同一份代码：`GameServer/Game/Dlc/DlcClientPatches.cs` 生成的客户端 Lua 载荷，以及
`GameServer/Game/Dlc/DlcEraWindow.cs` 的半开区间算术。它们**不启动服务端**，因此只覆盖 L1/L1.5，不能替代 L2（协议/数据库）与 L3（真机）。

## L1：策略与载荷结构（C#）

```powershell
dotnet run --project tests/EraAudit/EraAudit.csproj -c Release -- --emit tests/EraAudit/out
```

- 直接链接生产源码（`EnableDefaultCompileItems=false` + `<Compile Include="../../GameServer/...">`），不复制实现。
- 断言 378 项：`DlcEraWindow` 边界表、全部字面量载荷的 ASCII 与违禁字符（`+ < > & ' "`）、渲染后无 `MIKU_` 占位符、Off 分支存在、`AuditEraPayloads()` 为空。
- `--emit <dir>` 把 13 个**真正下发给客户端的**渲染结果写到目录里（6 个 era 载荷 + 7 个空集合/Off 变体），供 L1.5 执行。

## L1.5：在 Lua 里执行渲染后的载荷

依赖只有 Node 与 `fengari`（纯 JS 的 Lua 5.3）：

```powershell
# 一次性
$env:npm_config_cache = "$PWD\.temp\npm-cache"
npm install --prefix ..\..\.temp\luarun --no-audit --no-fund fengari

node tests/EraAudit/lua/run.js --out tests/EraAudit/out --fengari ..\..\.temp\luarun\node_modules
```

- 断言 56 项，每个场景用独立 `_ENV`（`loadfile(path, "t", env)`），因此互不污染。
- 覆盖：`GlobalTimeWindows` 半开边界与幂等、`ActivityFaceQuiet` 去重键 `(era, faceId)` 且只在真正展示后置位、
  `GachaTryWindows` 只盖分配 id / 幂等 / `LoadCfg` 重放 / Off 还原、`GachaPoolWindows` 成员与旧窗口还原 /
  `require` 钩子在应用成功后自我卸载、`IbshopWindows`/`FarmWarWindows` 快照与重建表。

## 已知限制（不要写成已验证）

- 客户端是 **Lua 5.1**；本机用 fengari 0.1.5（**Lua 5.3**）代跑。载荷只用到 `math.max/min`、`table.insert`、
  `pcall`、`os.date` 与字符串拼接，未触及 5.1/5.3 有差异的整数除法、`_ENV` 赋值与位运算，故语义等价，
  但"Lua 5.1 下执行"仍属未运行。
- `tests/EraAudit/out/` 是生成物，随时可以删掉重跑。
- L1.5 抓到过两个只靠字符串断言发现不了的真实缺陷（试用 id 序列当集合、幂等守卫在还原之后），
  新增载荷断言时优先补"执行后状态"，不要只查子串。
