# Pet data

`pets.json` is a byte-exact copy of `Client/assets/aion2/pets/pets.json` from SkeeveTV's Aion 2 DPS meter
(https://github.com/SkeeveTV, MIT licence), taken on 2026-10-16.

Layout: `pets` (id -> key, category = species, names per language), `monsters` (npc id -> pet id or list of
pet ids), `souls` (soul item id -> pet id), `shop`.

Zones and monsters (delivery 2):

- `regions.json` and `spawns.json` are byte-exact copies of the files of the same name in Skeeve's repository
  (same date). `regions.json` = Verteron / Altgard / Abyss sub-zones with the pets found there (Altgard is the
  Asmodian side and is not used); `spawns.json` = spawn tables -> npc id -> [x, y, z] points in world
  centimetres.
- `monster_names.json` is NOT a copy: it is the subset of `Client/assets/aion2/npcs/npc_names.json` (Skeeve)
  limited to the npc ids listed in `pets.json` `monsters` (same shape, `{"npcId": {"en": ..., "fr": ...}}`),
  made by `Tools/pets/build_monster_names.py`.

The pet panels over the game (settings, Overlay page) draw these spawn points on the maps of
`Client/assets/aion2/maps` (maps: NCSoft (Aion 2), export by Stacy).

To refresh: in a clone of Skeeve's repository (`git fetch`), run
`git show origin/main:Client/assets/aion2/pets/pets.json > pets.json` (same for `regions.json` and
`spawns.json`) in this folder, then from the repository root
`python Tools/pets/build_monster_names.py <clone> Client/assets/aion2/pets/monster_names.json`,
then run `Aion DPS.dll selftest`.

Where each soul drops (2026-10-18):

- `soul_places.json` comes from SkeeveTV (sent on request, the output of his `build_soul_routes.py`): pet id ->
  `[map id, zone id, "STR_Subzone_...", npc id]` for every place the game's own "Obtention / Butin de monstre" list
  names (maps 1010 Verteron, 1110 Altgard, 20 lower Abyss). A pet with no entry has no monster drop in that list.
  The game shows an Elyos only the Elyos side: a pet whose places are all in Altgard reads "no monster drop" there.

## Licence (Skeeve's notice)

MIT License

Copyright (c) 2026 SkeeveTV

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
