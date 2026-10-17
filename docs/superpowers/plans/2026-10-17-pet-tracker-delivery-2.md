# Pet Tracker – Delivery 2 (full Pets window, banner and sound) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Pets window becomes the preparation tool of the spec (phase bar, species filters, rich pet cards with monsters and zones, pins, a zones list with "Farm here"), and the meter announces a pet collected or levelled up with the banner and a sound.

**Architecture:** Skeeve's zone data (`regions.json`, `spawns.json`) and a reduced monster-name table ship beside `pets.json`; a static `Aion2PetZones` answers "which zones hold which pets" and "which monsters drop a pet". A pure view builder `PetsView` turns collection + catalog + pins + filter into rows and zones (SelfCheck-tested); `PetsWindow` only renders it. The collection gets a `LevelRaised` event (raised only when a level really rises) that MainWindow turns into a banner + sound.

**Tech Stack:** C# / .NET 10 WPF, `SelfCheckAion2` scenarios, scratch smoke harnesses with real windows.

**Spec:** `docs/superpowers/specs/2026-10-16-pet-tracker-design.md` (parts 4 and 6)

## Global Constraints

- Read-only: no injection, no memory reading, no packet sent.
- Every decoding change is covered by a SelfCheck scenario; real capture frames as hex literals when a capture holds the frame (never the capture file).
- Souls needed: level 0 → 5, level 1 → 25, level 2 → 75, level 3 top (0). 207 pets in the catalog.
- Pet names in the meter language, English fallback, `#<id>` when unknown; monster names the same way.
- Data from Skeeve (MIT), credited in `Client/assets/aion2/pets/README.md` and in the window footer ("Données des familiers : SkeeveTV (MIT)").
- Look: "modern classic" (charcoal #141C24→#0B1016, cards #1A2430 / border #2A3846, purple #7B5CF0), as `PlayerDetailsWindow` / `FightHistoryWindow`.
- UI strings in `Client/assets/i18n/ui_strings.json` (en/de/fr), natural French.
- Elyos side only: zone groups shown = Verteron (`World_L_A`), Eltnen (`World_L_B`), the starter zone (`World_L_Starter`) and the Abyss. Asmodian maps are out of scope.
- No release (no version bump, tag or push).

## Review Focus

- No login list and nothing saved: the window still opens, shows every catalog pet at level 0 (phase 1) and the hint "log in again with the meter running"; pins/filters work.
- A pet whose monsters have no spawn point anywhere (event, shop pet): card shows "—" for zone/monster, it is never in a zone's count, never dropped from the list.
- Every pinned pet already at level 3 / all pins removed: the list goes back to the phase list, never an empty window without a message.
- The same level frame twice (the game repeats it, or the login list after a level-up): no second banner (LevelRaised only when the level rises).
- Window open while souls arrive on the capture thread: the card updates without a cross-thread exception (BeginInvoke, never Invoke).

---

### Task 1: Zones and monster names (Skeeve's data)

**Files:**
- Create: `Client/assets/aion2/pets/regions.json`, `Client/assets/aion2/pets/spawns.json` (byte copies of `origin/main:Client/assets/aion2/pets/…` in `C:\dev\Aion-upstream`), `Client/assets/aion2/pets/monster_names.json` (subset of `origin/main:Client/assets/aion2/npcs/npc_names.json` limited to the NPC ids of `pets.json` `monsters`, same shape `{"npcId": {"en": …, "fr": …}}`), `Tools/pets/build_monster_names.py` (the script that makes the subset; args: upstream repo path, output path)
- Modify: `Client/assets/aion2/pets/README.md` (the three new files, source, refresh steps)
- Create: `Client/Aion2/Protocol/Aion2PetZones.cs`
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunAion2PetZonesScenario`)

**Interfaces:**
- Consumes: `Aion2PetCatalog.PetIds`, `Aion2PetCatalog.MonstersOf(int)`.
- Produces:
  - `sealed record PetZone(string Key, string Map, IReadOnlyDictionary<string,string> Names, IReadOnlyList<int> Pets)` with `string NameIn(string language)` (language → en → Key). `Map` ∈ `"World_L_A" | "World_L_B" | "World_L_Starter" | "Abyss"`.
  - `static class Aion2PetZones`: `IReadOnlyList<PetZone> All`; `IReadOnlyList<PetZone> ZonesOf(int petId)`; `IReadOnlyList<string> MonsterNames(int petId, string language)` (distinct, sorted, at most what exists); `IReadOnlyDictionary<int, float[]> SpawnsOn(string spawnTable)` (npc id → x,y,x,y… in world cm, used by delivery 3); `string SpawnTableOf(string map)` (`"World/World_L/World_L_A"` …, Abyss → `"Intersever/Abyss/Abyss_Reshanta_A"`).
  - Zones = regions.json group `verteron` (Map `World_L_A`) and group `abyss` (Map `Abyss`), plus one whole-map zone `Eltnen` (Map `World_L_B`, names from `instances.json` key `World/World_L/World_L_B`, or ui string fallback) and one `Starter` zone (Map `World_L_Starter`, names `{"en":"Starter zone","fr":"Zone de départ","de":"Startgebiet"}`), whose pets are the pets of every monster with a spawn point in that spawn table. Group `altgard` is skipped. Lazy load, missing/corrupt file → empty, never throws.

- [ ] **Step 1: Write the failing test** – `RunAion2PetZonesScenario`: `All.Any(z => z.Key == "STR_Subzone_OwllauVillage" && z.Pets.Contains(1034))`; `All.Single(z => z.Map == "World_L_B").Pets.Count > 0`; `All.Single(z => z.Map == "World_L_Starter").Pets.Count > 0`; `!All.Any(z => z.Key.Contains("Altgard") || z.Map == "Altgard")`; `ZonesOf(1117).Count > 0`; `MonsterNames(1117, "fr").Count > 0` and the first is not empty; `ZonesOf(99999).Count == 0`; `SpawnsOn("World/World_L/World_L_A").Count > 100`. Print the zone count per map and `ZonesOf(1117)` French names.
- [ ] **Step 2:** build + selftest – fails (type missing).
- [ ] **Step 3:** copy the data, write the script and run it, update the README, implement `Aion2PetZones`. Check the output folder gets the new files (csproj `assets/**` glob).
- [ ] **Step 4:** selftest – scenario True, ALL CHECKS PASSED. Note the size of `monster_names.json` in the report (expect well under 1 MB).
- [ ] **Step 5:** commit "Pets: zones, spawn points and monster names (Skeeve's data)".

### Task 2: Level raised, pet obtained frame, banner and sound

**Files:**
- Modify: `Client/Aion2/Aion2PetCollection.cs` (event `LevelRaised`), `Client/Aion2/Protocol/Aion2Protocol.cs` + `Client/assets/aion2/protocol/opcodes.json` (`"petAdded": [2448]`, family `PetAdded`), `Client/Aion2/Protocol/Aion2FrameDecoder.cs` (decode `0x0990`: pet id u32 at frame offset 4, level u32 at 8 → `ApplyLevel`), `Client/Ui/MainWindow.xaml.cs` (subscribe, BeginInvoke, banner + sound), `Client/Ui/MeterSettings.cs` (`bool PetAnnouncements = true`), `Client/Ui/SettingsWindow.xaml(.cs)` (one switch "Annoncer les familiers obtenus et montés de niveau" on the "Fin de boss & sons" page), `Client/assets/i18n/ui_strings.json`
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunAion2PetLevelRaisedScenario`); scratch harness `smokepetbanner`

**Interfaces:**
- Consumes: `Aion2PacketCombatSource.Pets` (`Aion2PetCollection`), `TeaseBanner.ShowLines(IReadOnlyList<(string Text, bool Gold)> lines, Window meter, bool middle = false)`, `TeaseSound.Play(bool fanfare, double volume)`, `MeterSettings.SoundVolume`.
- Produces: `event Action<Aion2PetState, int>? LevelRaised` on `Aion2PetCollection` (state after, previous level), raised outside the lock, only when the new level is higher than the old one (ApplySouls collecting at 5 souls, ApplyLevel, petAdded). Banner text (fr): level 1 → `"Familier obtenu : {name}"`, else `"{name} passe au niveau {level}"`, gold; sound `TeaseSound.Play(fanfare: true, settings.SoundVolume)`; nothing when `PetAnnouncements` is off; nothing for the login list or a restore.

- [ ] **Step 1: Write the failing test** – `RunAion2PetLevelRaisedScenario` through `Ingest`: login frame fixture of delivery 1 (reuse the existing hex constant), then a level frame for a pet at level 1 in that list raising it to 2 (build it from the real frame `149000005D0400000200000003070000001248000000000000` with the pet id bytes swapped to that pet – say which pet in a comment) → one `LevelRaised` with previous 1; the same frame again → no second event; the real 1117 level-2 frame on 1117 (already level 2) → no event; `ApplySouls(2074, 5)` on a pet not in the list → event with previous 0, state (2074, 1, 0); a `0x0990` frame `0990` + `00 00` + pet id u32 + level u32 (layout from Skeeve, no capture holds one – say so in a comment) for a level-0 pet of the list → event, level 1; the login list frame again → no event.
- [ ] **Step 2:** selftest – fails.
- [ ] **Step 3:** implement the event, the family + decode, the MainWindow handler (BeginInvoke on the dispatcher), the setting and its switch, the strings.
- [ ] **Step 4:** selftest – True, ALL CHECKS PASSED. Smoke harness `smokepetbanner` (scratch, real meter window shown, then calls the handler path with a fake LevelRaised for 1117): banner on screen, screenshot `C:\Users\stacy\OneDrive\Bureau\PDF\familiers_bandeau.png`; no sound check needed beyond no exception.
- [ ] **Step 5:** commit "Pets: banner and sound when a pet is obtained or levels up".

### Task 3: Pets view builder (pure logic)

**Files:**
- Create: `Client/Ui/PetsView.cs`
- Modify: `Client/Ui/MeterSettings.cs` (`List<int> PetPins = new()`, `string? PetFarmZone`)
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunPetsViewScenario`)

**Interfaces:**
- Consumes: `Aion2PetCollection` (`Pets`, `Phase`, `Needed(int)`), `Aion2PetCatalog` (`PetIds`, `Name`, `Species`), `Aion2PetZones` (`All`, `ZonesOf`, `MonsterNames`).
- Produces:
  - `sealed record PetRow(int PetId, string Name, string? Species, int Level, int Souls, int Needed, int SoulsLeft, IReadOnlyList<string> Monsters, string? Zone, bool Pinned)`; `SoulsLeft` = souls still needed to reach the phase target level (e.g. level 0 at phase 2 = 5 + 25 − souls).
  - `sealed record PetZoneRow(string Key, string Name, string Map, int PetsHere, int SoulsLeft, bool Chosen)`.
  - `sealed record PhaseStep(int Level, int Reached, int Total)` (catalog pets at ≥ Level / 207).
  - `sealed record PetsViewModel(int Phase, IReadOnlyList<PhaseStep> Steps, IReadOnlyList<PetRow> Rows, IReadOnlyList<PetZoneRow> Zones, bool PinnedOnly, int SoulsLeftTotal)`.
  - `static PetsViewModel PetsView.Build(Aion2PetCollection pets, string language, ISet<int> pins, string? species, string? farmZone)`: rows = pins ∖ level-3 pets when that set is non-empty (`PinnedOnly` true), else `Needed(Phase)`; species filter on top (`null` = all); sorted by `SoulsLeft` then name (current culture); zones = zones holding at least one row's pet, `PetsHere` = rows in that zone, `SoulsLeft` = their sum, sorted by `SoulsLeft` ascending then name; `Zone` of a row = first of `ZonesOf` in `All` order, `null` when none.

- [ ] **Step 1: Write the failing test** – `RunPetsViewScenario` on a collection built from the delivery-1 login fixture: no pins → `Phase == 1`, `Rows.All(r => r.Level == 0)`, rows sorted by `SoulsLeft`, `Steps[0].Total == 207`, `Steps[0].Reached == 143`; pins {1117, 1034} → `PinnedOnly`, rows = those two, and a pinned row aims at its next level: `SoulsLeft == Needed − Souls` (1117 at (2, 1) → 74; 1034 at (1, 20) → 5); pin of a level-3 pet only → `PinnedOnly == false`; species "Feral" → every row's `Species == "Feral"`; a pet id with no zone → row with `Zone == null`, not in any zone's `PetsHere`; `farmZone` set to a zone key → that zone row `Chosen`.
- [ ] **Step 2:** selftest – fails.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** selftest – True, ALL CHECKS PASSED.
- [ ] **Step 5:** commit "Pets: view builder (phase, pins, filters, zones)".

### Task 4: Full Pets window

**Files:**
- Modify: `Client/Ui/PetsWindow.xaml`, `Client/Ui/PetsWindow.xaml.cs` (rebuilt on `PetsView.Build`), `Client/assets/i18n/ui_strings.json`
- Test: scratch harness `smokepets2` (real meter window + PetsWindow on screen with the delivery-1 fixture collection)

**Interfaces:**
- Consumes: `PetsView.Build`, `MeterSettings.PetPins` / `PetFarmZone` (saved with `MeterSettings.Save()` on change), `Aion2PetCollection.Changed` / `ListReplaced` (BeginInvoke, coalesced: at most one rebuild per 250 ms).
- Produces: the window. Layout: header (title "Familiers", character + "liste lue le <date>"), phase bar = three steps "Niv. 1 · 143/207", "Niv. 2 · x/207", "Niv. 3 · x/207" with the current phase highlighted in purple; species chips (Tous + the five species, French names from the game's texts in `C:\dev\aion2-data\l10n` when found, else Intellect/Sauvage/Nature/Transformation/Spécial); left: pet cards (species badge with the name's initial as the icon – no icon set exists upstream; name, "Niv. N", souls bar `souls/needed`, monsters (first two, "+N"), zone, pin toggle 📌); right: zones list cards "N familiers · M âmes" with a "Farmer ici" button (chosen zone highlighted, button becomes "Zone suivie"); footer credit "Données des familiers : SkeeveTV (MIT)"; empty states of Review Focus.

- [ ] **Step 1:** harness `smokepets2`: opens the window with the fixture collection; prints phase steps, first five rows, first five zones; clicks the pin of the first row, prints rows again (`PinnedOnly`); clicks "Farmer ici" on the first zone; renders PNGs of the window (`familiers_fenetre.png`, `familiers_fenetre_epingles.png`) into `C:\Users\stacy\OneDrive\Bureau\PDF\`; restores the settings file.
- [ ] **Step 2:** implement the window.
- [ ] **Step 3:** run the harness, look at both PNGs (alignment, French text, no clipped text at 900×640 and at the minimum size), fix, re-run.
- [ ] **Step 4:** selftest ALL CHECKS PASSED; commit "Pets: full Pets window (phase, filters, pins, zones)".
