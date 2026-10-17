# Field bosses

`field_bosses.json`: the field bosses of every map (NPC ids, positions, names in 8 languages, fixed times of the Abyss bosses).

- Source: SkeeveAN's Aion 2 DPS meter (https://github.com/SkeeveAN), commit 9c598172, MIT license. Copied unchanged.
- Built from the game's data files by his `Tools/aion2-dat/build_field_bosses.py`; the bosses of a map are in the order of their NPC ids, which is how the in-game list (opcode 01 91) numbers them.
- Read by `Client/Aion2/Aion2FieldBosses.cs`.
