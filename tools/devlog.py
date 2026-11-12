#!/usr/bin/env python3
"""Dev-loop log reader: resolve the game install, then read what the client actually did.

The client path is a config value (`Loader.GamePath`), so nobody should have to be told
where Game.log lives. When the wizard never ran, this falls back to the launcher's own
record of the last install and, failing that, to the well-known locations.

Usage:
    python tools/devlog.py paths                     # show resolved paths, verify they exist
    python tools/devlog.py cap                       # every MIKU_CAP line in the session
    python tools/devlog.py cap --expect              # assert them against tools/caps.json
    python tools/devlog.py cap --expect caps.json --json out.json --strict
    python tools/devlog.py tail --lines 40           # last N lines of Game.log
    python tools/devlog.py watch --since 10m         # lines added in the last 10 minutes
    python tools/devlog.py errors                    # texture/UI/SDK errors, grouped by kind
    python tools/devlog.py pair --around 02:15       # client + server lines around a clock time

Everything is read-only: no process is touched, no file is written.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import re
import subprocess
import sys
from collections import Counter
from datetime import datetime, timedelta
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONFIG = REPO / "Config" / "Config.json"
LOCAL = REPO / "Config" / "Config.Local.json"

# Client log lines that carry a diagnosis. Keep this list short: it is a triage index,
# not a log dump.
MARKERS = {
    "cap": re.compile(r"MIKU_CAP\b"),
    "texture": re.compile(r"SetTexture Error"),
    "ui": re.compile(r"UI\.OpenInner"),
    "sdk": re.compile(r"\[XGSDK\]"),
    "unlua": re.compile(r"LogUnLua:"),
    "crash": re.compile(r"LogWindows: Error|Access Violation|Fatal error"),
}

STAMP = re.compile(r"\[(\d{4})\.(\d{2})\.(\d{2})-(\d{2})\.(\d{2})\.(\d{2}):(\d{3})\]")
SERVER_STAMP = re.compile(r"\[(\d{2}):(\d{2}):(\d{2})\]")


def load_json(path: Path) -> dict:
    try:
        return json.load(io.open(path, encoding="utf-8-sig"))
    except Exception:
        return {}


def candidate_game_dirs() -> list[Path]:
    out: list[Path] = []
    for cfg in (load_json(CONFIG), load_json(LOCAL)):
        loader = cfg.get("Loader") or {}
        game = (loader.get("GamePath") or "").strip()
        if game:
            out.append(Path(game))
    # The wizard writes Loader.GamePath; a hand-started server often has not. Fall back to
    # the launcher's own settings file before guessing at drive layout.
    for extra in (REPO / "Config" / "Setup.json", REPO / "Config" / "Loader.json"):
        cfg = load_json(extra)
        game = str(cfg.get("GamePath") or cfg.get("gamePath") or "").strip()
        if game:
            out.append(Path(game))
    # A hand-installed copy can live anywhere; let the environment say where instead of
    # guessing at drive letters (the public tree must not carry one machine's layout).
    env_game = os.environ.get("MIKUSB_GAME_DIR", "").strip()
    if env_game:
        out.append(Path(env_game))
    return out


def resolve() -> tuple[Path | None, Path, Path]:
    game = next((p for p in candidate_game_dirs() if (p / "data" / "game").is_dir()), None)
    client_log = (game / "data" / "game" / "Saved" / "Logs" / "Game.log") if game else None
    server_log = REPO / "Config" / "Logs" / "Server.log"
    return game, client_log, server_log


def read_lines(path: Path) -> list[str]:
    if not path or not path.is_file():
        return []
    # utf-8 with a lossy fallback: the client writes a few locale-dependent bytes.
    return io.open(path, encoding="utf-8", errors="replace").read().splitlines()


def cmd_paths(_args) -> int:
    game, client_log, server_log = resolve()
    print(f"game      : {game or 'NOT FOUND (set Loader.GamePath in Config/Config.json)'}")
    print(f"game log  : {client_log or '-'} {'OK' if client_log and client_log.is_file() else 'missing'}")
    print(f"server log: {server_log} {'OK' if server_log.is_file() else 'missing'}")
    if client_log and client_log.is_file():
        st = client_log.stat()
        print(f"log size  : {st.st_size} bytes, mtime {datetime.fromtimestamp(st.st_mtime):%Y-%m-%d %H:%M:%S}")
    return 0


def parse_cap_entries(lines: list[str]) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for line in lines:
        if not MARKERS["cap"].search(line):
            continue
        tag, _, value = line.split("MIKU_CAP", 1)[1].strip().partition(" ")
        out.append((tag.strip(), value.strip()))
    return out


def last_cap_block(entries: list[tuple[str, str]]) -> list[tuple[str, str]]:
    """The diagnostics payload runs on every login, so only the newest block is current.

    A block ends with `done 1` (or with `error <msg>` when the pcall failed, which suppresses
    `done`), so the block to judge starts right after the previous `done`.
    """
    done = [i for i, (tag, _) in enumerate(entries) if tag == "done"]
    if not done:
        return entries
    start = done[-2] + 1 if len(done) > 1 else 0
    return entries[start:]


def check_cap_value(spec: dict, value: str) -> str | None:
    """Return a problem description, or None when the value satisfies the contract entry."""
    kind = spec.get("type", "text")
    if spec.get("nullable") and value in ("nil", ""):
        return None
    if kind == "int":
        try:
            number = int(value)
        except ValueError:
            return f"expected int, got '{value}'"
        low, high = spec.get("min"), spec.get("max")
        if low is not None and number < low:
            return f"{number} is below min {low}"
        if high is not None and number > high:
            return f"{number} is above max {high}"
        return None
    if kind == "id_list":
        if not value:
            return None if spec.get("allow_empty", True) else "empty id list"
        bad = [p for p in value.split(",") if not p.strip().isdigit()]
        return f"non-numeric id(s): {', '.join(bad)}" if bad else None
    if kind == "map":
        if not value:
            return None
        bad = []
        for pair in value.split(","):
            left, sep, right = pair.partition("=")
            if not sep or not left.strip().isdigit() or not right.strip().isdigit():
                bad.append(pair)
        return f"bad pair(s): {', '.join(bad)}" if bad else None
    if kind == "flag":
        want = str(spec.get("value", "1"))
        return None if value == want else f"expected '{want}', got '{value}'"
    if kind == "bool":
        return None if value in ("0", "1", "true", "false") else f"expected bool, got '{value}'"
    if kind == "text":
        return None if value else "empty text"
    return None


def cmd_cap(args) -> int:
    _, client_log, _ = resolve()
    entries = parse_cap_entries(read_lines(client_log))
    block = last_cap_block(entries)

    if not args.expect:
        for tag, value in block:
            print(f"{tag} {value}".strip())
        return 0

    contract_path = Path(args.expect)
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except OSError as exc:
        print(f"cannot read contract {contract_path}: {exc}", file=sys.stderr)
        return 2
    except json.JSONDecodeError as exc:
        print(f"bad contract json {contract_path}: {exc}", file=sys.stderr)
        return 2

    specs: dict = contract.get("tags") or {}
    latest: dict[str, str] = {}
    for tag, value in block:
        latest[tag] = value  # last occurrence wins

    failures = 0
    print(f"caps: {len(block)} line(s) in the last block")
    for tag, spec in specs.items():
        if spec.get("forbidden"):
            if tag in latest:
                print(f"FAIL {tag:16} must not appear, got '{latest[tag]}'")
                failures += 1
            else:
                print(f"PASS {tag:16} absent, as required")
            continue
        if tag not in latest:
            if spec.get("required", True):
                print(f"FAIL {tag:16} missing (required by the contract)")
                failures += 1
            else:
                print(f"skip {tag:16} absent (optional)")
            continue
        problem = check_cap_value(spec, latest[tag])
        if problem:
            print(f"FAIL {tag:16} {problem}")
            failures += 1
        else:
            print(f"PASS {tag:16} {spec.get('type', 'text')} = {latest[tag][:60]}")

    for tag in latest:
        if tag in specs:
            continue
        print(f"WARN {tag:16} not in the contract (add it to tools/caps.json)")
        if args.strict:
            failures += 1

    if args.json:
        Path(args.json).write_text(
            json.dumps({"tags": latest, "failures": failures}, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
    print(f"contract: {failures} failure(s)")
    return 1 if failures else 0


def cmd_tail(args) -> int:
    _, client_log, _ = resolve()
    for line in read_lines(client_log)[-args.lines:]:
        print(line)
    return 0


def cmd_watch(args) -> int:
    _, client_log, _ = resolve()
    m = re.fullmatch(r"(\d+)([mh])", args.since)
    if not m:
        print("--since expects e.g. 10m or 2h", file=sys.stderr)
        return 2
    delta = timedelta(minutes=int(m.group(1))) if m.group(2) == "m" else timedelta(hours=int(m.group(1)))
    since = datetime.now() - delta
    keep = False
    for line in read_lines(client_log):
        hit = STAMP.search(line)
        if hit:
            ts = datetime(*[int(x) for x in hit.groups()[:6]])
            keep = ts >= since
        if keep:
            print(line)
    return 0


def cmd_errors(_args) -> int:
    _, client_log, _ = resolve()
    counts = Counter()
    samples: dict[str, list[str]] = {}
    for line in read_lines(client_log):
        for name in ("texture", "sdk", "crash"):
            if MARKERS[name].search(line):
                counts[name] += 1
                samples.setdefault(name, [])
                if len(samples[name]) < 5:
                    samples[name].append(line.strip()[:200])
    for name, n in counts.most_common():
        print(f"{name:8} {n:5}")
        for s in samples[name]:
            print(f"    {s}")
    if not counts:
        print("no texture/SDK/crash markers in this session")
    return 0


def cmd_pair(args) -> int:
    _, client_log, server_log = resolve()
    hhmm = args.around
    print(f"--- client (Game.log) ±{args.window} lines around {hhmm}")
    lines = read_lines(client_log)
    idx = [i for i, l in enumerate(lines) if f"-{hhmm.replace(':', '.')}" in l[:26]]
    for i in idx[:1]:
        for l in lines[max(0, i - args.window):i + args.window]:
            print(l)
    print(f"--- server (Server.log) [{hhmm}:xx]")
    for l in read_lines(server_log):
        hit = SERVER_STAMP.search(l)
        if hit and f"{hit.group(1)}:{hit.group(2)}" == hhmm:
            print(l)
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("paths").set_defaults(func=cmd_paths)
    p = sub.add_parser("cap", help="MIKU_CAP lines; --expect checks them against a contract")
    p.add_argument("--expect", nargs="?", const=str(REPO / "tools" / "caps.json"), default=None,
                   help="contract json (default tools/caps.json); exits 1 on violation")
    p.add_argument("--json", help="write the parsed tags and the failure count here")
    p.add_argument("--strict", action="store_true", help="treat unknown tags as failures")
    p.set_defaults(func=cmd_cap)
    p = sub.add_parser("tail"); p.add_argument("--lines", type=int, default=40); p.set_defaults(func=cmd_tail)
    p = sub.add_parser("watch"); p.add_argument("--since", default="10m"); p.set_defaults(func=cmd_watch)
    sub.add_parser("errors").set_defaults(func=cmd_errors)
    p = sub.add_parser("pair"); p.add_argument("--around", required=True); p.add_argument("--window", type=int, default=25); p.set_defaults(func=cmd_pair)
    args = ap.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
