"""Builds Client/assets/aion2/pets/monster_names.json: the names of the monsters that drop a pet soul.

Usage: python Tools/pets/build_monster_names.py <path to a clone of SkeeveTV's repository> <output file>

Reads origin/main:Client/assets/aion2/pets/pets.json (the "monsters" ids) and
origin/main:Client/assets/aion2/npcs/npc_names.json from that clone (via git show, the clone is not modified)
and writes the subset {"npcId": {"en": ..., "fr": ...}} for those ids only.
"""
import json
import subprocess
import sys


def show(repo, path):
    out = subprocess.run(["git", "-C", repo, "show", "origin/main:" + path], check=True, capture_output=True)
    return json.loads(out.stdout.decode("utf-8"))


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 1
    repo, output = sys.argv[1], sys.argv[2]
    pets = show(repo, "Client/assets/aion2/pets/pets.json")
    names = show(repo, "Client/assets/aion2/npcs/npc_names.json")
    subset = {}
    missing = 0
    for npc in sorted(pets["monsters"], key=int):
        if npc in names:
            subset[npc] = names[npc]
        else:
            missing += 1
    with open(output, "w", encoding="utf-8", newline="\n") as f:
        json.dump(subset, f, ensure_ascii=False, separators=(",", ":"))
    print(f"{len(subset)} monster names written to {output} ({missing} ids without a name)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
