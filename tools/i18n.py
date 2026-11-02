#!/usr/bin/env python3
"""Sjekker at alle tekster i brukerflaten har engelsk oversettelse i i18n/en.json.

Tekstene i koden er norske og er samtidig nøklene:
  Windows (C#):   Loc.T("Lagre"), Loc.F("Fant {0} filer", n)
  Windows (XAML): Text="{local:T 'Lagre'}"
  Mac (Swift):    L("Lagre"), L("Fant {0} filer", n)

  python3 tools/i18n.py            sjekk (feiler hvis noe mangler eller {0}-feltene ikke stemmer)
  python3 tools/i18n.py --update   legg til nye nøkler (tom oversettelse) og fjern ubrukte
"""
import html
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TABLE = ROOT / "i18n" / "en.json"

CS = re.compile(r'Loc\.[TF]\(\s*(@?)"((?:[^"\\]|\\.|"")*?)"(?!")')
XAML = re.compile(r"\{local:T\s+'((?:[^'\\]|\\.)*)'\}")
SWIFT = re.compile(r'(?<![\w.])L\(\s*"((?:[^"\\]|\\.)*)"')
ESC = {"n": "\n", "t": "\t", "r": "\r", '"': '"', "\\": "\\", "0": "\0", "'": "'"}


def unescape(s: str) -> str:
    def one(m):
        e = m.group(1)
        if e.startswith("u{"):
            return chr(int(e[2:-1], 16))
        if e.startswith("u") and len(e) == 5:
            return chr(int(e[1:], 16))
        return ESC.get(e, e)
    return re.sub(r"\\(u\{[0-9a-fA-F]+\}|u[0-9a-fA-F]{4}|.)", one, s)


def keys_in_code() -> dict:
    found = {}
    for f in (ROOT / "Windows" / "ShowCue").rglob("*.cs"):
        for m in CS.finditer(f.read_text(encoding="utf-8")):
            key = m.group(2).replace('""', '"') if m.group(1) else unescape(m.group(2))
            found.setdefault(key, f.relative_to(ROOT).as_posix())
    for f in (ROOT / "Windows" / "ShowCue").rglob("*.xaml"):
        for m in XAML.finditer(f.read_text(encoding="utf-8")):
            key = html.unescape(re.sub(r"\\(.)", r"\1", m.group(1)))
            found.setdefault(key, f.relative_to(ROOT).as_posix())
    for f in (ROOT / "Mac" / "Sources").rglob("*.swift"):
        for m in SWIFT.finditer(f.read_text(encoding="utf-8")):
            found.setdefault(unescape(m.group(1)), f.relative_to(ROOT).as_posix())
    return found


def placeholders(s: str) -> list:
    return sorted(set(re.findall(r"\{(\d+)(?:[:,][^}]*)?\}", s)))


def main() -> int:
    used = keys_in_code()
    table = json.loads(TABLE.read_text(encoding="utf-8")) if TABLE.exists() else {}

    if "--update" in sys.argv:
        new = {k: table.get(k, "") for k in sorted(used, key=str.casefold)}
        TABLE.parent.mkdir(exist_ok=True)
        TABLE.write_text(json.dumps(new, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        missing = sum(1 for v in new.values() if not v)
        print(f"{len(new)} tekster, {missing} uten oversettelse, {len(set(table) - set(used))} ubrukte fjernet.")
        return 0

    errors = []
    for key, where in sorted(used.items()):
        if not table.get(key):
            errors.append(f"{where}: mangler engelsk tekst for «{key}»")
        elif placeholders(key) != placeholders(table[key]):
            errors.append(f"{where}: {{0}}-feltene stemmer ikke for «{key}» → «{table[key]}»")
    for e in errors:
        print(e)
    unused = sorted(set(table) - set(used))
    if unused:
        print(f"Merk: {len(unused)} ubrukte oversettelser (kjør med --update for å fjerne dem).")
    print(f"{len(used)} tekster sjekket, {len(errors)} feil.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
