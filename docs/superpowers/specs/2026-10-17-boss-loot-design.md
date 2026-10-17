# Field boss timers, world loot, quiet background updates

Asked by Stacy (2026-10-17, overnight task), after QuestLog's app: "Field boss timers - respawn timers
read from the world map", "World loot - the items you picked up while playing, grouped by session and
map", "Updates itself - new versions download in the background and install when you restart the app".
Instruction: as far as possible without validation, check Skeeve's work first, no release.

## Decisions (made overnight, to be confirmed by Stacy)

- **Nothing shared between players.** QuestLog shares boss timers between users of a server; the meter's
  only outbound call stays the update check (Stacy's principle). Sharing = possible later option only.
- **Reuse Skeeve (MIT), credited**: `Client/Aion2/Aion2FieldBosses.cs` (list parser, state, fixed Abyss
  times) and `Client/assets/aion2/bosses/field_bosses.json` (85 named field bosses, 6 maps, 5 languages,
  built by his `Tools/aion2-dat/build_field_bosses.py`) from SkeeveAN/Aion-DPS-Meter origin/main 9c598172.
  Credit in `Client/assets/aion2/bosses/README.md` and in the boss window's footer.
- **Watched bosses**: a star per boss; Eternal Gartua (NPC 2101074, Verteron) is watched by default.
- **Footer**: watched bosses next to "Rift ... · Shugo ...": `Gartua in 1h12` / `Gartua here!` (green) /
  `Gartua ?` (unknown, tooltip: open the in-game world map's field boss list once). Setting on by default.
- **Mid-screen reminder** like the Shugo one (TeaseBanner.ShowLines middle), for watched bosses: 5 minutes
  before the respawn and at the respawn. Setting off by default (same rule as the Shugo reminder).
- **Loot = drops the player picked up**, local only, in `%AppData%\Aion DPS Meter\aion2-loot.json`.
- **Updates**: already downloaded in the background every 5 minutes (MainWindow.RunUpdateCheck) and applied
  by Velopack at the next start (`SetAutoApplyOnStartup`, on by default in 1.2.0). Only made explicit and
  told quietly, in French: no new behaviour, no restart forced.

## What the packets say (checked on real captures)

- **Field boss list** (opcode 0x0191, Skeeve's reading): `0191 | 0000 | map u32 | count u8 | count x
  { alive u8, place varint (= map*100 + n), [alive: x y z f32], [sometimes 1 more byte], time i64 ms UTC }`.
  Dead: time = when it comes back; alive: when it appeared. Places are numbered in the order of the map's
  NPC ids in field_bosses.json. Capture 2026-10-16 23:09:45 (Verteron = map 1010, 24 bosses): 315-byte
  frames read as Skeeve says (Eternal Gartua = place 21, dead, back at 1792190148937 = 2026-10-17 00:35:48
  local; Rotten Kutar = place 2, alive at x -27290 y -80782). 412-byte frames of the same moment add, after
  the 24th boss, a byte 00 and a second list (count 03, other markers): Skeeve's parser rejects them because
  the tail is not zeros. **Change**: after the last boss, a tail `00 | n (1..64) | ...` is accepted too.
  Instance maps (600xxx) send other 0x0191 frames (count 0 or other places) - rejected as before.
  The frames came about once a second while the world map was open (2026-10-08: 115 frames over 2 min).
  Stacy must open the world map's field boss list once per map to fill the timers.
- **Zone** (0x2136): zone id u32 at offset 6 (1010 Verteron, 600002 Krao Cave, ...), same numbers as the
  0x0191 map. Only read (observed), never consumed - the pet branch decodes the same frame.
- **Item on the ground** (0x1E56, 67 bytes): `1E56 01 | drop id (4 bytes, offset 3) | x y z f32 (7) |
  x y z f32 (19) | u32 (31) | loot id u32 (35) | item id u32 (39) | count u32 (43) | 02 | owner entity u32
  (48) | ...`.
- **Item gone from the ground** (0x1F56, 24 bytes): `1F56 01 | drop id (3) | u32 | loot id | owner u32 (15)
  | ... | last byte 00 picked / 01 vanished`.
- **Inventory slot** (0x1B56, 43 bytes): `1B56 01 | kind (3): 00 new slot, 01 count changed, 02 removed |
  00 | slot | ... | item id u32 (13) | count u64 (17)`.
- **Picked up by me** = a 0x1F56 for a drop seen in 0x1E56, last byte 00, with an inventory add (0x1B56
  kind 00 or 01) of the same item at most 1.5 s before. On capture 2026-10-16 22:42:29 (solo spider farm)
  this gives 26 pickups, all owned by Stacy's entity; the 29 others (other players' loot, picked by them or
  vanished) have no inventory add. Owner ids are not used: the player's id changes at a teleport.
- **Item names**: gear from `assets/aion2/items/item_info.json` (English) + French/German/... from Skeeve's
  `Backend/src/data/aion2/item_names_i18n.json` (keyed by English name; only gear names are copied);
  pet souls from `assets/aion2/pets/pets.json` `souls` when that file is there (pet branch); else
  "Item #id". Stacy can name an unknown item (double-click), kept in `aion2-item-names.json`.

## What Stacy sees

1. **Overlay footer**: `Rift in 2h · Shugo in 12 min · Gartua in 1h12`.
2. **App menu > Field bosses**: window in the "modern classic" look; maps with a known list first; per boss:
   star, name, state (`here` / `back in 1h12 (00:35)` / `unknown`), "list seen at 23:09". Footer: credit
   + "Open the field boss list of the in-game world map to update the times".
3. **App menu > Loot**: window; left = sessions (a session ends after 30 min without a pickup or at a meter
   start), right = the session's maps, each with its items `name x count`, newest first. Keeps 60 sessions.
4. **Settings > Overlay**: "Field boss timer in the footer" (on), "Field boss reminder in the middle of the
   screen" (off). **Settings > Updates**: quiet line "Version X downloaded - it installs by itself at the next
   start of the meter" when one is waiting.

## Not done / later

- Sharing timers between players (needs a server: refused by principle for now).
- Respawn when the player kills a boss without opening the map (respawn durations are not in the data).
- Full item name table (needs Skeeve's `Item.dat` reader + game files; souls come with the pet branch).
- Timers per server (an alt on another server overwrites the list of the same map).
