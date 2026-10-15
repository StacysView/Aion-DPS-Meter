# MVP jingles and teasing sounds

`Client/assets/sounds/mvp/*.wav` - eight original victory jingles (epic, rock, jazz, RPG, western,
mariachi, gospel, electro), written for the meter on 2026-10-15 and chosen by the repository's owner.
Every melody is original; nothing is quoted.

`compose_tease.py` writes the teasing sounds of `Client/assets/sounds/taquin` and the four Halloween
ones played by recorded instruments (`Client/assets/sounds/halloween/taquin_3..5`, `mvp_7`) the same
way, with pitch bends for the slides; only those chosen by the repository's owner are shipped.

They are written as MIDI by `compose_mvp.py`, played by recorded instruments - the
[GeneralUser GS](https://github.com/mrbumpy409/GeneralUser-GS) SoundFont (v2.0.3, by S. Christian
Collins, free to use for any music creation, see its license) through
[FluidSynth](https://github.com/FluidSynth/fluidsynth) 2.6.1 - then trimmed and brought to one
loudness by `finish.py`. Neither the SoundFont nor FluidSynth is in this repository.

To make them again:

```
python compose_mvp.py midi
fluidsynth -ni -q -g 0.45 -r 44100 -F wav/mvp_epique.wav GeneralUser-GS.sf2 midi/mvp_epique.mid   (one per file)
python finish.py wav out
```
