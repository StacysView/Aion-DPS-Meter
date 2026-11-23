"""Writes Client/assets/aion2/bosses/dungeon_finals.json: for each dungeon of the boss table, the name(s) of its last boss (the one the battle music of the Monsters theme plays for).

Usage: python make_dungeon_finals.py <Client folder>
Rule: the bosses of an instance in the order of their first NPC id; the last boss is the third one (a dungeon has three), or the only/last one when the table lists fewer. The overrides below
fix the dungeons whose table holds extra monsters (hand-checked, Stacy 2026-11-23). The open-world instances are left out.
"""
import collections
import json
import sys

FIELD = {'Verteron', 'Altgard', 'Abyss', 'Eltnen', 'Morheim'}
OVERRIDES = {
    "Dying Dramata's Nest": ['Decaying Durvati', 'Decaying Durvati (Specimen)'],
    'Cave of the Stranded': ['Lamiphedon'],
    'Fire Temple': ["Kromede's Desire"],
    'Fire Temple (Conquest)': ["Kromede's Desire"],
    'Ferocious Horn Den': ['Ferocious Horn Nuakum'],
    'Ferocious Horn Den (Conquest)': ['Ferocious Horn Nuakum'],
    'Mirror of Scarlet Desire': ["Kromede's Desolation"],
}


def main(client):
    bosses = json.load(open(f'{client}/assets/aion2/npcs/boss_npcs.json', encoding='utf-8'))['bosses']
    by = collections.defaultdict(list)
    for npc, b in bosses.items():
        by[b['instance']].append((int(npc), b['name']))
    finals = {}
    for instance, rows in sorted(by.items()):
        if instance in FIELD or instance.startswith('Abyssal Forge: Ludra'):
            continue
        rows.sort()
        names = []
        for _, n in rows:
            if n not in names:
                names.append(n)
        if len(names) < 2 and instance not in OVERRIDES:
            continue  # a nightmare dungeon: one boss, killed in seconds: no battle music (Stacy, 2026-11-23)
        finals[instance] = OVERRIDES.get(instance) or [names[2] if len(names) >= 3 else names[-1]]
    out = {'_readme': 'The last boss of each dungeon (names as in boss_npcs.json), for the battle music of the Monsters theme. Edit a line to change which boss gets the music. Ludra has its own music.',
           'finals': finals}
    with open(f'{client}/assets/aion2/bosses/dungeon_finals.json', 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for k, v in finals.items():
        print(f'{k}: {", ".join(v)}')


if __name__ == '__main__':
    main(sys.argv[1])
