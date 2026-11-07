#!/usr/bin/env python3
"""Collects the player-facing English strings of the game (docs/design/localization.md).

Reads every C# literal that looks like UI text (and every $"..." template, as a {0} {1}
format) plus DisplayName/Description in skill .tres files, and writes
tools/i18n/strings.txt (one per line, sorted). With --missing <locale> it prints the ones
assets/i18n/<locale>.tsv has no translation for, grouped by file.
"""
import os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP_DIRS = {"tests", "tools"}
LIT = re.compile(r'(?<![\w$@])"((?:[^"\\\n]|\\.)*)"')
INTERP = re.compile(r'\$"((?:[^"\\\n]|\\.)*)"')
TRES = re.compile(r'^(DisplayName|Description) = "((?:[^"\\]|\\.)*)"', re.M)

def looks_like_text(s):
    if len(s) < 2 or not re.search(r"[A-Za-z]", s):
        return False
    if s.startswith(("res://", "user://", "uid://", "icon_", "#")) or "/" in s and " " not in s:
        return False
    if re.fullmatch(r"[a-z0-9_.:\-]+", s):      # ids, keys, node paths
        return False
    if re.fullmatch(r"[A-Z][A-Za-z0-9]+", s) and not re.fullmatch(r"[A-Z][a-z]+", s):  # PascalCase node names
        return False
    return True

def template(s):
    """$"Level {x}" -> "Level {0}" (format specs kept: {x:0.0} -> {0:0.0})."""
    n = [0]
    def rep(m):
        spec = m.group(2) or ""
        i = n[0]; n[0] += 1
        return "{%d%s}" % (i, spec)
    return re.sub(r"\{([^{}:]+)(:[^{}]*)?\}", rep, s)

def collect():
    found = collections.defaultdict(set)
    for base, dirs, files in os.walk(os.path.join(ROOT, "scripts")):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(base, f)
            rel = os.path.relpath(path, ROOT)
            text = open(path, encoding="utf-8").read()
            text = re.sub(r"^\s*///.*$|//[^\n\"]*$", "", text, flags=re.M)
            for m in INTERP.finditer(text):
                t = template(m.group(1))
                if looks_like_text(re.sub(r"\{\d+[^}]*\}", "", t)):
                    found[t].add(rel)
            for m in LIT.finditer(re.sub(INTERP, "", text)):
                s = m.group(1)
                if "GD.Print" in text[max(0, m.start() - 40):m.start()] or "PushWarning" in text[max(0, m.start() - 40):m.start()]:
                    continue
                if looks_like_text(s):
                    found[s].add(rel)
    for base, dirs, files in os.walk(os.path.join(ROOT, "resources")):
        for f in files:
            if f.endswith(".tres"):
                path = os.path.join(base, f)
                for m in TRES.finditer(open(path, encoding="utf-8").read()):
                    found[m.group(2)].add(os.path.relpath(path, ROOT))
    return found

def load_tsv(locale):
    path = os.path.join(ROOT, "assets", "i18n", f"{locale}.tsv")
    table = {}
    if os.path.exists(path):
        for line in open(path, encoding="utf-8"):
            if "\t" in line and not line.startswith("#"):
                k, v = line.rstrip("\n").split("\t", 1)
                table[k] = v
    return table

if __name__ == "__main__":
    found = collect()
    with open(os.path.join(ROOT, "tools", "i18n", "strings.txt"), "w", encoding="utf-8") as out:
        for s in sorted(found):
            out.write(s + "\n")
    if "--missing" in sys.argv:
        table = load_tsv(sys.argv[sys.argv.index("--missing") + 1])
        by_file = collections.defaultdict(list)
        for s, files in found.items():
            if s not in table:
                by_file[sorted(files)[0]].append(s)
        total = sum(len(v) for v in by_file.values())
        for f in sorted(by_file, key=lambda f: -len(by_file[f])):
            print(f"{len(by_file[f]):5d}  {f}")
        print(f"{total} missing of {len(found)}")
    else:
        print(f"{len(found)} strings")
