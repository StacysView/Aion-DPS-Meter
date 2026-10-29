# Christmas sounds

`Client/assets/sounds/christmas/*.wav` - fifteen sounds for the season (1 December to 6 January): `cube_*` (five for a hidden cube appearing), `mvp_*` (five for the MVP),
`taquin_*` (five for the teased). Arranged on 2026-10-29 from carols long in the public domain (Jingle Bells, Silent Night, Deck the Halls, We Wish You a Merry Christmas,
Joy to the World, O Come All Ye Faithful, Shchedryk / Carol of the Bells); the Gloria and the jokes are made up for the meter.

They are written as MIDI by `compose_christmas.py`, played by recorded instruments - the
[GeneralUser GS](https://github.com/mrbumpy409/GeneralUser-GS) SoundFont through [FluidSynth](https://github.com/FluidSynth/fluidsynth) - then trimmed and brought to one
loudness by `../mvp-sounds/finish.py`, as the MVP jingles (see `../mvp-sounds/README.md`). Neither the SoundFont nor FluidSynth is in this repository.

```
python compose_christmas.py midi
fluidsynth -ni -q -g 0.45 -r 44100 -F wav/cube_1_cloches_traineau.wav GeneralUser-GS.sf2 midi/cube_1_cloches_traineau.mid   (one per file)
python ../mvp-sounds/finish.py wav out
```
