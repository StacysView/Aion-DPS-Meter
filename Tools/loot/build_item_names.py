#!/usr/bin/env python3
"""Builds assets/aion2/items/item_names_i18n.json.

usage: build_item_names.py <item_info.json> <skeeve item_names_i18n.json> <out>

item_info.json maps item id -> [english name, ...]; Skeeve's file maps english name ->
{"fr": ..., "de": ..., ...} (MIT, https://github.com/SkeeveAN). The output keeps only the ids whose
english name Skeeve's file knows, with fr/de/es/ru: { "<id>": {"fr": "...", "de": "...", "es": "...", "ru": "..."} }.
"""
import json
import sys

LANGS = ("fr", "de", "es", "ru")


def main() -> int:
    if len(sys.argv) != 4:
        print(__doc__)
        return 2
    with open(sys.argv[1], encoding="utf-8") as f:
        info = json.load(f)
    with open(sys.argv[2], encoding="utf-8") as f:
        names = json.load(f)
    out = {}
    for item_id, row in info.items():
        tr = names.get(row[0])
        if not tr:
            continue
        entry = {lang: tr[lang] for lang in LANGS if tr.get(lang)}
        if entry:
            out[item_id] = entry
    with open(sys.argv[3], "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
    print(f"{len(out)} of {len(info)} ids translated")
    return 0


if __name__ == "__main__":
    sys.exit(main())
