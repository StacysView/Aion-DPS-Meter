# Teasing banner for low DPS at a boss's death

Asked by Stacy (2026-10-10): when a boss dies, the meter of everyone who runs it shows a teasing
line for each group member who finished the fight under a DPS threshold.

## Decisions (Stacy)

- **What**: a teasing text, one random phrase per player, with their DPS. 45 phrases (30 plain, 15
  that name the player), French and English; other UI languages fall back to English.
- **When**: at a boss's death only (its hit points read 0) - no wipe, no trash. Fights shorter than
  20 s between the first and the last hit on the boss are left alone (a few seconds' DPS means
  nothing).
- **Who**: every class, healers included. Default: the whole group (each meter computes every
  member's DPS from the same frames, so everyone sees the same lines); a setting narrows it to the
  local player only.
- **Where**: a banner at the top centre of the screen the overlay is on, over the game, for about
  six seconds, then it fades. Click-through, never takes the focus.
- **Settings**: "Tease below [13000] DPS" (on by default) and "the whole group / only me".

## Rules

- DPS is the overlay's own number: the player's damage over the boss fight (boss and adds) divided
  by the time from the first to the last hit on the boss (`BossFight.Dps`).
- Players considered: those who dealt damage or healed during the fight, kept to the group (the
  local player and the members of the roster; before the roster is read, the players fighting the
  same monsters - the Group scope's own rule) or to the local player alone.
- One banner per death (boss id and the time of its 0 reading). Players sorted from the lowest DPS.
- Phrases are drawn like cards from a deck: none comes back before all were used; two players under
  the threshold at the same death get different phrases. A plain phrase is prefixed with the name
  ("Diva : ...") when the whole group is teased; a phrase that names the player is not.

## Constraints kept

Nothing is sent to the game and nothing in the game is touched: the banner is a window of the
meter on the local screen, like the overlay. Only people who run the meter see it.

## Tests

- `LowDpsTease.Pick`: who is teased (threshold, group vs self, healers, outsiders, fight under 20 s).
- `PhraseDeck`: 45 draws give 45 different phrases, and no phrase twice in a row across decks.
- Every phrase exists in French and English with its placeholders.
- Replay of the 2026-10-10 14:18 capture: at Necromancer Duanka's death the group's lowest players
  are picked.
