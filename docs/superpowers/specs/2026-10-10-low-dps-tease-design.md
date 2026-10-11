# Banner at a boss's death: MVP, deaths, low DPS

Asked by Stacy (2026-10-10, reworked 2026-10-11): when a boss dies, the meter of everyone who runs
it shows a banner about the group - who carried it, who died, who stayed far behind.

## Decisions (Stacy)

- **When**: at a boss's death only (its hit points read 0) - no wipe, no trash - after a fight of
  20 s or more (first to last hit on the boss), and **never solo**: at least two members of the
  group (the local player included) must have been in the fight. It once fired alone on Divided
  Aponos in the Nightmare Altar.
- **One line per member at most, the telling one**:
  - died **2 times or more** during the fight: a line about their deaths (a low DPS is no news then);
  - else under **50 % of the average of the two best DPS among the others** (the other one alone in
    a pair): a line about their DPS. The threshold follows the group: on 2026-10-10, 12,778 on
    Watchdog Kwapo, 10,136 on Necromancer Duanka.
  - Healers are judged like everyone, on their DPS too.
- **MVP**: the group's best DPS opens the banner in gold - only when someone is teased; a boss
  where everyone did well shows nothing.
- **Sound**: a sad trombone with the banner, made by the meter itself (a WAV built in memory, no
  file, nothing downloaded).
- **Settings**: two switches only - the banner as a whole, and its sound. No threshold to set.
- **Where**: top centre of the screen the overlay is on, over the game, about six seconds, then it
  fades. Click-through, never takes the focus.
- **Phrases**: 30 DPS phrases + 15 naming the player (Stacy's), 11 for deaths, 6 for the MVP,
  French and English (other languages fall back to English); each family drawn like cards from a
  deck, no phrase back before the others were used. A DPS phrase that does not name the player
  gets the name before it.

## Rules

- DPS is the overlay's own number: the player's damage over the boss fight (the boss's last run,
  split at silences and resets to full, and its adds) divided by the time from the first to the
  last hit on the boss (`BossFight.Dps`).
- The players there: who dealt damage or healed during the fight. The group: the local player
  and the roster's members (before the roster is read, the players fighting the same monsters).
- A death: the player's hit points reaching 0 inside the fight (`Deaths.Find`).
- One banner per death (boss id and the time of its 0 reading).

## Constraints kept

Nothing is sent to the game and nothing in the game is touched: the banner is a window of the
meter on the local screen, like the overlay. Only people who run the meter see it.

## Tests

- `LowDpsTease.Pick`: MVP then the teased lowest first, one line each (deaths over DPS, a single
  death ignored, healers judged on DPS, strangers left out); no banner and no MVP when nobody is
  teased; a pair; a fight under 20 s; a boss killed solo.
- `PhraseDeck`: 45 draws give 45 different phrases, and no phrase twice in a row across decks.
- Every phrase exists in French and English with its placeholders; the sound is a valid 2 s WAV.
- Replays of 2026-10-10 13:14 and 14:18: the banner's DPS is the overlay's, deaths are found
  (three times dead on Kromede's Desire, Black Smoke Murute).
