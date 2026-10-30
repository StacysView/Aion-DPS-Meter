# Skins of the meter: the format, and what skin-pack does

A **skin** is the look of one theme, drawn by Gemini as images and shown by the meter as they are (Stacy, 2026-10-30: Gemini does the design, nobody redraws it).
One skin = one folder `Client/assets/skins/<id>/` with a `skin.json` and PNG files with real transparency. Text (names, numbers) is never in an image.

## 1. What Gemini delivers (the raw folder, one image per element, flat magenta #FF00FF background everywhere)

| file (any of .png .jpg .webp) | what it is | notes |
|---|---|---|
| `cadre` | the overlay's frame, empty | wide and flat (2560 x 640), THIN band (~12 px), ornaments outside, straight identical edges between the corners; magenta inside and outside |
| `boule` | the bauble before the fight's name | one object |
| `flocons` | the snowflakes of the DPS bar, side by side | N objects in a row, split by the tool |
| `etincelle` | the sparkle at the corner of the DPS number | one object |
| `lueur_dps` | the soft glow behind the DPS number | one object |
| `filet_or` | the glowing gold line under the total | one object, wide and thin |
| `ligne_joueur` | the local player's row background (burgundy bar) | wide; its left and right ends are rounded; the middle repeats |
| `bouton_degats` | the mode button's background | one object, wide |
| `info` | the small "i" icon before the events line | one object |
| `onglet_ferme`, `onglet_ouvert` | a closed tab and an open tab background | one object each |

Any file may be missing: the meter then keeps what it draws itself for that element. Other `*.png|jpg|webp` files are packed too (trimmed, keyed) under their own name, for later.

## 2. What skin-pack writes (the skin folder)

`python pack.py <raw folder> <skin folder> --id hiver --name "Hiver" --theme Winter [--corner 180x170]`

For every raw image: the magenta is keyed out (soft edge, spill removed), the empty margins are trimmed, a PNG with alpha is written. Names in the skin folder:

- `frame_tl.png frame_t.png frame_tr.png frame_l.png frame_r.png frame_bl.png frame_b.png frame_br.png`: the frame cut in 9 (the centre is dropped). Cut lines: `corner` = `WIDTHxHEIGHT` in source pixels from the frame's OUTER edge inward (default: 6 x the band thickness wide, 5 x tall, min 70 px). The outside margin (ornaments that overhang) belongs to the pieces: the left column is `overhang.left + cornerW` wide, the top row `overhang.top + cornerH` tall, etc. The edge pieces (t, b, l, r) are cut at the frame's middle and are 1 pixel thick in the stretched direction.
- `boule.png`, `flocon_1.png ... flocon_N.png`, `etincelle.png`, `lueur_dps.png`, `filet_or.png`, `ligne_joueur.png`, `bouton_degats.png`, `info.png`, `onglet_ferme.png`, `onglet_ouvert.png`: trimmed, keyed.
- `skin.json`.

## 3. skin.json

All lengths in **points** (the meter's 320-point-wide panel), except `px` which are source pixels. `ppp` = points per source pixel; for the frame it is `320 / frameOuterWidthPx`, for the other images the tool uses the same `ppp` unless `--ppp-<name>` says otherwise (the engine may rescale any image: width and height in the json are only the default size).

```json
{
  "version": 1,
  "id": "hiver",
  "name": "Hiver",
  "theme": "Winter",
  "frame": {
    "ppp": 0.125,
    "overhang": { "left": 14.0, "top": 12.0, "right": 14.0, "bottom": 6.0 },
    "corner": { "w": 22.5, "h": 21.25 },
    "pieces": { "tl": "frame_tl.png", "t": "frame_t.png", "tr": "frame_tr.png", "l": "frame_l.png", "r": "frame_r.png", "bl": "frame_bl.png", "b": "frame_b.png", "br": "frame_br.png" },
    "band": 1.5
  },
  "images": {
    "boule":         { "file": "boule.png",         "w": 12.0,  "h": 14.4 },
    "flocons":       [ { "file": "flocon_1.png", "w": 30.0, "h": 30.0 }, { "file": "flocon_2.png", "w": 24.0, "h": 24.0 } ],
    "etincelle":     { "file": "etincelle.png",     "w": 9.0,   "h": 9.0 },
    "lueur_dps":     { "file": "lueur_dps.png",     "w": 40.0,  "h": 18.0 },
    "filet_or":      { "file": "filet_or.png",      "w": 80.0,  "h": 2.5 },
    "ligne_joueur":  { "file": "ligne_joueur.png",  "w": 300.0, "h": 24.0, "capLeft": 10.0, "capRight": 10.0 },
    "bouton_degats": { "file": "bouton_degats.png", "w": 60.0,  "h": 18.0 },
    "info":          { "file": "info.png",          "w": 11.0,  "h": 11.0 },
    "onglet_ferme":  { "file": "onglet_ferme.png",  "w": 40.0,  "h": 16.0 },
    "onglet_ouvert": { "file": "onglet_ouvert.png", "w": 40.0,  "h": 16.0 }
  }
}
```

Rules for the tool: an element with no raw image is simply absent from `images`; `flocons` is an array (left to right); numbers are rounded to 2 decimals; `frame.band` is the band's thickness in points (measured on the straight top edge); `capLeft` / `capRight` of `ligne_joueur` are the width of the rounded ends in points (the middle is what the meter repeats or stretches) estimated as the row height / 2.

## 4. What the meter does with it (for the record)

`SkinPack` loads the folder, `SkinFrame` builds the 9 slices (a 3 x 3 grid, the centre empty, fixed corner sizes, stretching edges, placed so the frame's outer edge lies on the panel's edge, the overhang outside it), the other images are placed by MainWindow (bauble before the title, info icon before the events, the local player's row, the DPS glow and sparkle, the gold line). When the folder is missing or broken the vector decor is used.
