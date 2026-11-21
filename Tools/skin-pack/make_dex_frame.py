"""usage: python make_dex_frame.py <skin folder> : draws the pixel-art frame of the Monsters skin (a red handheld "dex" device: a blue lens with three lights in the top-left corner,
a speaker and buttons below, the screen's dark bezel all around) as the 8 pieces of a frame, and writes them with the frame values of skin.json.

It is drawn here, in code, one art pixel at a time (an original design: a generic red field-guide device, no copied artwork). One art pixel is ART_PT points on the 320-point panel and
SCALE image pixels, so the meter's smooth downscaling keeps the edges crisp. The straight edges (t, b, l, r) are flat: the meter stretches or tiles them; every ornament sits in a corner.
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image

SCALE = 6          # image pixels per art pixel
ART_PT = 2.0       # points per art pixel in the 320-point panel
SIDE, TOP, BOTTOM = 6, 13, 12   # band thickness in art pixels: left/right, top, bottom
CW = 28            # width of the corner columns in art pixels
TOP_H, BOT_H = TOP + 3, BOTTOM + 3  # corner heights: the band and three rows of the screen's rounded-off inside corner

BLACK = (22, 22, 30)
RED_L = (246, 112, 122)
RED = (214, 44, 60)
RED_M = (226, 66, 80)
RED_D = (160, 26, 44)
BEZ_G = (150, 156, 174)
BEZ_D = (30, 34, 46)
WHITE = (244, 247, 250)
BLUE_D = (34, 108, 196)
BLUE = (66, 172, 244)
PAD = (58, 62, 78)
PAD_L = (96, 104, 126)
LED_R, LED_Y, LED_G = (244, 80, 64), (250, 212, 48), (80, 204, 104)
LED_DIM = ((108, 34, 40), (116, 98, 36), (38, 80, 50))   # the same three lights, off
LED_X = (16, 20, 24)


def piece(w, h, outer, opening):
    """RGBA art array (h, w, 4): the red body with its outline, highlight and bezel; `outer` the outer edges among 'LTRB', `opening` = (x0, y0, x1, y1) transparent rectangle (end excluded)."""
    a = np.zeros((h, w, 4), np.uint8)
    x0, y0, x1, y1 = opening
    for y in range(h):
        for x in range(w):
            if x0 <= x < x1 and y0 <= y < y1:
                continue
            dx = max(x0 - x, x - (x1 - 1), 0)
            dy = max(y0 - y, y - (y1 - 1), 0)
            k = max(dx, dy)
            dist = {'L': x, 'T': y, 'R': w - 1 - x, 'B': h - 1 - y}
            edge = min((e for e in outer), key=lambda e: dist[e])
            o = dist[edge]
            if o == 0:
                c = BLACK
            elif o == 1:
                c = RED_L if edge in 'LT' else RED_D
            elif k == 1:
                c = BEZ_G
            elif k == 2:
                c = BEZ_D
            elif k == 3:
                c = RED_D if edge in 'LT' else RED_M
            elif o == 2:
                c = RED_M
            else:
                c = RED
            a[y, x] = (*c, 255)
    return a


def disc(a, cx, cy, r, color):
    h, w = a.shape[:2]
    for y in range(h):
        for x in range(w):
            if math.hypot(x + 0.5 - cx, y + 0.5 - cy) <= r:
                a[y, x] = (*color, 255)


def rect(a, x0, y0, x1, y1, color):
    a[y0:y1, x0:x1] = (*color, 255)


def save(a, folder, name):
    big = Image.fromarray(a, 'RGBA').resize((a.shape[1] * SCALE, a.shape[0] * SCALE), Image.NEAREST)
    big.save(os.path.join(folder, name))
    return big.size


def top_left(lit):
    """The top-left corner: the lens, and the three lights - `lit` None = all off, 0, 1 or 2 = that one on (the frame's blinking, see main)."""
    tl = piece(CW, TOP_H, 'LT', (SIDE, TOP, CW, TOP_H))
    disc(tl, 9.5, 5.5, 5.2, BLACK)
    disc(tl, 9.5, 5.5, 4.3, WHITE)
    disc(tl, 9.5, 5.5, 3.4, BLUE_D)
    disc(tl, 9.5, 5.5, 2.5, BLUE)
    for x, y in ((8, 3), (9, 3), (8, 4)):
        rect(tl, x, y, x + 1, y + 1, WHITE)
    bright = (LED_R, LED_Y, LED_G)
    for i, x in enumerate(LED_X):
        on = lit == i
        rect(tl, x - 1, 2, x + 4, 7, BLACK)
        rect(tl, x, 3, x + 3, 6, bright[i] if on else LED_DIM[i])
        if on:
            rect(tl, x, 3, x + 1, 4, WHITE)
    return tl


def main(folder):
    # the corners
    tl = top_left(None)
    tr = piece(CW, TOP_H, 'RT', (0, TOP, CW - SIDE, TOP_H))
    bl = piece(CW, BOT_H, 'LB', (SIDE, 0, CW, 3))
    rect(bl, 11, 5, 14, 14, BLACK)      # the cross: its outline, then its body (rows 5..13 are the red part of the bottom band)
    rect(bl, 8, 8, 17, 11, BLACK)
    rect(bl, 12, 6, 13, 13, PAD)
    rect(bl, 9, 9, 16, 10, PAD)
    rect(bl, 12, 6, 13, 7, PAD_L)
    rect(bl, 9, 9, 10, 10, PAD_L)
    br = piece(CW, BOT_H, 'RB', (0, 0, CW - SIDE, 3))
    for cx, cy in ((11.5, 9.5), (18.5, 7.5)):
        disc(br, cx, cy, 3.2, BLACK)
        disc(br, cx, cy, 2.5, PAD)
        rect(br, int(cx - 1.5), int(cy - 1.5), int(cx - 0.5), int(cy - 0.5), PAD_L)
    for y in (6, 8, 10):                # the speaker
        rect(br, 0, y, 6, y + 1, BEZ_D)
    # the straight edges
    t = piece(1, TOP_H, 'T', (-1000, TOP, 1000, TOP_H))
    b = piece(1, BOT_H, 'B', (-1000, -1000, 1000, 3))
    l = piece(CW, 5, 'L', (SIDE, -1000, 1000, 1000))
    r = piece(CW, 5, 'R', (-1000, -1000, CW - SIDE, 1000))
    sizes = {name: save(a, folder, f'frame_{name}.png') for name, a in (('tl', tl), ('tr', tr), ('bl', bl), ('br', br), ('t', t), ('b', b), ('l', l), ('r', r))}
    for i, key in enumerate(('r', 'y', 'g')):
        save(top_left(i), folder, f'frame_tl_{key}.png')

    path = os.path.join(folder, 'skin.json')
    skin = json.load(open(path, encoding='utf-8'))
    pt_per_px = ART_PT / SCALE
    pieces = {k: f'frame_{k}.png' for k in ('tl', 'tr', 'bl', 'br', 't', 'b', 'l', 'r')}
    # the three lights blink in turn: each layer is the whole frame again with one light on, shown for a third of the cycle
    # (only the top-left corner differs, so the other seven pieces of a layer are a blank picture: no need to draw the whole frame again, and the overlapping edges of four frames showed as dark lines)
    Image.new('RGBA', (SCALE, SCALE), (0, 0, 0, 0)).save(os.path.join(folder, 'frame_blank.png'))
    blank = {k: 'frame_blank.png' for k in pieces}
    lights = [{'pieces': {**blank, 'tl': f'frame_tl_{key}.png'}, 'period': 2.1} for key in ('r', 'y', 'g')]
    skin['frame'] = {
        'ppp': round(pt_per_px, 5),
        'overhang': {'left': 0.0, 'top': 0.0, 'right': 0.0, 'bottom': 0.0},
        'corner': {'w': round(CW * ART_PT, 2), 'h': round(TOP_H * ART_PT, 2)},
        'pieces': pieces,
        'band': round(TOP * ART_PT, 2),
        'tileSides': True,
        'pixelArt': True,
        'lights': lights,
    }
    images = skin['images']
    if 'filet_or' in images:
        images['filet_or']['w'] = 64.0
        images['filet_or']['h'] = 6.0
    # the creatures stand on the console's top edge, the four type coins sit in the bottom band between the d-pad and the speaker
    decos = []
    for name, along in (('creature_electrique', 62), ('creature_eau', 90), ('creature_plante', 124), ('creature_feu', 156)):
        if name in images:
            h = images[name]['h']
            decos.append({'file': images[name]['file'], 'w': images[name]['w'], 'h': h, 'anchor': 'tl', 'along': along, 'down': round(h - 3, 2)})
    for i, name in enumerate(('badge_electrique', 'badge_eau', 'badge_plante', 'badge_feu')):
        if name in images:
            decos.append({'file': images[name]['file'], 'w': images[name]['w'], 'h': images[name]['h'], 'anchor': 'bl', 'along': 131 + i * 16, 'down': -8})
    skin['decos'] = decos
    skin['panel'] = {'padding': [SIDE * ART_PT, TOP * ART_PT, SIDE * ART_PT, BOTTOM * ART_PT]}
    skin['hpBars'] = True
    json.dump(skin, open(path, 'w', encoding='utf-8'), indent=2)
    print('dex frame written:', sizes, 'padding', skin['panel']['padding'], 'decos', len(decos))


if __name__ == '__main__':
    main(sys.argv[1])
