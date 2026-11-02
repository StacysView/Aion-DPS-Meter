"""Two tests with the thick-snow Gemini frame (en7k..., 1152 x 928, no bulbs).
usage: python pack_noel_c.py <full|sides> <skin folder (a copy of noel)>
full : the whole new frame, its top and bottom pieces repeated along the edges
sides: the current frame A (already in the folder) with its left and right sides swapped for the thick snow columns of the new frame"""
import json
import sys
import numpy as np
from PIL import Image

NEW = 'C:/Users/stacy/Downloads/Gemini_Generated_Image_en7kihen7kihen7k.jpg'
which, OUT = sys.argv[1], sys.argv[2]


def key(path):
    arr = np.asarray(Image.open(path).convert('RGB')).astype(float)
    r, g, b = arr[..., 0], arr[..., 1], arr[..., 2]
    mag = np.clip(((r + b) / 2 - g - 40) / 110, 0, 1) * ((r > 120) & (b > 120))
    alpha = 1 - mag
    alpha = np.where(alpha < 0.05, 0, alpha)
    fg = np.clip((arr - (1 - alpha)[..., None] * np.array([255, 0, 255.])) / np.maximum(alpha, 1e-3)[..., None], 0, 255)
    spill = np.clip((fg[..., 0] + fg[..., 2]) / 2 - fg[..., 1] - 25, 0, 255) * (alpha < 0.98)
    fg[..., 2] -= spill * 0.8 * (fg[..., 2] > fg[..., 1])
    fg[..., 0] -= spill * 0.4 * (fg[..., 0] > fg[..., 1] + 60)
    return np.clip(fg, 0, 255), alpha


fg, alpha = key(NEW)
full = Image.fromarray(np.dstack([fg, alpha * 255]).astype('uint8'), 'RGBA')
W, H = full.size
sk = json.load(open(f'{OUT}/skin.json', encoding='utf-8'))
if which == 'full':
    tw, th, bh = 240, 205, 200
    cuts = {'tl': (0, 0, tw, th), 't': (tw, 0, W - tw, th), 'tr': (W - tw, 0, W, th), 'bl': (0, H - bh, tw, H), 'b': (tw, H - bh, W - tw, H), 'br': (W - tw, H - bh, W, H),
            'l': (0, 215, tw, 720), 'r': (W - tw, 215, W, 720)}
    pieces = {}
    for k, c in cuts.items():
        full.crop(c).save(f'{OUT}/frame_{k}.png')
        pieces[k] = f'frame_{k}.png'
    ppp = 0.1
    cx0, cx1, cy0, cy1 = 100, 1053, 90, 825
    ov = {'left': cx0, 'top': cy0, 'right': W - cx1, 'bottom': H - cy1}
    sk['frame'] = {'ppp': ppp, 'overhang': {k: round(v * ppp, 2) for k, v in ov.items()}, 'corner': {'w': round(tw * ppp, 2), 'h': round(th * ppp, 2)}, 'pieces': pieces,
                   'band': 1.0, 'tileSides': True, 'tileEdges': True}
else:
    ppp = sk['frame']['ppp']
    wide = 112  # px, in the frame A's scale: thicker than A's 74
    for side in ('l', 'r'):
        src = full.crop((12, 215, 195, 720)) if side == 'l' else full.crop((957, 215, 1140, 720))
        k = wide / src.width
        col = src.resize((wide, round(src.height * k)), Image.LANCZOS)
        piece = Image.new("RGBA", (320, col.height), (0, 0, 0, 0))
        x = 75 - wide // 2 if side == "l" else 249 - wide // 2
        piece.alpha_composite(col, (max(0, x), 0))
        piece.save(f'{OUT}/frame_{side}.png')
    sk['frame']['tileSides'] = True
json.dump(sk, open(f'{OUT}/skin.json', 'w', encoding='utf-8'), indent=2)
print(which, sk['frame']['overhang'], sk['frame']['corner'])
