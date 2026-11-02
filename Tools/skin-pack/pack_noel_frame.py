"""Builds the Christmas frame from one of the two new Gemini frames into a skin folder: usage: python pack_noel_ab.py <a|b> <skin folder to write into (a copy of noel)>"""
import json
import sys
import numpy as np
from PIL import Image

DL = 'C:/Users/stacy/Downloads/Gemini_Generated_Image_'
VARIANTS = {
    # file, corner width / top height / bottom height, side strip rows, panel edge (centre of the bars): left, right, top, bottom
    'a': dict(file=DL + 'z78q83z78q83z78q.jpg', tw=320, th=240, bh=225, strip=(236, 368), edge=(75, 1680, 72, 532), ppp=0.13),
    'b': dict(file=DL + '2k56kh2k56kh2k56.jpg', tw=335, th=235, bh=232, strip=(215, 372), edge=(85, 1712, 62, 528), ppp=0.13),
}
which, OUT = sys.argv[1], sys.argv[2]
v = VARIANTS[which]
arr = np.asarray(Image.open(v['file']).convert('RGB')).astype(float)
r, g, b = arr[..., 0], arr[..., 1], arr[..., 2]
mag = np.clip(((r + b) / 2 - g - 40) / 110, 0, 1) * ((r > 120) & (b > 120))
alpha = 1 - mag
alpha = np.where(alpha < 0.05, 0, alpha)
fg = np.clip((arr - (1 - alpha)[..., None] * np.array([255, 0, 255.])) / np.maximum(alpha, 1e-3)[..., None], 0, 255)
spill = np.clip((fg[..., 0] + fg[..., 2]) / 2 - fg[..., 1] - 25, 0, 255) * (alpha < 0.98)
fg[..., 2] -= spill * 0.8 * (fg[..., 2] > fg[..., 1])
fg[..., 0] -= spill * 0.4 * (fg[..., 0] > fg[..., 1] + 60)
fg = np.clip(fg, 0, 255)
from scipy import ndimage
CH, CW = alpha.shape
tw, th, bh = v['tw'], v['th'], v['bh']
# the bulbs: bright, coloured, outside the corners (their gifts and ribbons are bright too); three groups of them blink at their own pace
mx, mn = fg.max(-1), fg.min(-1)
core = (mx > 232) & ((mx - mn) / np.maximum(mx, 1) > 0.22) & (alpha > 0.5)
for x0, y0_, x1, y1_ in ((0, 0, tw, th), (CW - tw, 0, CW, th), (0, CH - bh, tw, CH), (CW - tw, CH - bh, CW, CH)):
    core[y0_:y1_, x0:x1] = False
core = ndimage.binary_opening(core, iterations=1)
near = ndimage.binary_dilation(core, iterations=14)
lab, count = ndimage.label(near)
# every other bulb along each string: the top and the bottom strings run along x, the sides along y
group = np.zeros_like(alpha, dtype=int) - 1
centres = ndimage.center_of_mass(near, lab, range(1, count + 1))
def region(c):
    y, x = c
    if y < CH * 0.33 and CW * 0.18 < x < CW * 0.82:
        return 'top'
    if y > CH * 0.6 and CW * 0.18 < x < CW * 0.82:
        return 'bottom'
    return 'left' if x < CW / 2 else 'right'
by_region = {}
for i, c in enumerate(centres, 1):
    by_region.setdefault(region(c), []).append((c[0] if region(c) in ('left', 'right') else c[1], i))
for rname, items in by_region.items():
    for k, (_, i) in enumerate(sorted(items)):
        group[lab == i] = k % 2
NG = 2
# the wire: thin, golden
hh = np.zeros_like(mx); d = mx - mn; m_ = d > 1e-6
rc = (mx == fg[..., 0]) & m_
hh[rc] = (((fg[..., 1] - fg[..., 2])[rc] / d[rc]) % 6) / 6
sat = d / np.maximum(mx, 1)
wire = (hh > 0.06) & (hh < 0.17) & (sat > 0.3) & (mx > 80) & (mx < 235) & (alpha > 0.5)
for x0, y0_, x1, y1_ in ((0, 0, tw, th), (CW - tw, 0, CW, th), (0, CH - bh, tw, CH), (CW - tw, CH - bh, CW, CH)):
    wire[y0_:y1_, x0:x1] = False
# the ribbons are red, the bar is snow: nothing else of that hue lies between the corners; only the thin lines stay (the opening takes the blobs out)
wire = wire & ~ndimage.binary_opening(wire, iterations=2)
gone = ndimage.binary_dilation(wire, iterations=2) | ndimage.binary_dilation(near, iterations=2)
alpha = np.where(gone, 0, alpha)
print(count, 'bulbs erased')
full = Image.fromarray(np.dstack([fg, alpha * 255]).astype('uint8'), 'RGBA')
layers = []

cuts = {'tl': (0, 0, tw, th), 't': (tw, 0, CW - tw, th), 'tr': (CW - tw, 0, CW, th),
        'bl': (0, CH - bh, tw, CH), 'b': (tw, CH - bh, CW - tw, CH), 'br': (CW - tw, CH - bh, CW, CH)}
pieces = {}
y0, y1 = v['strip']
sides = {'l': (0, y0, tw, y1), 'r': (CW - tw, y0, CW, y1)}
light_pieces = [{} for _ in layers]
for k, c in {**cuts, **sides}.items():
    full.crop(c).save(f'{OUT}/frame_{k}.png')
    pieces[k] = f'frame_{k}.png'
    for gi, layer in enumerate(layers):
        layer.crop(c).save(f'{OUT}/lit{gi}_{k}.png')
        light_pieces[gi][k] = f'lit{gi}_{k}.png'
cx0, cx1, cy0, cy1 = v['edge']
ppp = v['ppp']
ov = {'left': cx0, 'top': cy0, 'right': CW - cx1, 'bottom': CH - cy1}
sk = json.load(open(f'{OUT}/skin.json', encoding='utf-8'))
sk['frame'] = {'ppp': ppp, 'overhang': {k: round(x * ppp, 2) for k, x in ov.items()}, 'corner': {'w': round(tw * ppp, 2), 'h': round(th * ppp, 2)}, 'pieces': pieces, 'band': 1.0, 'tileSides': True}
sk['frame'].pop('lights', None)
sk['garland'] = {'bulb': 6.0, 'spacing': 13.5, 'period': 1.2, 'layout': 'outside', 'reach': 8}
sk.pop('decos', None)
json.dump(sk, open(f'{OUT}/skin.json', 'w', encoding='utf-8'), indent=2)
print(which, sk['frame']['overhang'], sk['frame']['corner'])
