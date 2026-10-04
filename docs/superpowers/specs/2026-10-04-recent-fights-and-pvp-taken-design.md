# Recent fights history and players' hits in Taken mode

Date: 2026-10-04. Fork: StacysView/Aion-DPS-Meter (overlay-only build).

## 1. The last 10 fights

### What a fight is

A fight is what the overlay showed between two resets of the meter: the automatic reset (silence
after the last hit), the first hit on a boss, the overlay's reset button, or the meter closing. It
is saved when it ends, if it lasted at least 10 seconds and holds damage.

The history inherited from upstream (`FightRecorder`, one entry per monster, `fights.db`) is
turned off: a 10:37 capture gave 16 pieces of trash and two bosses split in parts, nothing like the
fights the overlay showed. `fights.db` is left on disk untouched.

### Storage

- A separate database, `recent-fights.db` in the data folder, through the existing `FightStore`
  (same schema: summary, participants, events, id→name table).
- After each insert, `Prune` keeps the newest 10.
- Kind `"overlay"`; target name = the boss/target the overlay followed, or the localized
  "All targets" when it showed every target.

### What is saved

`RecentFights.Describe(...)` (new, `History/RecentFights.cs`, no UI types) builds a `FightDetail`
from the events since the last reset:

- Participants = the players the overlay listed: players with damage, heals or hits taken in the
  fight, kept by the scope that was on (`IsInScope`, Group by default).
- Per participant: damage on non-player targets, DPS over the fight's span (first to last
  damage), healing (heals on summons left out, as in Heal mode), damage taken (same rule as Taken
  mode, section 2).
- Deaths are not kept (they need the hit-point readings, which are not stored).

### UI

- A history button (Segoe "History" glyph) in the overlay header, next to the settings button,
  with a localized tooltip.
- It opens a window (the existing `FightHistoryWindow`, slimmed down): top grid = the 10 fights
  (time, target, duration, group damage, players); bottom grid = the selected fight's players
  (name, class, damage, DPS, share, healing, taken). Search box, server column and "load into
  meter" button removed.
- Double-click on a player opens `PlayerDetailsWindow` with that player's hits of the stored fight
  (names from the stored id→name table).
- The overlay keeps running live meanwhile; nothing is swapped.

## 2. Taken mode counts players' hits

`HostileHitsTaken` counted only monsters' hits. It now also counts hits from players who are not
the target themselves and not in the local player's group (`IsGroupMember`, roster names kept
after the group breaks up). The rule moves to a small testable helper (`Combat/TakenHits.cs`).

- Never yourself: self-inflicted hits (a skill's own HP cost, the arena's round reset frames) stay
  out.
- Never a teammate: a group member's mis-attributed hit (a monster's "Attack" credited to a party
  member on Thamon, 2026-10-04 00:31) or a heal decoded as damage stays out.
- Known limit: two members of the same group dueling each other will not see each other's hits.
- Amounts are the hits as sent, like everywhere else. In the 1v1 arena capture (2026-10-04 20:45)
  they run 10-40 % above the hit points lost round by round, most likely shields absorbing part of
  them; there is no in-game recap to settle it, so hits are counted as for bosses.

## Checks

- Self-tests: `RecentFights.Describe` on synthetic events (participants, totals, span, scope);
  prune to 10; `TakenHits` rule (monster, outside player, self, teammate).
- Replays through the headless window, data folder pointed at a scratch folder:
  - 10:37 capture: one history entry per overlay fight, the double boss as one fight;
  - 20:45 arena: Taken shows the opponent's 84,861 on the local player;
  - 00:31 Thamon: no party member listed as an attacker in Taken.
- Smoke test of the real window (overlay, settings, history window opening).
