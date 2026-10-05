# Group scope without a roster, and a fight timer that follows the rows

Date: 2026-10-05. Fork: StacysView/Aion-DPS-Meter. Approved by the user ("A et B ok").

## Problem

Solo on a training dummy in town (capture 2026-10-04 22:33), the Group scope listed a stranger
hitting the same dummy, and the overlay's timer ran while the list showed nothing: the fight was
everybody's around, not the shown players'.

- No roster is ever sent to a player without a group, so the Group scope's fallback ("the players
  fighting the same monsters", taken from SkeeveAN 0.10.18) stayed on for the whole session.
- The overlay timer, the span heal/taken rates are divided by, and the automatic reset all read
  every event, strangers' included: someone fighting nearby kept the fight open and the timer
  running. Damage DPS is per player (from their own first hit) and was not affected.

## Decisions

**B. Group without a roster is the local player alone.** No fallback. A meter started inside a
dungeon waits for the roster (up to 4 min 41 s measured, 2026-10-05 00:22); meanwhile "All" shows
the same players there, an instance holding only one's group. Raid keeps its approximation (the
group plus the players fighting the same monsters). Taken mode's notion of a teammate keeps the
fallback: it only decides whose hits on a player are hostile.

**A. The fight follows the shown players.** An event belongs to the fight on screen when a shown
player is its source or its target (`InShownFight`). The overlay timer, the span heal and taken
rates use, and the automatic reset (a new fight after the silence) only look at those events. In
"All" every player is shown, so nothing changes there. The recent-fights history measures a
fight's span on the shown players' damage too.

## Checks

- Self-test: a stranger's hits long before the shown players' fight do not lengthen the history
  entry (span and DPS of the shown player unchanged).
- Replay through the headless window: the 22:33 dummy capture shows only the local player in
  Group, with a timer that is his own fight's; the 00:22 Auldor capture is unchanged in Group
  (the five members, once the roster is read) and in All.
- Smoke test of the real window.
