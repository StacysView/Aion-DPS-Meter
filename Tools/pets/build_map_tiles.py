"""Builds the world map tiles shipped with the meter.

Usage: python Tools/pets/build_map_tiles.py <source folder> <output folder>
  e.g. python Tools/pets/build_map_tiles.py C:/dev/aion2-data/maps Client/assets/aion2/maps

Reads <source>/<Map>/Res/<Map>_<col>_<row>.png (col = x, row = y, two digits), shrinks a tile to 1024 px when
it is bigger (LANCZOS), writes <output>/<Map>/<col>_<row>.jpg (no zero padding, quality 80, RGB) and maps.json.
Other files in Res (T_*ID_*, Fog, TransparentMapPath, Masked) are ignored.
"""
import json
import os
import re
import sys

from PIL import Image

TILE = 1024
MAPS = {
    # key: (spawn table, level bounds size in metres)
    "World_L_A": ("World/World_L/World_L_A", 8192),
    "World_L_B": ("World/World_L/World_L_B", 8192),
    "World_L_Starter": ("World/World_L/World_L_Starter", 4096),
}


def main(source: str, output: str) -> None:
    result = {}
    for key, (table, size) in MAPS.items():
        res = os.path.join(source, key, "Res")
        pattern = re.compile(r"^" + re.escape(key) + r"_(\d\d)_(\d\d)\.png$")
        out_dir = os.path.join(output, key)
        os.makedirs(out_dir, exist_ok=True)
        cols = set()
        rows = set()
        total = 0
        for name in sorted(os.listdir(res)):
            match = pattern.match(name)
            if not match:
                continue
            col, row = int(match.group(1)), int(match.group(2))
            image = Image.open(os.path.join(res, name)).convert("RGB")
            if image.width != TILE or image.height != TILE:
                image = image.resize((TILE, TILE), Image.LANCZOS)
            target = os.path.join(out_dir, f"{col}_{row}.jpg")
            image.save(target, "JPEG", quality=80)
            total += os.path.getsize(target)
            cols.add(col)
            rows.add(row)
        grid = max(len(cols), len(rows))
        if len(cols) != len(rows) or len(cols) * len(rows) != sum(1 for f in os.listdir(out_dir)):
            raise SystemExit(f"{key}: tiles do not form a full square grid")
        if grid * TILE != size:
            raise SystemExit(f"{key}: {grid} x {TILE} px does not match {size} m at 1 m per pixel")
        result[key] = {"spawnTable": table, "grid": grid, "tile": TILE, "size": size}
        print(f"{key}: {grid * grid} tiles, {total / 1e6:.2f} MB")
    with open(os.path.join(output, "maps.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"maps": result}, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
