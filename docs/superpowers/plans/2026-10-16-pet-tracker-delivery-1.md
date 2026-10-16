# Pet Tracker – Delivery 1 (pet data, collection, first Pets window) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The meter knows every pet's name and Stacy's live soul counters, and shows them in a first plain Pets window.

**Architecture:** Skeeve's `pets.json` (MIT) ships as data and is read by a catalog class. Three new opcode families (pet list at login, soul gained, level up) feed an `Aion2PetCollection` owned by the packet source, saved per character so it survives a meter restart. A simple WPF window lists the pets of the current phase, closest to done first, and refreshes when the collection changes.

**Tech Stack:** C# / .NET 10 WPF, existing `Aion2FrameDecoder` / `opcodes.json`, `SelfCheckAion2` scenarios.

**Spec:** `docs/superpowers/specs/2026-10-16-pet-tracker-design.md`

## Global Constraints

- Read-only: no injection, no memory reading, no packet sent.
- Every decoding change is covered by a SelfCheck scenario built on real capture frames (hex fixtures only, nothing else of the recording).
- Opcodes are data in `Client/assets/aion2/protocol/opcodes.json` (decimal): pet list 144 (0x0090), soul gained 3472 (0x0D90), level up 5264 (0x1490).
- Souls needed: level 0 (not collected) 5, level 1 → 25, level 2 → 75, level 3 is the top (0).
- Pet names from `pets.json` in the meter language, English fallback, `#<id>` when unknown.
- Skeeve credited (README beside the data, footer of the window); MIT licence text kept.
- Look: "modern classic" (charcoal gradient, cards #1A2430 / #2A3846, purple #7B5CF0), as `PlayerDetailsWindow`.
- Before release: a real window on screen (smoke harness), selftest "ALL CHECKS PASSED"; publish = `git push fork HEAD:main` + tag.

## Review Focus

- Meter started after the login: no pet list frame this session → the saved collection of that character is shown, with "last seen at login on <date>"; nothing saved → a hint "log in again with the meter running".
- A soul for a pet absent from the login list (a pet never touched before): created at level 0 with those souls, x/5.
- A pet id missing from `pets.json` (game patch): listed as `#1234`, never dropped.
- Another character logs in: its own list replaces the shown one; the saved file is per character name.
- Level 3 pets: counted as done in every phase, never "needed".

---

### Task 1: Pet catalog (Skeeve's data)

**Files:**
- Create: `Client/assets/aion2/pets/pets.json` (copy of `origin/main:Client/assets/aion2/pets/pets.json` from `C:\dev\Aion-upstream`), `Client/assets/aion2/pets/README.md` (source, date, MIT credit to SkeeveTV, how to refresh)
- Create: `Client/Aion2/Protocol/Aion2PetCatalog.cs`
- Test: `Client/Combat/SelfCheckAion2.cs` (new `RunAion2PetCatalogScenario`, added to the list)

**Interfaces:**
- Produces: `static class Aion2PetCatalog` with `IReadOnlyList<int> PetIds`, `string Name(int petId, string language)`, `string? Species(int petId)` (`"Intellect" | "Feral" | "Nature" | "Trans" | "Special"` as in the file's `category`), `int? PetOfSoulItem(int itemId)` (from `souls`), `IReadOnlyList<int> MonstersOf(int petId)` (inverse of `monsters`). Loaded lazily from `AppContext.BaseDirectory/assets/aion2/pets/pets.json`; a missing/corrupt file gives empty results, never throws.

- [ ] **Step 1: Write the failing test** – `RunAion2PetCatalogScenario`: `PetIds.Count == 207`; `Name(1117,"fr") == "Araignée instable"`; `Name(1117,"en") == "Unstable Spider"`; `Name(1117,"tr") == "Unstable Spider"` (fallback); `Name(99999,"fr") == "#99999"`; `PetOfSoulItem(534660086) == 1117`; `Species(1117) == "Feral"`; `MonstersOf(1034).Contains(2000924)`.
- [ ] **Step 2:** build, run `dotnet "Aion DPS.dll" selftest` from `Client\bin\Release\net10.0-windows` – expect the new scenario to fail (type missing → build error first).
- [ ] **Step 3:** copy the data, write the README, implement `Aion2PetCatalog`. Make sure `assets/**` already copies to output (csproj glob).
- [ ] **Step 4:** selftest – scenario True, ALL CHECKS PASSED.
- [ ] **Step 5:** commit "Pets: Skeeve's pet data (names, species, monsters, soul items)".

### Task 2: Collection from the packets

**Files:**
- Modify: `Client/Aion2/Protocol/Aion2Protocol.cs` (enum `OpcodeFamily`: `PetList`, `PetSouls`, `PetLevel`), `Client/assets/aion2/protocol/opcodes.json` (`"petList": [144], "petSouls": [3472], "petLevel": [5264]`)
- Create: `Client/Aion2/Aion2PetCollection.cs`
- Modify: `Client/Aion2/Protocol/Aion2FrameDecoder.cs` (three cases + decode methods), `Client/Aion2/Aion2PacketCombatSource.cs` (owns the collection, passes it to the decoder, exposes it)
- Test: `Client/Combat/SelfCheckAion2.cs` (`RunAion2PetCollectionScenario`)

**Interfaces:**
- Consumes: `Aion2PetCatalog.PetIds` (phase totals).
- Produces:
  - `sealed record Aion2PetState(int PetId, int Level, int Souls)` with `int Needed` (5/25/75/0 by level) and `bool IsTop` (level 3).
  - `sealed class Aion2PetCollection`: `IReadOnlyDictionary<int, Aion2PetState> Pets`; `DateTime? ListedAt`; `string? Character`; `void ApplyList(IEnumerable<(int Id, int Level)> levels, IEnumerable<(int Id, int Souls)> souls, DateTime at)` (replaces everything); `void ApplySouls(int petId, int gained)`; `void ApplyLevel(int petId, int level)` (souls → 0); `int Phase` (1 while any catalog pet is below level 1, then 2 while any below 2, then 3); `IReadOnlyList<Aion2PetState> Needed(int phase)` (pets below `phase`, catalog pets missing from the list included at level 0 / 0 souls); `event Action<Aion2PetState, bool LeveledUp>? Changed`; `void Save(string path)` / `static Aion2PetCollection Load(string path)` (JSON, per character: `pets-<character>.json` in `AppDataFolder.Path`).
  - `Aion2PacketCombatSource.Pets` (the collection; loaded from the saved file of the known character at start, saved after each change).
- Frame layouts (bytes after the 2-byte opcode): list = `u32` (1012), varint n, n × (`u32` id, `u32` id, `u32` level), varint m, m × (`u32` id, `u32` souls), then sections to ignore; souls = `01`, `u32` id, `u32` gained; level = `00 00`, `u32` id, `u32` level, rest ignored. Short or malformed frames are ignored.

- [ ] **Step 1: Write the failing test** – `RunAion2PetCollectionScenario`, frames fed through `Aion2PacketCombatSource.Ingest` like `RunAion2RegenerationHealScenario`:
  - the real login frame of 2026-10-16 22:49 (capture `capture_2026-10-16_22-49-55`, opcode 0x0090, first 2,645 bytes = header + both sections; extract with the scratch `petdump` harness) → `Pets.Count == 156` (143 levelled + 13 souls-only), `Pets[1117] == (1117, 2, 1)`, `Pets[1034] == (1034, 1, 20)`, `Pets[1017] == (1017, 0, 3)`, three pets at level 3;
  - then the real soul frame `0D90015D04000002000000` → `Pets[1117].Souls == 3`;
  - then the real level frame `149000005D0400000200000003070000001248000000000000` → `Pets[1117] == (1117, 2, 0)` and `Changed` raised with `LeveledUp == true`;
  - then the real frame `0D90010A04000001000000` (pet 1034, +1, capture 22-42-29 at 27 s) → `Pets[1034].Souls == 21`;
  - `Phase == 1` (13 pets have souls but are not collected);
  - direct call (no decoding involved) `ApplySouls(2074, 4)` on a pet in no list → `(2074, 0, 4)`, `Needed == 5`.
- [ ] **Step 2:** selftest – fails.
- [ ] **Step 3:** implement families, decoder cases, collection, save/load (character from `Entities` local name when known).
- [ ] **Step 4:** selftest – True, ALL CHECKS PASSED; replay `capture_2026-10-16_22-42-29` with a scratch harness and check 1117 ends at level 2, 1 soul when started from level 1 at 5 souls.
- [ ] **Step 5:** commit "Pets: collection from the login list, souls and level-up frames".

### Task 3: First Pets window

**Files:**
- Create: `Client/Ui/PetsWindow.xaml`, `Client/Ui/PetsWindow.xaml.cs`
- Modify: `Client/Ui/MainWindow.xaml` (app-menu row "Familiers" next to the fight history row), `Client/Ui/MainWindow.xaml.cs` (`OnPetsClicked`, one window at a time like `_fightHistoryWindow`), `Client/assets/i18n/ui_strings.json` (`Pets.*` en/de/fr)
- Test: scratch smoke harness `smokepets` (real window on screen, collection from the fixture frames)

**Interfaces:**
- Consumes: `Aion2PacketCombatSource.Pets`, `Aion2PetCatalog.Name/Species`.
- Produces: `PetsWindow(Aion2PetCollection pets)` – header "Phase N · x/207 pets at level N · S souls left"; one card per needed pet, closest to done first (fewest souls left, then name): name, species, level, souls bar `souls/needed`; footer credit "Pet data: SkeeveTV (MIT)"; refreshes on `Changed` (dispatcher); empty state per Review Focus (saved list date / log-in hint).

- [ ] **Step 1:** smoke harness `smokepets`: opens the meter window and `PetsWindow` with a collection built from the Task 2 fixtures; prints header text, first three cards (expect Unstable Spider absent at phase 1 if level 2, the 24/25 pets first), renders `apercu_familiers.png`.
- [ ] **Step 2:** implement window, menu row, strings.
- [ ] **Step 3:** run harness, look at the PNG, copy it to `C:\Users\stacy\OneDrive\Bureau\PDF\familiers_apercu.png`; show Stacy and wait for OK.
- [ ] **Step 4:** selftest ALL CHECKS PASSED; commit "Pets: first Pets window"; on Stacy's OK bump version, commit "Release X", tag, `git push fork HEAD:main`, push tag.
