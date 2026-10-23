#!/usr/bin/env python3
"""Audit the Lua payloads of DlcClientPatches.cs for characters the client cannot decode.

HandlerReqLogin.SendClientPatch drops any payload containing one of
ForbiddenPatchChars ('+', '<', '>', '&', "'", '"'): System.Text.Json escapes them to
\\uXXXX and the client's Misc/Json.lua decoder (Lua 5.1) rejects those escapes, so the
whole patch would silently become a no-op. The server logs an error when it drops one,
but by then the feature is already broken in-game - this catches it at build time.

Write payloads without those characters:
  "text"        -> [[text]]          (Lua long string, no quotes)
  #s >= 12      -> #s == 12          (no '>')
  n > 0         -> n ~= 0            (no '>')

Usage: python tools/check_client_patches.py [path-to-DlcClientPatches.cs]
Exit code 0 = clean, 1 = at least one payload would be dropped.
"""
import os
import re
import sys

DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                       "GameServer", "Game", "Dlc", "DlcClientPatches.cs")
FORBIDDEN = {"+", "<", ">", "&", "'", '"'}

path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT
path = os.path.normpath(path)

lines = open(path, encoding="utf-8").read().splitlines()

start_re = re.compile(r'public const string (\w+) = """')
blocks = []
i = 0
while i < len(lines):
    m = start_re.search(lines[i])
    if not m:
        i += 1
        continue
    name = m.group(1)
    j = i + 1
    while j < len(lines) and not re.match(r'^\s*""";\s*$', lines[j]):
        j += 1
    blocks.append((name, i + 1, j))
    i = j + 1

if not blocks:
    print(f"no patch payload found in {path}")
    sys.exit(1)

bad_total = 0
for name, begin, end in blocks:
    hits = []
    for idx in range(begin, end):
        for ch in lines[idx]:
            if ch in FORBIDDEN:
                hits.append((idx + 1, ch, lines[idx].strip()))
    if hits:
        bad_total += len(hits)
        print(f"[BAD] {name}  ({len(hits)} forbidden char(s))")
        for lineno, ch, text in hits[:8]:
            print(f"   {lineno}: '{ch}' -> {text}")
        if len(hits) > 8:
            print(f"   ... {len(hits) - 8} more")

print(f"{len(blocks)} payload(s) checked, {bad_total} forbidden character(s).")
sys.exit(1 if bad_total else 0)
