# World maps

Tiles of the three Elyos-side world maps, shown by the pet tracker.

Cartes : NCSoft (Aion 2), export de Stacy.

Built by `Tools/pets/build_map_tiles.py` from the exported tile images (`<Map>_<col>_<row>.png`): the 2048 px
tiles of World_L_A are shrunk to 1024 px (LANCZOS), the others already are 1024 px; saved as JPEG quality 80.
`<Map>/<col>_<row>.jpg`: col = x, row = y, (0, 0) at the top left, no zero padding.

| Map | Zone | Tiles | Size on disk | Covers |
| --- | --- | --- | --- | --- |
| World_L_A | Verteron | 8 x 8 | about 5.9 MB | 8192 m (1 m per pixel) |
| World_L_B | Eltnen | 8 x 8 | about 6.5 MB | 8192 m (1 m per pixel) |
| World_L_Starter | starter zone | 4 x 4 | about 1.5 MB | 4096 m (1 m per pixel) |

`maps.json` lists them (spawn table in `spawns.json`, grid, tile size, size in metres).

## Calibration

A map covers its level bounds (the `LevelBounds` scale of the map's `.umap`), centred on the world origin, no flip.
With x, y the game's world position in centimetres:

    pixel x = (x / 100 + size / 2) / metres per pixel
    pixel y = (y / 100 + size / 2) / metres per pixel

World_L_A is confirmed on captures (Exiles' Forest, Drana Plantation). World_L_B and World_L_Starter are only
derived from the level bounds and checked against SkeeveTV's pet spawn points, which all fall on land.
