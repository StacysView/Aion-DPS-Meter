"""usage: python prep_monstres_raw.py <gemini folder> <raw folder> : turns Gemini's two Monsters images into the raw folder of skin-pack.

<gemini folder> holds `planche.jpg` (the sheet of small pieces: button, tabs, bars, platforms, type coins, capsule, star, heart) and `cadre.jpg` (the frame with a creature in each
corner). The sheet's pieces are cut out by their position (found as separate blobs), the four creatures are taken out of the frame picture - the blue band and the white inside
behind them are removed by flooding from the border through the band's colours, the creature's dark outline stopping the flood - and put on the magenta background pack.py keys.
The frame itself is not used: the skin draws its own (make_dex_frame.py).
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

MAGENTA = (255, 0, 255)


def blobs(path, grow=6, min_area=600):
    im = Image.open(path).convert('RGB')
    a = np.asarray(im).astype(int)
    fg = ~((a[..., 0] > 200) & (a[..., 1] < 90) & (a[..., 2] > 200))
    fg = ndimage.binary_dilation(ndimage.binary_opening(fg, iterations=1), iterations=grow)
    lab, _ = ndimage.label(fg)
    boxes = []
    for s in ndimage.find_objects(lab):
        y, x = s
        if (x.stop - x.start) * (y.stop - y.start) > min_area:
            boxes.append((x.start + grow, y.start + grow, x.stop - grow, y.stop - grow))
    return im, boxes


# where the frame's blue band lies in Gemini's frame picture (x0, y0, x1, y1), to tell it from a creature's own blue
BANDS = ((76, 86, 137, 884), (76, 86, 1078, 133), (1016, 86, 1078, 884), (76, 830, 1078, 884))


def clean_creature(im, box, band_colours=True):
    """The creature of the frame picture inside `box`, on magenta: the magenta, the white inside and - inside the band's own rectangles only - the band's colours (teal, cyan, grey),
    as far as the border reaches through them, are wiped; only the biggest piece left is kept. (A creature's own blue, like the seal's, lies outside the band or is walled in by its outline.)"""
    x0, y0 = box[0], box[1]
    crop = np.asarray(im.crop(box)).astype(int)
    h, w = crop.shape[:2]
    r, g, b = crop[..., 0], crop[..., 1], crop[..., 2]
    magenta = (r > 200) & (g < 90) & (b > 200)
    white = np.minimum(np.minimum(r, g), b) > 222
    cyan = (b > r + 25) & (g > r + 10) & (b > 90) & (np.abs(b - g) < 70)
    grey = (np.abs(r - g) < 14) & (np.abs(g - b) < 22) & (r > 120)
    inside_band = np.zeros((h, w), bool)
    for bx0, by0, bx1, by1 in BANDS:
        inside_band[max(0, by0 - y0):max(0, by1 - y0), max(0, bx0 - x0):max(0, bx1 - x0)] = True
    bg_like = magenta | white | ((cyan | grey) & inside_band & band_colours)
    lab, _ = ndimage.label(bg_like)
    edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    wipe = np.isin(lab, list(edge))
    keep = ~wipe
    lab2, n = ndimage.label(ndimage.binary_opening(keep, structure=np.ones((2, 2))), structure=np.ones((3, 3)))
    if n:
        sizes = ndimage.sum(keep, lab2, range(1, n + 1))
        keep = lab2 == (1 + int(np.argmax(sizes)))
    # the band's thin leftover lines: open with a 3 x 3 square, then give the body back its edge (two pixels, inside what was kept)
    body = ndimage.binary_opening(keep, structure=np.ones((4, 4)))
    for _ in range(3):
        body = ndimage.binary_dilation(body, structure=np.ones((3, 3))) & keep
    keep = body
    out = crop.copy()
    out[~keep] = MAGENTA
    return Image.fromarray(out.astype(np.uint8))


def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    sheet, boxes = blobs(os.path.join(src, 'planche.jpg'))

    def crop(name, box, pad=6):
        x0, y0, x1, y1 = box
        sheet.crop((max(0, x0 - pad), max(0, y0 - pad), min(sheet.width, x1 + pad), min(sheet.height, y1 + pad))).save(os.path.join(dst, name + '.png'))

    # the blobs of the sheet, by position: the wide button, both tabs (one blob, split below), the bars, the XP bar, the coins, the capsule, the star, the heart
    wide = next(b for b in boxes if b[2] - b[0] > 600 and b[1] < 80)
    tabs = next(b for b in boxes if 500 < b[2] - b[0] < 600 and 200 < b[1] < 300)
    coins = sorted((b for b in boxes if 440 < b[1] < 500 and b[0] > 620), key=lambda b: b[0])
    capsule = next(b for b in boxes if 215 < b[2] - b[0] < 245 and 215 < b[3] - b[1] < 245)
    star = next(b for b in boxes if b[0] > 1000 and 590 < b[1] < 640)
    crop('bouton_degats', wide)
    # the two tabs are one blob: split at the empty column between them
    a = np.asarray(sheet).astype(int)
    mag = (a[..., 0] > 200) & (a[..., 1] < 90) & (a[..., 2] > 200)
    cols = (~mag[tabs[1]:tabs[3], tabs[0]:tabs[2]]).sum(axis=0)
    gap = [i + tabs[0] for i, c in enumerate(cols) if c < 3 and tabs[0] + 40 < i + tabs[0] < tabs[2] - 40]
    cut = gap[len(gap) // 2]
    crop('onglet_ferme', (tabs[0], tabs[1], cut - 2, tabs[3]))
    crop('onglet_ouvert', (cut + 2, tabs[1], tabs[2], tabs[3]))
    # (no XP bar: it stood for nothing, Stacy 2026-11-21)
    crop('boule', capsule)
    red = os.path.join(src, 'planche_capsule_rouge.jpg')  # the capsule of the first sheet, red and white, when Stacy wants that one
    if os.path.exists(red):
        sheet2, boxes2 = blobs(red)
        box = next(b for b in boxes2 if 215 < b[2] - b[0] < 245 and 215 < b[3] - b[1] < 245)
        x0, y0, x1, y1 = box
        sheet2.crop((max(0, x0 - 6), max(0, y0 - 6), min(sheet2.width, x1 + 6), min(sheet2.height, y1 + 6))).save(os.path.join(dst, 'boule.png'))
    crop('etincelle', star)
    for name, box in zip(('badge_electrique', 'badge_eau', 'badge_plante', 'badge_feu'), coins):
        crop(name, box)

    frame = Image.open(os.path.join(src, 'cadre.jpg')).convert('RGB')
    creatures = {
        'creature_electrique': (20, 20, 225, 225),
        'creature_eau': (875, 20, 1135, 225),
        'creature_plante': (25, 675, 250, 885),
        'creature_feu': (870, 670, 1130, 885),
    }
    for name, box in creatures.items():
        clean_creature(frame, box).save(os.path.join(dst, name + '.png'))
    print('raw folder ready:', sorted(os.listdir(dst)))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
