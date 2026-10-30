# skin-pack

Turns the raw images Gemini draws (flat magenta `#FF00FF` background) into a skin folder for the meter. The format is in [SPEC.md](SPEC.md).

```
python pack.py <raw folder> <skin folder> --id hiver --name "Hiver" --theme Winter [options]
```

Needs Python with numpy, Pillow and scipy. The raw folder holds one image per element (`cadre`, `boule`, `flocons`, `etincelle`, `lueur_dps`, `filet_or`, `ligne_joueur`, `bouton_degats`,
`info`, `onglet_ferme`, `onglet_ouvert`; .png .jpg .webp). Missing ones are skipped; other images are packed under their own name.

| option | meaning |
|---|---|
| `--px-per-pt N` | resolution of the files: pixels per point of the 320-point panel (default 3; the meter draws them at their natural size) |
| `--corner WxH` | the frame's corner cut, in SOURCE pixels from the frame's outer edge inward (default 6 x band wide, 5 x band tall, at least 70) |
| `--erase X0,Y0,X1,Y1` | wipes a rectangle of the frame image (a stray object inside it); may be repeated |
| `--ppp-<element> V` | points per source pixel for one element (default: the frame's, `320 / frameOuterWidthPx`) |
| `--w-<element> PTS` | the same, given as the element's width in points (e.g. `--w-boule 12`) |

What it does: keys the magenta (soft edge, pink spill removed, JPEG fringe eroded), trims, resamples with Lanczos in premultiplied alpha, splits `flocons` into one sprite per
snowflake, cuts `cadre` in 9 (the whole frame is resampled once, then cut, so the pieces meet exactly; the flat colour inside the frame becomes transparent), and writes `skin.json`.

Limits (what Gemini's frame has to respect, see the order sheet):
- the ornaments must lie inside the corner pieces; the straight edges are stretched from the middle third of the straight run, so anything there repeats;
- the band's tone must be the same all along an edge: a brushed-metal gradient shows as a step where the stretched edge meets a corner;
- the interior (magenta or one flat colour) is removed everywhere, so a dark ring drawn between the band and the inner line disappears too.
