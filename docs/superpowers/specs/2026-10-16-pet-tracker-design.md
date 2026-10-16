# Pet tracker: souls left, zones to farm, a minimap over the game

Asked by Stacy (2026-10-16), after QuestLog's pet tracker: the meter shows every pet with the souls
still to collect, the zones where its monsters live, and - while farming - a small map over the game
with the player and the monsters he still needs, beside a list of the zone's pets and their counters.

## Decisions (Stacy)

- **Two parts** (option C): a **Pets window** to prepare, and **two panels over the game** to farm.
- **By zone**, as QuestLog does it: the window lists the zones with "N pets here, M souls left"; a
  **Farm here** button picks one; the panels then follow that zone.
- **Phase first**: the list and the panels show the pets of the player's current phase - first every
  pet collected (5 souls), then every pet to level 2 (25 more), then to level 3 (75 more). A **pin**
  on a pet keeps only the pinned ones (a targeted session); unpinning all goes back to the phase.
- **Data reuse, not re-extraction**: pet names, monsters and spawn points come from Skeeve's
  repository (MIT): `Client/assets/aion2/pets/pets.json`, `spawns.json`, `regions.json`, the pet
  icons. Credited in a README beside the files and in the window's footer. Updates of the game:
  take his new files.
- **Maps**: the game's own world maps, exported by Stacy with FModel (UI/Map/WorldMap/World_L_A,
  World_L_B, World_L_Starter: 8 x 8 tiles of 2048 px, 4 x 4 of 1024 for the starter zone), shipped
  reduced (a few MB per map). Stacy's decision: fan tools reuse game maps, the publisher tolerates it.
- **Look**: the "modern classic" of the overlay, the settings and the details window.
- **Principle kept**: read-only - no injection, no memory reading, no packet sent.

## What the packets say (checked on real captures, 2026-10-16)

- **Login** (opcode 0x0090): `u32 1012 | varint n | n x (pet id u32, pet id u32, level u32) | varint m
  | m x (pet id u32, souls towards the next level u32) | ...` - 143 pets of Stacy's, matching his
  pet window exactly (38/75, 26/75, 23/75 ... 24/25, 24/25, 23/25 ...). Skeeve reads it the same way.
- **Soul gained** (0x0D90): `01 | pet id u32 | souls gained u32` (1, 2, 3 or 4 at a time).
- **Level threshold** (0x0E90): `01 | pet id u32 | 25`; **level up** (0x1490): `00 00 | pet id u32 |
  new level u32 | ...`. Checked: 5 -> 12 -> 20 -> 25 -> level 2 -> 1/75 on the Unstable Spider (1117),
  the screenshots' counters and fights placed in the recording to the second.
- **Item on the ground** (0x1E56): position (3 floats) and item id (u32 at offset 39); **picked up**
  (0x1B56): item id at offset 13, then 0x0D90. Soul item 534660086 -> pet 1117 (Skeeve's `souls`
  table agrees).
- **Positions**: world centimetres as floats - the player in 0x2937 / 0x1C37 / 0x1F37 (id varint, then
  x y z at offset 5-6) and 0x2B38 every second (offset 35). The player's id changes at a teleport
  (as at a zone change - see Aion2EntityDirectory).
- **Which map**: the zone id (1010 = World_L_A / Verteron, as `WorldMapMapList` says) from the zone
  frame Skeeve reads (`Aion2Maps.OfZone`); to be confirmed on a capture while porting.

## Map calibration (World_L_A, checked)

The map is 8192 m square, centred on the world's origin: on the full 16384 px mosaic,
`pixel = (x / 100 + 4096) * 2`, `(y / 100 + 4096) * 2` (x, y in world centimetres), no flip. Two
points checked against the game's own map: the Exiles' Forest and the Drana Plantation (the
latter falls on the map's "DranaPlantation" place, (1125, -735) in map metres). World_L_B and the
starter zone get the same check (one capture + a map screenshot each) before they ship.

## Parts

1. **Pet data** (`Aion2/Pets/`): loads Skeeve's files; answers a pet's name (meter language, English
   fallback), its species, its monsters, the zones' pets, a zone's spawn points per pet.
2. **Collection** (`PetCollection`): state from the login frame, kept up to date by 0x0D90 / 0x1490;
   souls left per pet and per phase; raises "collected" and "level up" for the banner. Lives with
   the capture source like the entity directory; survives zone changes; reset at a new login frame.
3. **Position and map** (`PlayerPosition`, `WorldMaps`): the local player's position (id changes
   handled), the current map, world -> pixel per map.
4. **Pets window**: phase bar (Lv.1 / Lv.2 / Lv.3 with x/207), species filters, the list (icon,
   name, level, souls bar, monster, zone, pin), the zones list with **Farm here**; sorted closest to
   done first.
5. **Panels over the game** (both movable, lockable, remembered, click-through when locked):
   - **Tracker map**: the zone's map around the player, the player's arrow at the centre, a dot per
     spawn point of a needed pet's monster, the pet's icon on hover;
   - **Tracker list**: the zone's needed pets with icon, souls x/25 or x/75, spawn count; a soul
     gained moves the bar at once.
6. **Banner and sound** when a pet is collected or levels up (TeaseBanner, the MVP sounds' volume).
7. **Settings** (Overlay page): switch for the panels, their opacity.

## Delivery (one release each, preview before anything visible ships)

1. Parts 1 + 2 and a first plain Pets window: Stacy's real counters.
2. Part 4 (the full window) + part 6.
3. Parts 3 + 5 + 7 (maps and panels), World_L_A first, then B and the starter zone once calibrated.

## Tests

- SelfCheck: the login frame and the 0x0D90 / 0x0E90 / 0x1490 sequence from Stacy's captures (hex
  fixtures, nothing else of the recording), expecting 1/75 on pet 1117 at the end; the soul item ->
  pet link; a world point -> pixel on World_L_A (the two calibration points).
- Smoke harnesses: the Pets window and both panels on screen with the captured state; a real
  window before each release.

## Out of scope (later)

Full interactive map (NPCs, gathering, chests), field bosses and their timers (Skeeve has
`build_field_bosses.py` - next topic, Gartua), Asmodian maps, the Black Cloud pet shop.
