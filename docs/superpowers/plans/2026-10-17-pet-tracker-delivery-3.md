# Pet Tracker – Delivery 3 (maps, position, panels over the game) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While farming, two panels over the game: a minimap of the zone around the player with the spawn points of the monsters he still needs, and a list of the zone's needed pets with live soul counters.

**Architecture:** The game's world maps ship as reduced JPEG tiles (1024 px, 1 m per pixel) with a small `maps.json`; `Aion2WorldMaps` turns a world position into a map pixel. The decoder learns the zone frame (0x2136) and the local player's position frames; `Aion2PlayerPosition` (owned by the packet source) keeps the last position and zone. Two small topmost windows (`PetMapPanel`, `PetListPanel`), movable, lockable (click-through when locked), remembered, follow the chosen farm zone (delivery 2's `MeterSettings.PetFarmZone`) or else the player's map.

**Tech Stack:** C# / .NET 10 WPF, Python + Pillow for the tile build script, `SelfCheckAion2` scenarios, scratch smoke harnesses with real windows.

**Spec:** `docs/superpowers/specs/2026-10-16-pet-tracker-design.md` (parts 3, 5, 7; "Map calibration")

## Global Constraints

- Read-only: no injection, no memory reading, no packet sent.
- Calibration: a map covers its level bounds (World_L_A 8192 m, World_L_B 8192 m, World_L_Starter 4096 m – `LevelBounds` scale in the `.umap` json), centred on the world origin, no flip: `pixel = (x / 100 + size / 2) / metresPerPixel`, `(y / 100 + size / 2) / metresPerPixel`, x, y in world cm. Shipped tiles: 1024 px, 1 m per pixel (World_L_A 8 × 8, World_L_B 8 × 8, World_L_Starter 4 × 4). World_L_A is checked on captures; B and the starter zone are derived from the level bounds and checked only against Skeeve's spawn points (all land on the map) – say so in the README.
- Zone id → map: 1010 and 902 → World_L_A; 1011 and 904 → World_L_B; 100001 and 100002 (Poeta) → World_L_Starter; anything else → no map (panels show "no map for this zone").
- Captures never committed; fixtures are hex frames copied from `SCRATCH\petdump\frames_*.txt`.
- Map files never in a public repo unless Stacy decided (he did: fan tools reuse game maps) – credit "Cartes : NCSoft (Aion 2), export de Stacy" in the README beside the tiles.
- Look: "modern classic" (cards #1A2430 / #2A3846, purple #7B5CF0), panel background semi-transparent with the settings' opacity.
- No release.

## Review Focus

- Position unknown (meter started while standing still, no position frame yet): the map panel shows the zone's map centred on the zone's spawn points with "position inconnue", never a black box or a crash.
- A zone change to an instance (dungeon) or a zone with no map: panels show the "no map here" state and come back when the player returns.
- Spawn points of a needed pet on another map than the current one: never drawn on the wrong map (filter by spawn table of the current map).
- Locked panels: clicks go to the game (WS_EX_TRANSPARENT), the meter's settings unlock them; positions survive a restart and stay on a visible screen.
- A teleport changes the player's entity id: the position keeps following (zone frame position + next local position frame).

---

### Task 1: World map tiles and calibration

**Files:**
- Create: `Tools/pets/build_map_tiles.py` (args: source folder `C:\dev\aion2-data\maps`, output `Client/assets/aion2/maps`; reads `<Map>/Res/<Map>_<col>_<row>.png`, resizes to 1024 px when bigger, writes `<Map>/<col>_<row>.jpg` quality 80, writes `maps.json`)
- Create: `Client/assets/aion2/maps/maps.json`, `Client/assets/aion2/maps/World_L_A/*.jpg` (64), `World_L_B/*.jpg` (64), `World_L_Starter/*.jpg` (16), `Client/assets/aion2/maps/README.md`
- Create: `Client/Aion2/Protocol/Aion2WorldMaps.cs`
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunAion2WorldMapsScenario`)

**Interfaces:**
- Produces:
  - `sealed record Aion2WorldMap(string Key, string SpawnTable, int Grid, int TilePixels, double SizeMetres, double MetresPerPixel)` with `(double X, double Y) ToPixel(float worldX, float worldY)`, `int MapPixels => Grid * TilePixels`, `string TilePath(int col, int row)` (`assets/aion2/maps/<Key>/<col>_<row>.jpg`).
  - `static class Aion2WorldMaps`: `IReadOnlyList<Aion2WorldMap> All`; `Aion2WorldMap? Get(string key)`; `Aion2WorldMap? OfZone(int zoneId)` (table in Global Constraints); lazy, missing file → empty, never throws.
  - `maps.json`: `{"maps":{"World_L_A":{"spawnTable":"World/World_L/World_L_A","grid":8,"tile":1024,"size":8192}, …}}`.

- [ ] **Step 1: Write the failing test** – `RunAion2WorldMapsScenario`: `All.Count == 3`; World_L_A `ToPixel(0, 0) == (4096, 4096)`; `ToPixel(112500, -73500)` (Drana Plantation place, map metres (1125, -735)) == (5221, 3361) ±1; the position of the 23-09-45 zone frame (x, y floats at 18 and 22 of `213602000000F2030000…` in `frames_23-09-45.txt`) lands within 200 px of that place; World_L_Starter `ToPixel(0,0) == (2048, 2048)`; `OfZone(1010).Key == "World_L_A"`, `OfZone(1011).Key == "World_L_B"`, `OfZone(600001) == null`; every tile file of every map exists.
- [ ] **Step 2:** selftest – fails.
- [ ] **Step 3:** write and run the script (record total size per map in the README: about 5.5 MB A, 6.2 MB B, 1.4 MB starter), write `Aion2WorldMaps`.
- [ ] **Step 4:** selftest – True, ALL CHECKS PASSED.
- [ ] **Step 5:** commit "Pets: world map tiles (Verteron, Eltnen, starter zone) and calibration".

### Task 2: Zone and player position from the packets

**Files:**
- Create: `Client/Aion2/Aion2PlayerPosition.cs`
- Modify: `Client/Aion2/Protocol/Aion2Protocol.cs` + `opcodes.json` (`"zone": [8502]` – 0x2136 – if the family slot `zone` is free in our fork, else a new name `zoneChange`; `"localPosition": [15415]` – 0x3C37), `Client/Aion2/Protocol/Aion2FrameDecoder.cs`, `Client/Aion2/Aion2PacketCombatSource.cs` (owns `Position`)
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunAion2PlayerPositionScenario`)

**Interfaces:**
- Consumes: the entity directory's knowledge of the local player id (how the fork already tells the local player's entity id; 0x3C37 frames of other entities are ignored).
- Produces: `sealed class Aion2PlayerPosition`: `(float X, float Y, DateTime At)? Last`; `int ZoneId`; `Aion2WorldMap? Map => Aion2WorldMaps.OfZone(ZoneId)`; `event Action? Changed` (raised outside its lock, at most every 200 ms of capture time); `void NoteZone(int zoneId, float? x, float? y, DateTime at)`; `void NotePosition(float x, float y, DateTime at)`. Lock-guarded. `Aion2PacketCombatSource.Position`.
- Frames: zone 0x2136 = zone id u32 at offset 6, x/y floats at 18/22 when the frame is ≥ 26 bytes and values are finite and within ±500 000; local position 0x3C37 = opcode | entity varint | 03 | x f32 | y f32. Also take the local player's own position from the movement frames the spec names (0x2937 / 0x1C37 / 0x1F37: entity varint then x, y floats at the offset Skeeve's `PositionOffset` gives – see `SCRATCH\skeeve\sk_FrameDecoder.cs` around line 1544) when the entity is the local player.

- [ ] **Step 1: Write the failing test** – `RunAion2PlayerPositionScenario` through `Ingest` with real frames: the login zone frame of `frames_22-49-55.txt` (`213601000000F2030000…`) → `ZoneId == 1010`, `Map.Key == "World_L_A"`, `Last` set from its x/y; then the 23-09-45 zone frame → `Last` within 1 cm of its floats, `Changed` raised; a real 0x3C37 frame of the local player from `frames_22-42-29.txt` (pick one whose entity is the local player – the scenario must make that entity the local player the way existing scenarios do) → `Last` updated; a 0x3C37 frame of another entity → unchanged; a short zone frame (8 bytes) → ignored.
- [ ] **Step 2:** selftest – fails.
- [ ] **Step 3:** implement families, decoding, `Aion2PlayerPosition`, the source property.
- [ ] **Step 4:** selftest – True, ALL CHECKS PASSED. Scratch replay of `capture_2026-10-16_23-09-45.jsonl`: print `ZoneId`, map key and pixel at start, after the teleport and at the end; the end pixel must lie on the Drana Plantation (≈ (5221, 3361) ± 250 px).
- [ ] **Step 5:** commit "Pets: zone and player position from the packets".

### Task 3: Map panel and list panel

**Files:**
- Create: `Client/Ui/PetPanels.cs` (shared: topmost tool window base `PetPanelWindow` – no taskbar entry, drag to move when unlocked, click-through when locked via WS_EX_TRANSPARENT|WS_EX_LAYERED like `TeaseBanner`/`NativeOverlay`, position/size remembered in `MeterSettings`, clamped to a visible screen), `Client/Ui/PetMapPanel.cs`, `Client/Ui/PetListPanel.cs`, `Client/Ui/PetTrackerView.cs` (pure: what the panels show)
- Modify: `Client/Ui/MeterSettings.cs` (`bool PetPanels = false`, `bool PetPanelsLocked = false`, `double PetPanelsOpacity = 0.85`, `double? PetMapLeft/Top`, `double? PetListLeft/Top`, `double PetMapSize = 260`), `Client/Ui/MainWindow.xaml.cs` (create/close panels with the setting, feed them on `Position.Changed` / collection `Changed`, BeginInvoke), `Client/assets/i18n/ui_strings.json`
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunPetTrackerViewScenario`); scratch harness `smokepetpanels`

**Interfaces:**
- Consumes: `Aion2WorldMaps`, `Aion2PlayerPosition`, `Aion2PetZones.All` / `SpawnsOn` / `SpawnTableOf`, `Aion2PetCatalog.MonstersOf` / `Name`, `PetsView.Build` (delivery 2) for the needed pets and pins, `MeterSettings.PetFarmZone`.
- Produces: `sealed record TrackerDot(int PetId, double PixelX, double PixelY)`; `sealed record TrackerPet(int PetId, string Name, int Level, int Souls, int Needed, int SpawnCount)`; `sealed record TrackerModel(Aion2WorldMap? Map, string ZoneName, (double X, double Y)? Player, IReadOnlyList<TrackerDot> Dots, IReadOnlyList<TrackerPet> Pets)`; `static TrackerModel PetTrackerView.Build(Aion2PetCollection pets, Aion2PlayerPosition position, string language, ISet<int> pins, string? farmZone)`. The zone = chosen farm zone (its map) or else the player's map (all its needed pets); dots = spawn points on that map's spawn table of the monsters of the needed pets of that zone, de-duplicated within 5 px; pets sorted by souls left.
- Map panel: square, `PetMapSize` px, shows ±(size/2) m around the player (1 px = 1 m at 100 %, mouse wheel zoom 50–200 % when unlocked), tiles loaded lazily (cache the 9 around), player arrow at the centre (purple), dots coloured per pet (stable hue from the id), hover a dot → tooltip with the pet name (unlocked only), zone name at the top, "position inconnue" state of Review Focus.
- List panel: zone name, each needed pet: species badge, name, bar `souls/needed`, "N points" spawn count; updates at once on a soul.

- [ ] **Step 1: Write the failing test** – `RunPetTrackerViewScenario`: delivery-1 login fixture + zone frame 1010 + position of the 23-09-45 zone frame → `Map.Key == "World_L_A"`, `Player` ≈ (5221, 3361) ± 250, `Dots.Count > 0`, every dot inside the 8192 px map, every dot's pet is in `Pets`; farm zone = the Owllau village zone key → only pets of that zone; zone 600001 → `Map == null`, `Dots` empty; no position → `Player == null`, dots still listed.
- [ ] **Step 2:** selftest – fails.
- [ ] **Step 3:** implement view, panels, settings, MainWindow wiring.
- [ ] **Step 4:** selftest – True. Harness `smokepetpanels`: real meter window + both panels on screen fed by the fixtures (player at the Drana Plantation, then at the Exiles' Forest); prints the model; toggles locked and checks the extended style bit; renders `C:\Users\stacy\OneDrive\Bureau\PDF\familiers_minicarte.png` and `familiers_liste_panneau.png`; restores the settings file. Look at both PNGs and fix what looks wrong.
- [ ] **Step 5:** commit "Pets: map and list panels over the game".

### Task 4: Settings and credits

**Files:**
- Modify: `Client/Ui/SettingsWindow.xaml(.cs)` (Overlay page: a card "Familiers" with switch "Panneaux de farm des familiers", switch "Verrouiller (les clics passent au jeu)", opacity slider 30–100 %, map size slider 180–400 px), `Client/assets/i18n/ui_strings.json`, `README.md` + `README.fr.md` (a "Pets" / "Familiers" section: what it does, data credit SkeeveTV MIT, maps credit), `Client/assets/aion2/pets/README.md`
- Test: scratch harness `smokepetsettings` (Settings window on the Overlay page, toggling the switch shows/hides the panels)

- [ ] **Step 1:** implement the card, wire it to the settings and MainWindow (same apply path as the other overlay switches).
- [ ] **Step 2:** harness: settings window on screen, switch on → both panels visible, lock → click-through bit set, opacity → panel opacity; render `C:\Users\stacy\OneDrive\Bureau\PDF\familiers_parametres.png`.
- [ ] **Step 3:** selftest ALL CHECKS PASSED; commit "Pets: panel settings and credits".
