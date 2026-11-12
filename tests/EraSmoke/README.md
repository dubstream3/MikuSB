# EraSmoke — L2：对活服务端验证时代载荷真的发出去了

`tests/EraAudit` 只证明**代码会渲染出什么**；本工具证明**线上真的收到什么**。它连上 `127.0.0.1:21000`，
用真实 `ComboToken` 登录，然后只观察服务端主动推送的帧：

- `NtfSyncAttr`(1004)：登录重播种的固定属性；
- `NtfScript`(1010) 且 `Api == "GM"`：客户端补丁，JSON 体里的 `code` 就是 Lua 载荷。

断言针对**已经上线的字节**，不针对源码：载荷必须无 `MIKU_` 占位符、纯 ASCII、不含 `+ < > & ' "`、
位于 `{"code": ...}` 信封里；`GachaTryWindows` 必须带 `idset`（`local idset = {}` + `idset[row.nId]`）、
`idcount` 与列表长度一致，且幂等守卫出现在还原之前。

## 前置

```powershell
# 1. 构建（GameServer 的载荷代码变更后必须重建，否则发的是旧载荷）
dotnet build GameServer/GameServer.csproj -c Release
dotnet build MikuSB/MikuSB.csproj -c Debug

# 2. 起服务端。MikuSB.exe 的清单要求管理员权限（普通 shell 是 os error 740）；
#    用 dotnet 宿主直接跑 dll 可以无窗口启动。系统 dotnet 只有 8.0，必须用便携 SDK 的 dotnet.exe。
dotnet MikuSB/bin/Debug/net10.0/MikuSB.dll
#    （后台起： ... < /dev/null > .temp/headless-boot.log 2>&1 &）

# 3. 登录 token 取自本地库，不要写进仓库
$token = python -c "import sqlite3;print(sqlite3.connect('Config/Database/Miku.db').execute('select ComboToken from Account where Uid=1').fetchone()[0])"

dotnet build tests/EraSmoke/EraSmoke.csproj -c Debug
dotnet tests/EraSmoke/bin/Debug/net10.0/era-smoke.dll "$token" 14
```

通过时最后一行是 `EraSmoke: all checks passed.`，退出码 0；任一断言失败退出码 1。

## 已知限制

- 只覆盖**登录即推送**的部分。`/dlc` 运行期换期（`PushDlcEraSwitch`）需要往服务端控制台写指令，
  本工具不驱动控制台，因此换期后的重推仍未验证。
- 不校验数据库写入（属 L2 的 DB 断言，需要配合 `Config/Database/Miku.db` 前后对比）。
- 不覆盖发奖路径（`GetAward`/`tbGet`），那需要驱动具体的领奖 api。
- 未跑 L3：没有客户端，画面与 `require` 时机无法确认。
