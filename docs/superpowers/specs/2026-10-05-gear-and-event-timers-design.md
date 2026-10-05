# Players' equipment in the details window, Rift / Shugo countdowns in the overlay

Date: 2026-10-05. Fork: StacysView/Aion-DPS-Meter. Chosen by the user ("Faille + Shugo, each its
own setting"; "you + inspected players").

## Equipment

- What the game sends, nothing more: the local player's equipment at login and on zone changes
  (already decoded), another player's when the local player opens that player's profile in game
  (opcode 0x5036, taken from SkeeveAN with two fixes, see the decoding commit). The meter never
  asks for anything: no inspection, no equipment.
- The details window gets two tabs, Skills / Equipment. Equipment: slot, item (coloured by
  quality), enchant level, item level, in the game's slot order; beside the name, level, average
  item level, combat power and legion; under the table, when the record was read. Without a
  record: how to get one (open the profile in game; for oneself, log in or change zone).
- Character windows are kept 30 days in aion2-inspected.json, so a restart keeps them. The history
  window's details look them up by name too.

## Rift / Shugo Festival

- By the clock (local time), as aion2.fr gives them: Shugo every hour at :00, open 10 minutes;
  the Rift's portal at 02:00, 05:00 ... 23:00, open 10 minutes.
- One line at the bottom of the overlay: "Faille dans 2h03 · Shugo dans 3:48" - green while open
  ("Faille ouverte · 6:00"), accent colour in the last ten minutes before. Its own one-second
  timer (the rows only refresh on new hits).
- Two settings, on by default: "Afficher la Faille sur l'overlay", "Afficher le Shugo sur l'overlay".

## Checks

- Self-test: the character window fixture (20 items, enchant levels as on screen); the countdowns
  against the site at 20:39:15 (Faille 2:20:45, Shugo 20:45), an open portal, the jump past midnight.
- Renders: the details window on its Equipment tab (with and without a record), the overlay with
  the countdown line, the settings page in French. Smoke test of the real window.
