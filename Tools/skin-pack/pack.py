"""skin-pack: turns the raw images Gemini draws (flat magenta #FF00FF background) into a skin folder for the meter (see SPEC.md).

usage: python pack.py <raw folder> <skin folder> --id ID --name NAME --theme THEME [--corner WxH] [--erase X0,Y0,X1,Y1]
                      [--ppp-<element> VALUE] [--w-<element> POINTS]

For every raw image the magenta is keyed out (soft edge, spill removed), the empty margins are trimmed and a PNG with real transparency is written.
`cadre` is cut in 9 pieces, `flocons` is split into one sprite per snowflake, and skin.json is written in the schema of SPEC.md.
"""
import argparse
import json
import os
import re
import sys
import unicodedata

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

PANEL_POINTS = 320.0
DEFAULT_PPP = PANEL_POINTS / 2560.0  # the frame asked from Gemini is 2560 px wide: used when there is no frame

# The usual size, in points, of each element in the 320-point-wide overlay (its width; the height follows the image's ratio): used when no --w-<element> / --ppp-<element>.
# `ligne_joueur`, the tabs and the button stretch in the meter, these are only their default size.
DEFAULT_WIDTH = {'boule': 12.0, 'info': 11.0, 'etincelle': 10.0, 'lueur_dps': 44.0, 'filet_or': 60.0, 'ligne_joueur': 300.0, 'bouton_degats': 52.0,
                 'onglet_ferme': 44.0, 'onglet_ouvert': 44.0, 'flocons': 28.0}

# raw name -> (element key of the json, file name or pattern)
KNOWN = {
    'cadre': 'frame',
    'boule': 'boule',
    'flocons': 'flocons',
    'etincelle': 'etincelle',
    'lueur_dps': 'lueur_dps',
    'filet_or': 'filet_or',
    'ligne_joueur': 'ligne_joueur',
    'bouton_degats': 'bouton_degats',
    'info': 'info',
    'onglet_ferme': 'onglet_ferme',
    'onglet_ouvert': 'onglet_ouvert',
}
EXTENSIONS = ('.png', '.jpg', '.jpeg', '.webp')


# ---------------------------------------------------------------------------------------------------------------- keying
def load_rgb(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(np.float32) / 255.0


def key_magenta(rgb):
    """RGB (h, w, 3) floats -> RGBA floats: the magenta background becomes transparent with a soft edge, the pink spill of the edges is removed."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    magenta = np.clip(((r + b) / 2 - g) / 0.78, 0, 1)  # 1 = pure #FF00FF
    magenta = np.clip((magenta - 0.08) / 0.72, 0, 1)
    alpha = 1 - magenta
    # JPEG leaves a ring of pinkish pixels around the objects: erode it
    pinkish = ((r + b) / 2 - g) > 0.14
    ring = pinkish & (alpha > 0) & (alpha < 0.97)
    alpha = np.where(ring, alpha * 0.4, alpha)
    alpha = np.where(alpha < 0.04, 0.0, alpha)
    a = np.maximum(alpha, 1e-3)[..., None]
    mag = np.array([1.0, 0.0, 1.0], dtype=np.float32)
    fg = np.clip((rgb - (1 - alpha)[..., None] * mag) / a, 0, 1)
    # what pink is left on semi-transparent pixels is pulled towards the green channel
    spill = np.clip(((fg[..., 0] + fg[..., 2]) / 2 - fg[..., 1]) - 0.05, 0, 1) * (alpha < 0.98)
    fg[..., 0] = fg[..., 0] - spill * 0.8 * np.maximum(fg[..., 0] - fg[..., 1], 0)
    fg[..., 2] = fg[..., 2] - spill * 0.8 * np.maximum(fg[..., 2] - fg[..., 1], 0)
    # gold needles (r > g > b) and blue ones (b > g > r) never have green above both red and blue: that is leftover of the unmixing
    cap = np.maximum(fg[..., 0], fg[..., 2]) + 0.04
    fg[..., 1] = np.where(alpha < 0.95, np.minimum(fg[..., 1], cap), fg[..., 1])
    fg = np.clip(fg, 0, 1)
    fg[alpha <= 0] = 0
    return np.dstack([fg, alpha]).astype(np.float32)


def to_image(rgba):
    return Image.fromarray(np.clip(rgba * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA')


def bbox_of(alpha, threshold=0.02):
    ys, xs = np.where(alpha > threshold)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def trim(rgba, margin=1):
    box = bbox_of(rgba[..., 3])
    if box is None:
        return rgba
    x0, y0, x1, y1 = box
    h, w = rgba.shape[:2]
    x0, y0, x1, y1 = max(0, x0 - margin), max(0, y0 - margin), min(w, x1 + margin), min(h, y1 + margin)
    return rgba[y0:y1, x0:x1]


def resample(rgba, new_w, new_h):
    """Lanczos in premultiplied alpha (no dark or pink fringe); keeps the colours of the opaque pixels."""
    new_w, new_h = max(1, int(new_w)), max(1, int(new_h))
    if (new_w, new_h) == (rgba.shape[1], rgba.shape[0]):
        return rgba
    alpha = rgba[..., 3]
    channels = [rgba[..., c] * alpha for c in range(3)] + [alpha]
    done = []
    for channel in channels:
        image = Image.fromarray(np.ascontiguousarray(channel, dtype=np.float32), 'F')
        done.append(np.asarray(image.resize((new_w, new_h), Image.LANCZOS), dtype=np.float32))
    a = np.clip(done[3], 0, 1)
    safe = np.maximum(a, 1e-4)
    rgb = np.clip(np.dstack([done[0] / safe, done[1] / safe, done[2] / safe]), 0, 1)
    rgb[a < 1e-3] = 0
    return np.dstack([rgb, a]).astype(np.float32)


def scaled(rgba, factor):
    return resample(rgba, round(rgba.shape[1] * factor), round(rgba.shape[0] * factor))


# ---------------------------------------------------------------------------------------------------------------- snowflakes
def split_sprites(rgba, gap=12):
    """One sprite per connected object, left to right; objects closer than `gap` pixels are one sprite."""
    mask = rgba[..., 3] > 0.1
    grown = ndi.binary_dilation(mask, structure=np.ones((3, 3), bool), iterations=max(1, gap // 2))
    labels, count = ndi.label(grown)
    sprites = []
    for index in range(1, count + 1):
        part = labels == index
        if (part & mask).sum() < 30:
            continue
        ys, xs = np.where(part & mask)
        sprites.append((xs.mean(), part))
    sprites.sort(key=lambda s: s[0])
    out = []
    for _, part in sprites:
        piece = rgba.copy()
        piece[..., 3] = np.where(part, piece[..., 3], 0)
        out.append(trim(piece))
    return out


# ---------------------------------------------------------------------------------------------------------------- the frame
def luminance(rgb):
    return 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]


def clear_interior(rgb, rgba, centre):
    """The inside of the frame (magenta already keyed, or a flat colour) becomes transparent."""
    cy, cx = centre
    if rgba[cy, cx, 3] < 0.05:
        return rgba, 'magenta (already transparent)'
    colour = rgb[cy, cx]
    # every pixel of the interior's flat colour goes, connected to the centre or not: the dark ring between the inner line and the band, the shadows between needles
    # (the panel's own background shows there, so the frame is only its gold, its line and its ornaments)
    inner = np.abs(rgb - colour).max(axis=2) < 0.075
    ring = ndi.binary_dilation(inner, iterations=1) & (np.abs(rgb - colour).max(axis=2) < 0.14)
    inner = inner | ring
    rgba = rgba.copy()
    rgba[inner, 3] = 0
    rgba[inner, :3] = 0
    return rgba, 'flat colour (%d, %d, %d)' % tuple((colour * 255).round().astype(int))


def measure_frame(rgb, rgba):
    h, w = rgba.shape[:2]
    alpha = rgba[..., 3]
    cx, cy = w // 2, h // 2
    spread = 30

    def first(line):
        hits = np.where(line > 0.5)[0]
        return int(hits[0]) if len(hits) else -1

    tops = [first(alpha[:, x]) for x in range(cx - spread, cx + spread)]
    bottoms = [h - first(alpha[::-1, x]) for x in range(cx - spread, cx + spread)]
    lefts = [first(alpha[y, :]) for y in range(cy - spread, cy + spread)]
    rights = [w - first(alpha[y, ::-1]) for y in range(cy - spread, cy + spread)]
    top, bottom, left, right = (int(np.median(v)) for v in (tops, bottoms, lefts, rights))
    # the band: the bright (metallic) run from the top edge, the dark gap and the thin inner line come after it
    lum = luminance(rgba[..., :3])
    column = lum[top:top + 200, cx]
    first_lum = float(np.median(column[2:6]))
    band = 0
    while band < len(column) and column[band] > 0.5 * first_lum and rgba[top + band, cx, 3] > 0.5:
        band += 1
    box = bbox_of(alpha, 0.1)
    overhang = {
        'left': max(0, left - box[0]), 'top': max(0, top - box[1]),
        'right': max(0, box[2] - right), 'bottom': max(0, box[3] - bottom),
    }
    return dict(left=left, top=top, right=right, bottom=bottom, band=band, overhang=overhang, box=box, centre=(cy, cx))


def cut_frame(rgba, m, corner, factor):
    """The whole frame region (outer box + overhang) is resampled once, then cut in the scaled space: the pieces meet exactly."""
    left, top, right, bottom = m['left'], m['top'], m['right'], m['bottom']
    ov = m['overhang']
    x0, y0 = left - ov['left'], top - ov['top']
    x1, y1 = right + ov['right'], bottom + ov['bottom']
    region = scaled(rgba[y0:y1, x0:x1], factor)
    cw, ch = corner
    xa_src, xb_src = min(left + cw, (left + right) // 2), max(right - cw, (left + right) // 2)
    ya_src, yb_src = min(top + ch, (top + bottom) // 2), max(bottom - ch, (top + bottom) // 2)

    def sx(v):
        return int(round((v - x0) * factor))

    def sy(v):
        return int(round((v - y0) * factor))

    xa, xb, ya, yb = sx(xa_src), sx(xb_src), sy(ya_src), sy(yb_src)
    mid_x, mid_y = sx((left + right) // 2), sy((top + bottom) // 2)
    h, w = region.shape[:2]
    third_x, third_y = max(1, (xb - xa) // 3), max(1, (yb - ya) // 3)
    cols = slice(xa + third_x, max(xa + third_x + 1, xb - third_x))
    rows = slice(ya + third_y, max(ya + third_y + 1, yb - third_y))

    def across_columns(part):
        return np.median(part[:, cols], axis=1, keepdims=True)

    def across_rows(part):
        return np.median(part[rows, :], axis=0, keepdims=True)

    pieces = {
        'tl': region[0:ya, 0:xa], 'tr': region[0:ya, xb:w], 'bl': region[yb:h, 0:xa], 'br': region[yb:h, xb:w],
        't': across_columns(region[0:ya]), 'b': across_columns(region[yb:h]),
        'l': across_rows(region[:, 0:xa]), 'r': across_rows(region[:, xb:w]),
    }
    marks = dict(left=sx(left), top=sy(top), right=sx(right), bottom=sy(bottom), xa=xa, xb=xb, ya=ya, yb=yb, size=(w, h))
    return pieces, marks


# ---------------------------------------------------------------------------------------------------------------- main
def element_key(path):
    stem = os.path.splitext(os.path.basename(path))[0]
    ascii_stem = unicodedata.normalize('NFKD', stem).encode('ascii', 'ignore').decode().lower()
    ascii_stem = re.sub(r'[^a-z0-9_]+', '_', ascii_stem).strip('_')
    return ascii_stem


def num(value):
    return round(float(value), 2)


def main():
    parser = argparse.ArgumentParser(description='Packs Gemini\'s raw images into a skin folder (see SPEC.md).', allow_abbrev=False)
    parser.add_argument('raw')
    parser.add_argument('skin')
    parser.add_argument('--id', required=True)
    parser.add_argument('--name', required=True)
    parser.add_argument('--theme', required=True)
    parser.add_argument('--corner', help='WxH in source pixels, from the frame\'s outer edge inward')
    parser.add_argument('--px-per-pt', type=float, default=3.0, help='resolution of the files: pixels per point of the 320-point panel (default 3)')
    parser.add_argument('--erase', action='append', default=[], help='X0,Y0,X1,Y1 (source pixels) of the frame image to wipe, e.g. a stray object inside it')
    args, extra = parser.parse_known_args()

    ppp_override, width_override = {}, {}
    index = 0
    while index < len(extra):
        item = extra[index]
        match = re.match(r'--(ppp|w)-([a-z0-9_]+)$', item)
        if not match or index + 1 >= len(extra):
            sys.exit(f'unknown option {item}')
        (ppp_override if match.group(1) == 'ppp' else width_override)[match.group(2)] = float(extra[index + 1])
        index += 2

    os.makedirs(args.skin, exist_ok=True)
    raws = {}
    for name in sorted(os.listdir(args.raw)):
        if name.lower().endswith(EXTENSIONS):
            raws[element_key(name)] = os.path.join(args.raw, name)
    if not raws:
        sys.exit('no image in ' + args.raw)

    report = []
    skin = {'version': 1, 'id': args.id, 'name': args.name, 'theme': args.theme}
    images = {}
    frame_ppp = None

    # ---- the frame first: its ppp is the default of the others
    if 'cadre' in raws:
        rgb = load_rgb(raws['cadre'])
        h, w = rgb.shape[:2]
        for rect in args.erase:
            x0, y0, x1, y1 = (int(v) for v in rect.split(','))
            rgb[y0:y1, x0:x1] = rgb[h // 2, w // 2]
        rgba = key_magenta(rgb)
        rgba, interior = clear_interior(rgb, rgba, (h // 2, w // 2))
        m = measure_frame(rgb, rgba)
        outer_w = m['right'] - m['left']
        outer_h = m['bottom'] - m['top']
        frame_ppp = PANEL_POINTS / outer_w  # points per SOURCE pixel (the default of the other elements)
        ppt = args.px_per_pt
        factor = ppt * frame_ppp
        if args.corner:
            cw, ch = (int(v) for v in args.corner.lower().split('x'))
        else:
            cw, ch = max(70, 6 * m['band']), max(70, 5 * m['band'])
        pieces, cut = cut_frame(rgba, m, (cw, ch), factor)
        files = {}
        for key, piece in pieces.items():
            file = f'frame_{key}.png'
            to_image(piece).save(os.path.join(args.skin, file), optimize=True)
            files[key] = file
        ov = m['overhang']
        ov_pt = {'left': cut['left'] / ppt, 'top': cut['top'] / ppt,
                 'right': (cut['size'][0] - cut['right']) / ppt, 'bottom': (cut['size'][1] - cut['bottom']) / ppt}
        band_px = max(1, round(m['band'] * factor))
        skin['frame'] = {
            'ppp': round(1 / ppt, 5),
            'overhang': {k: num(v) for k, v in ov_pt.items()},
            'corner': {'w': num((cut['xa'] - cut['left']) / ppt), 'h': num((cut['ya'] - cut['top']) / ppt)},
            'pieces': files,
            'band': num(band_px / ppt),
        }
        sizes = ', '.join(f"{k} {p.shape[1]}x{p.shape[0]}" for k, p in pieces.items())
        report.append(f"cadre: {w}x{h} source, interior {interior}")
        report.append(f"  outer box x {m['left']}..{m['right']}  y {m['top']}..{m['bottom']}  ({outer_w}x{outer_h} px), band {m['band']} px, overhang px {ov}")
        report.append(f"  source ppp {frame_ppp:.5f} (= {PANEL_POINTS:g} / {outer_w}), {ppt:g} px per point -> scale {factor:.4f}; corner {cw}x{ch} source px")
        report.append(f"  written: frame.ppp {skin['frame']['ppp']}, overhang {skin['frame']['overhang']}, corner {skin['frame']['corner']}, band {skin['frame']['band']} pt")
        report.append(f"  pieces (px): {sizes}")

    # ---- the other elements
    for key, path in raws.items():
        if key == 'cadre':
            continue
        rgb = load_rgb(path)
        rgba = key_magenta(rgb)
        base_ppp = frame_ppp if frame_ppp else DEFAULT_PPP
        ppt = args.px_per_pt
        out_name = key

        def finish(piece, name):
            """Trimmed piece -> resampled at ppt pixels per point (scale = ppt * ppp of this image)."""
            ppp = ppp_override.get(name, base_ppp)
            if name not in ppp_override and name not in width_override and name in DEFAULT_WIDTH:
                ppp = DEFAULT_WIDTH[name] / piece.shape[1]
            if name in width_override:
                ppp = width_override[name] / piece.shape[1]
            return scaled(piece, ppt * ppp), ppp

        if key == 'flocons':
            entries = []
            for number, sprite in enumerate(split_sprites(rgba), 1):
                out, ppp = finish(sprite, key)
                file = f'flocon_{number}.png'
                to_image(out).save(os.path.join(args.skin, file), optimize=True)
                entries.append({'file': file, 'w': num(out.shape[1] / ppt), 'h': num(out.shape[0] / ppt)})
                report.append(f"flocons: sprite {number} {sprite.shape[1]}x{sprite.shape[0]} px -> {out.shape[1]}x{out.shape[0]} px = {entries[-1]['w']} x {entries[-1]['h']} pt")
            if entries:
                images['flocons'] = entries
            else:
                report.append('flocons: no snowflake found')
            continue
        trimmed = trim(rgba)
        out, ppp = finish(trimmed, key)
        file = out_name + '.png'
        to_image(out).save(os.path.join(args.skin, file), optimize=True)
        entry = {'file': file, 'w': num(out.shape[1] / ppt), 'h': num(out.shape[0] / ppt)}
        if key == 'ligne_joueur':
            entry['capLeft'] = entry['capRight'] = num(entry['h'] / 2)
        images[key] = entry
        label = '' if key in KNOWN else ' (unknown element, packed as is)'
        report.append(f"{key}: {rgb.shape[1]}x{rgb.shape[0]} source -> {trimmed.shape[1]}x{trimmed.shape[0]} trimmed -> {out.shape[1]}x{out.shape[0]} px (ppp {ppp:.5f}) = {entry['w']} x {entry['h']} pt{label}")

    skin['images'] = images
    with open(os.path.join(args.skin, 'skin.json'), 'w', encoding='utf-8', newline='\n') as handle:
        json.dump(skin, handle, indent=2, ensure_ascii=False)
        handle.write('\n')
    print('\n'.join(report))
    print(f"skin written to {args.skin}")


if __name__ == '__main__':
    main()
