#!/usr/bin/env python3
"""Viser kompilator- og testfeil fra en logg som GitHub-annotasjoner (røde merker ved riktig fil og linje).
Bruk: annotate.py <loggfil>   (GitHub viser maks 10 annotasjoner per steg)."""
import os, re, sys

log = sys.argv[1]
if not os.path.exists(log):
    sys.exit(0)
root = os.environ.get("GITHUB_WORKSPACE", "") + "/"
pat = re.compile(r"^(/[^:]+?):(\d+)(?::(\d+))?: (?:fatal )?error: (.*)$")
errors = {}
for line in open(log, encoding="utf-8", errors="replace"):
    if m := pat.match(line.rstrip()):
        path, ln, col, msg = m.groups()
        errors.setdefault((path, ln, msg), (path.removeprefix(root), ln, col or "1", msg))

print(f"::notice::Fant {len(errors)} unike feil i {os.path.basename(log)}")
for p, ln, col, msg in list(errors.values())[:10]:
    msg = msg.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
    print(f"::error file={p},line={ln},col={col}::{msg}")
