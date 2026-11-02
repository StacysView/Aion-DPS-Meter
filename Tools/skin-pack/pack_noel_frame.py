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
objs = sorted(ndimage.center_of_mass(near, lab, range(1, count + 1)), key=lambda c: c[1])
order = {i + 1: k for k, i in enumerate(sorted(range(count), key=lambda i: objs[i][1]))}
group = np.zeros_like(alpha, dtype=int) - 1
for i in range(1, count + 1):
    group[lab == i] = order[i] % 3
soft_all = np.clip(ndimage.gaussian_filter(near.astype(float), 4) * 1.5, 0, 1)
dim_fg = fg * (1 - 0.62 * soft_all)[..., None]
dim_alpha = alpha * (1 - 0.35 * soft_all)
print(count, 'bulbs')
full = Image.fromarray(np.dstack([dim_fg, dim_alpha * 255]).astype('uint8'), 'RGBA')
layers = []
for gidx in range(3):
    m = np.clip(ndimage.gaussian_filter((group == gidx).astype(float), 4) * 1.5, 0, 1)
    layers.append(Image.fromarray(np.dstack([fg, alpha * m * 255]).astype('uint8'), 'RGBA'))

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
sk['frame']['lights'] = [{'pieces': lp, 'period': per} for lp, per in zip(light_pieces, (0.9, 1.3, 1.1))]
sk.pop('decos', None)
json.dump(sk, open(f'{OUT}/skin.json', 'w', encoding='utf-8'), indent=2)
print(which, sk['frame']['overhang'], sk['frame']['corner'])
