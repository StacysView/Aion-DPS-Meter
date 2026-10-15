# Sounds of the end-of-boss banner

The banner at a boss's death plays a sound to the people it is about (see `Ui/TeaseSound.cs`):
the fanfare to the MVP, the trombone to the teased.

Put a file here to replace the meter's own sound for everyone - it is installed with every update:

- `Fanfare.mp3` (or `Fanfare.wav`) - the MVP's sound
- `Trombone.mp3` (or `Trombone.wav`) - the teased player's sound

Use music you are allowed to share (royalty-free, or your own): this repository and its installer
are public.

A player can still use a sound of their own on their PC: `fanfare.mp3` or `trombone.mp3` in
`%APPDATA%\Aion DPS Meter (Stacy)\` wins over the files here.

## In use

- `fanfare.mp3` - composed by Xavier Maréchal, a friend of the repository's owner, who gave it for
  the meter (2026-10-11).
- `mvp/` - eight original MVP jingles in as many styles, played by recorded instruments (see
  `Tools/mvp-sounds`). They take turns with the fanfare above, a shuffled round each time, never the
  same twice in a row (2026-10-15).

Every sound plays at the volume of the settings' slider (half by default).

## Halloween

From 20 October to 4 November, the files of `halloween/` take turns before the sounds above (a
player's own file still comes first): `mvp_*` for the MVP, `taquin_*` for the teased. The five there
were chosen by the repository's owner (2026-10-15) and made by `Tools/halloween-sounds/make_halloween.py`,
which synthesizes them: Chopin's funeral march (1839) and Bach's Toccata in D minor (c. 1705) are in
the public domain and played by the script, no recording used; the others are original.
