# Winter and Christmas sounds (2026-11-02)

`Client/assets/sounds/winter/*.wav` - the sounds of the Winter theme (2 November to 1 December): `cube_*` (a hidden cube appearing), `mvp_*`, `taquin_*`. All original music.
`Client/assets/sounds/christmas/` also got the second round (`mvp_8` to `mvp_14`, `taquin_6` to `taquin_10`: sleigh gallop, hosanna, peal of bells, a star, a sulky reindeer...), sorted by the repository's owner.

Same method as `../christmas-sounds`: written as MIDI by these scripts (`python compose_hiver.py <midi folder>`, `compose_hiver2.py`, `compose_noel.py`, `compose_noel2.py`; `song_base.py` holds the MIDI writer), played by recorded
instruments (GeneralUser GS SoundFont through FluidSynth), then trimmed and brought to one loudness by `../mvp-sounds/finish.py`. Only the sorted files are shipped; the scripts also contain the ones that were dropped.
