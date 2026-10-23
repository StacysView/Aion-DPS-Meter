# Dummy test module: a finished tool to measure what a skill level is worth

Date: 2026-10-23. Status: design, phase 1 in progress.

## Goal

The player stands at a training dummy, runs a fixed-length test, changes one skill level (for
example Skill X at 20 and Skill Y at 18, then X at 18 and Y at 20) and repeats. The meter must say,
honestly, what each change did to the damage, and where the next point of improvement is.
"Honestly" is the hard part: a skill that hits for 120k then 125k is normal. The module has to tell
noise from a real effect and say "undecided" when it cannot tell.

## Why damage fluctuates (and what the module does about each)

Measured by SkeeveTV on about 170,000 hits (docs/protocol/hit-kinds.md in his repo, MIT):

| Source | Effect on a hit | Handling |
|---|---|---|
| Critical | about x1.85 | Excluded from the "plain hit" mean; crit rate reported with its own interval |
| Double | about x2.0 | Excluded from the plain mean |
| Perfect | about x1.1 | Excluded from the plain mean |
| Parry | about x0.4 | Excluded from the plain mean |
| Damage roll (min..max) | random spread around the mean | Averaged over many hits; spread (SD) shown; used for the confidence interval |
| Rotation (how often each skill is cast) | changes total DPS, not what a level is worth | Per-hit / per-cast quality is judged apart from cast counts |
| Buffs, potions, food | multiply everything | Effects on the player are recorded per run and compared; a difference is a warning |
| Gear, stats | multiply everything | Own stat table (frame 0x4a36) is snapshotted per run; a difference is a warning |
| Different dummy | different defence | Target name/id recorded; comparing different targets is a warning |

A "plain hit" is a damage hit that is not a tick and has none of crit, double, perfect, parry.

## The test run (precise, identical timer)

- Presets: 30 s, 1 min, 2 min, 3 min, 5 min. Only runs of the same planned length are compared.
- Arm: the overlay shows "Waiting for the first hit". The clock starts at the player's first own
  hit, skill cast or effect after Arm, in **capture time** (the packet timestamps), never the UI
  clock. This is the only way two runs are measured the same way.
- The run window is exactly [Start, Start + Planned]. An event with a timestamp past the window
  is not part of the run, even if it arrives late (a grace period only keeps the poll open).
- DPS of a run = damage in the window / Planned (the same divisor for every run of that length),
  never damage / the last hit's time.
- The overlay counts down from the capture clock; "Finished" stays 20 s; Stop ends a run early and
  marks it incomplete (not comparable).
- Everything is the local player's: own hits (summons attributed to the owner, as the meter does),
  own skill uses (accepted and refused), own effects.

## Reading the skills (levels)

- Levels come from the skill entry frames: the login list (0x0051, opcode family Skills), the
  per-skill entries 0x1338 (id, id, total level, base level, bonuses c/a/b), 0x0f38 (variant
  entry) and 0x1038 (variant removed). Total level = base level + bonuses (verified 2635 of 2635
  in Skeeve's docs/protocol/skill-frames.md).
- Open question we cannot settle without a capture: whether opening the skill tab in game
  resends the list. The module therefore never assumes. A "Skills read" line in the setup panel
  shows when the last skill frame arrived and how many skills it covered, with the instruction
  "open the skills tab in game". Every run stores `LevelsReadAt`; when it is older than the last
  level-changing action the player declares ("I changed levels" button) or older than the run by
  more than a set age, the run is flagged "levels not confirmed" and is excluded from
  level conclusions (kept for the DPS history).
- A debug helper records, during the setup, which opcodes arrive when the skill tab is opened, so
  the answer can be found and the heuristic replaced by a fact.

## Analysis

All pure code (no windows, no packets), covered by SelfCheck scenarios.

Per run and per skill (grouped by base id = id / 10000 * 10000, the level and variant are in the
low digits): uses (accepted casts), hits, hits per use, plain hits n, plain mean, plain SD, min,
max, crit rate (hits flagged crit / hits, with a Wilson interval), double / perfect / parry
rates, measured crit multiplier (median crit / median plain), total damage, share, DPS
contribution, uses per minute, refused uses.

Comparison of two configurations A and B (a configuration = a label + a level signature; several
runs per configuration are pooled):

- Per skill, plain mean A vs B: Welch's t-test, 95 % interval of the change in percent. Verdict per
  skill: better / worse / **no measurable difference** (interval contains 0) / not enough data
  (fewer than 10 plain hits on a side). Never report a bare percentage without its interval.
- Crit rate A vs B per skill: if it differs significantly, a level cannot explain it, raise a
  warning (stat or buff changed).
- Total DPS: with one run per side, the standard error is estimated analytically from the hit
  spreads (sqrt of the sum of hit variances over the window / Planned); |delta| below 1.96 times the
  combined error is "within noise". With 3 or more runs per side the empirical run-to-run spread is
  used instead. The result states which of the two it used.
- Cast counts are reported separately ("you cast Skill Y 12 times in A and 9 in B") so a DPS
  change caused by the rotation is not mistaken for a level effect.
- If more than one skill level differs between A and B, the module does not attribute the DPS
  change to a skill: it says "mixed", shows each skill's own plain-mean change (which is still
  valid per skill) and suggests the one-change-at-a-time protocol.

Level curve and improvement points: for each skill, all runs with a confirmed level are collected;
with two or more distinct levels a least-squares slope of plain mean per level is fitted (with its
error). Estimated DPS gain of +1 level = slope x (1 + crit rate x (crit multiplier - 1)) x hits
per minute, using the latest rotation. Skills are ranked by that estimate, each with a confidence
tier (measured / extrapolated from two points / not measurable yet: "test another level").
No ranking is shown from a single level.

## Storage

`dummy-runs.db` (SQLite, in the app data folder, independent of the 20-fight history): one row per
run with the summary columns and a gzip JSON blob of events, uses, effects, stats and levels. Kept:
the newest 200 runs, notes editable, delete per run. Export: Discord-style table to the clipboard
and CSV of the per-skill table.

## UI

- Dummy test window: setup (duration, label, note), checklist (target is a dummy, skills read,
  stats read, effects), Start / Stop, run list with notes, select two configurations, results.
- Overlay: countdown, state, live DPS, hits so far. Follows the existing themes.
- Results: the per-skill table (levels, uses, plain mean with interval, crit rate, DPS share,
  verdict), the total DPS comparison with its noise statement, warnings, the improvement ranking.
- All strings in Client/assets/i18n/ui_strings.json (en and fr at least, by Localization).
- Relationship to the existing "Compare skill levels" in the fight history (DummyLab): that view
  stays for fights already in the history; it shares `SkillLevel` and the dummy name list
  (`DummyTargets`) with this module.

## Credit and reuse

SkeeveAN/Aion-DPS-Meter (MIT, (c) 2026 SkeeveTV) has a timed dummy test (History/DummyTest.cs,
Ui/DummyTestWindow.cs, Ui/DummyTestOverlay.cs, Combat/RotationAnalysis.cs) and the protocol work
for skill uses, refusals, hit kinds and stats. Where code is taken, keep a comment crediting him
as the rest of the codebase does. His module has no skill levels and no statistics; both are new.

## Phases

1. Protocol: hit kinds, own skill uses and refusals, skill entries, own stats, own effects.
   Analysis: the pure statistics and comparison. (Disjoint files, in parallel.)
2. Engine and storage: DummyTest state machine on capture time, controller wired to the meter,
   SQLite store.
3. UI: window, overlay, results, export, strings.
4. Review pass, SelfChecks, a capture to settle the skill-tab question.
