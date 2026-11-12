# Era gacha rosters

Why the pinned era needs its own banner list, where that list comes from, and how the payload
rebuilds it inside the client.

## 1 The table the client loads has no banner for a pinned era

`gacha.txt` and `gacha_group.txt` are shipped by the settings pak and then **rewritten by every
patch pak**. The highest-numbered patch pak wins, so the data the client actually loads is the
one from `Patch_NonUFS-WindowsNoEditor_11_P.pak`, not the `Settings_0` base pak beside it.

The Windows NoEditor patch paks re-time the whole backlog into a short list of 2026 windows
(`202604020400`, `202605080400`, `202606190400`, `202607160400`, ...). For a pinned era that
means the era's own rows are **inside their window in no build at all**: the era-35 slice of
`gacha.txt` (`202601290400`–`202603120400`) and its 5 `gacha_group.txt` groups are simply absent
from the table the client loads. Re-dating the rows that *are* in the table cannot reveal one,
which is what the `MikuPickBan` fallback and the `MikuDispApply` borrow-a-name rows were
compensating for - and that compensation is exactly what put `card.girl202_suits` (卜卜-灵鉴) in
the front page slot with every other pool stacked underneath it.

The dates are still shipped, in `PAK_Game_Settings_0-WindowsNoEditor.pak`:

| file | rows | content |
| --- | --- | --- |
| `Game/Content/Settings/gacha/gacha.txt` | 192 | the real historical windows |
| `Game/Content/Settings/gacha/gacha_group.txt` | 39 | the groups those rows belong to |

Both tables agree on the UP payload (`ShowResource`, the `pool0NN` ids, `UpNum`,
`ProtectType`); only `PoolTime` was squashed. So the fix is **not** a new banner definition -
it is the window and the group row the patch build dropped.

## 2 The roster

`DlcClientPatches.GachaRosters` carries one row per `gacha_group.txt` line of a pinned era, in
file order:

```csharp
new GachaRosterRow(PoolId, GroupName, GroupIcon, GroupMark, GachaId)
```

Era 35 (24 rows, 5 groups) as shipped by the base pak:

| poolid | group | icon | mark | gachaid (char100, weapon100, char50, weapon50) |
| --- | --- | --- | --- | --- |
| 13-16 | `card.girl013_suits` 安卡希雅-时之重奏 | 130510006 | 1 | 191, 192, 91, 92 |
| 17-20 | `card.girl054_suits` 茉莉安-霄鹰 | 130520004 | 1 | 193, 194, 93, 94 |
| 21-24 | `card.girl084_suits` 芬妮-澄意 | 130470004 | 0 | 179, 180, 79, 80 |
| 25-28 | `card.girl133_suits` 瑟瑞斯-悠游 | 130480004 | 0 | 181, 182, 81, 82 |
| 29-36 | `gacha.diyup3` 心之回响 | 0 | 2 | 195, 196, 95, 96, 197, 198, 97, 98 |

The order inside a group is fixed by the shipped file: **100% character, 100% weapon, 50%
character, 50% weapon**. `GroupIcon`/`GroupMark` are rendered as `nil` when zero, because
`uw_gacha_list.lua` tests them rather than comparing against 0.

Adding an era means adding a dictionary entry; nothing else in the payload has to change.

## 3 What the payload does with it

`MIKU_GACHA_ROSTER` is substituted by `GachaPoolWindowsFor`, which renders the roster as a Lua
table literal. In the client, `gpools()`:

1. builds `MikuRosterWant`/`MikuRosterRows` from the literal (**rows without a gachaid are
   skipped**: those are group rows of a pool that is not in the table);
2. walks `Gacha.tbGacha` once. A roster id is opened unconditionally and stamped with
   `{ now - 1h, now + 30d }`; every other id is decided by its own shipped window, exactly as
   before. Ids outside the era get their shipped window back;
3. `MikuDispApply()` (called right after, and again from the `LoadCfg` wrapper on every
   reload) removes the previous rows **by reference** and prepends the roster rows to
   `Gacha.tbDisplayGroup`, which is an array walked with `ipairs`.

`nId` is `poolId - 1000`, i.e. -987..-964 for era 35: negative so it can never collide with a
pool id, and subtraction because `+` is a forbidden character in the payloads.

Three things were deleted from the old implementation and must not come back:

- the `MikuPickBan` fallback (picking "the banner that was live when the era opened" is what
  handed the front page to a 3.4 pool);
- the `Gacha.Logic.IsOpen` wrapper (`IsInTime` is what hides a banner, and a stamped window
  satisfies it);
- `MikuDispApply`'s borrow-a-name rows (one row per in-window pool, named after the first group
  in the file). That is what produced the single 卜卜-灵鉴 group with every pool stacked under
  it.

## 4 Verifying

Layer 1 (offline, no server): `pwsh -NoProfile -File tools/test.ps1 -SkipL2`. The gate checks
the payload for forbidden characters; `tests/EraAudit` renders
`GachaPoolWindowsFor(floor, stop, 35)` and asserts the roster row count, the display rows, the
per-group marks/icons and idempotency; `tests/EraAudit/lua` runs the emitted payload against a
fake `Gacha.tbGacha` whose 210 pools are **all** dated outside the era (the regression guard:
the old borrow-a-name code opened every one of them).

Layer 2 (server + client): pin the era (`GameServer.PinDlcActivityId: 35` in `Config.json`, or
`Config.Local.json`), run the server, log in, then read the client log:

```
grep -E "POOLAPPLY|MIKU_CAP display|MIKU_CAP pool_in_time" \
  "D:/Games/Snow/data/game/Saved/Logs/Game.log"
```

Expected: `POOLAPPLY ... roster 24 ...` with 24 ids in `ids`; `MIKU_CAP pool_in_time` carries
exactly those 24 ids plus the permanent (1, 2, 3) rows; `MIKU_CAP display` no longer contains
the 173 borrow-a-name entries (31 shipped rows + 24 roster rows = 55).
