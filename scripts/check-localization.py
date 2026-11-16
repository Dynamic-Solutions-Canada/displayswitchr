#!/usr/bin/env python3
"""Check the UI text catalogs in HubDisplaySwitch/Localization/Catalogs.

- Every key the code uses (UiText.Get/Format literals, l10n:Localize.* in XAML, SwitchReason values)
  exists in en-US.json.
- Every translated catalog has exactly the en-US keys, no empty values, and the same {{placeholders}}.
- en-US values equal their keys (the English text is the key).

Run from the repo root: python3 scripts/check-localization.py   (add --list to print the code's keys)
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
APP = ROOT / "HubDisplaySwitch"
CATALOGS = APP / "Localization" / "Catalogs"
LOCALES = ["en-US", "es-ES", "fr-FR", "de-DE", "zh-CN", "pt-BR", "ja-JP"]   # en-CA/fr-CA reuse en-US/fr-FR

STRING = r'"((?:[^"\\]|\\.)*)"'
XAML = re.compile(r'l10n:Localize\.\w+="([^"]*)"')
SET_CONTENT = re.compile(r'Localize\.Set\w+\(\w+,\s*' + STRING)
REASON = re.compile(r'(\w+)\s*=\s*' + STRING)
PLACEHOLDER = re.compile(r'\{\{\s*(\w+)\s*}}')


def call_arguments(text, start):
    """Text of a call's arguments, from just after its "(" to the matching ")", skipping string contents."""
    depth, i, in_string = 1, start, False
    while i < len(text) and depth:
        c = text[i]
        if in_string:
            if c == "\\":
                i += 1
            elif c == '"':
                in_string = False
        elif c == '"':
            in_string = True
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        i += 1
    return text[start:i - 1]


def code_keys():
    keys = set()
    for path in APP.rglob("*.cs"):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8")
        for m in re.finditer(r'UiText\.(?:Get|Format)\(', text):
            rest = call_arguments(text, m.end())
            # Literals in the call, minus Format's ("name", value) placeholder names.
            names = set(re.findall(r'\(\s*"(\w+)"\s*,', rest))
            for lit in re.findall(STRING, rest):
                if lit and lit not in names:
                    keys.add(json.loads('"' + lit + '"'))
        for lit in SET_CONTENT.findall(text):
            keys.add(json.loads('"' + lit + '"'))
        if "class SwitchReason" in text:
            block = text.split("class SwitchReason", 1)[1].split("}", 1)[0]
            for _, lit in REASON.findall(block):
                keys.add(json.loads('"' + lit + '"'))
    for path in APP.rglob("*.xaml"):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        for value in XAML.findall(path.read_text(encoding="utf-8")):
            keys.add(value.replace("&quot;", '"').replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">"))
    return keys


def main():
    used = code_keys()
    if "--list" in sys.argv:
        print(json.dumps(sorted(used), ensure_ascii=False, indent=1))
        return 0
    problems = []
    catalogs = {}
    for locale in LOCALES:
        path = CATALOGS / f"{locale}.json"
        if not path.exists():
            problems.append(f"{locale}: missing {path.name}")
            continue
        catalogs[locale] = json.loads(path.read_text(encoding="utf-8"))
    en = catalogs.get("en-US", {})
    for key in sorted(used - en.keys()):
        problems.append(f"en-US: missing key used in code: {key!r}")
    for key in sorted(en.keys() - used - {""}):
        problems.append(f"en-US: unused key: {key!r}")
    for key, value in en.items():
        if key != value:
            problems.append(f"en-US: value differs from key: {key!r}")
    for locale, catalog in catalogs.items():
        if locale == "en-US":
            continue
        for key in sorted(en.keys() - catalog.keys()):
            problems.append(f"{locale}: missing {key!r}")
        for key in sorted(catalog.keys() - en.keys()):
            problems.append(f"{locale}: extra {key!r}")
        for key, value in catalog.items():
            if key in en and not value.strip():
                problems.append(f"{locale}: empty {key!r}")
            if key in en and sorted(PLACEHOLDER.findall(value)) != sorted(PLACEHOLDER.findall(key)):
                problems.append(f"{locale}: placeholders differ in {key!r}")
    for p in problems:
        print(p)
    print(f"{len(used)} keys in code, {len(LOCALES)} catalogs, {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
