"""usage: python build_monstres.py <gemini folder> <skin folder> : the whole Monsters skin from Gemini's two pictures (planche.jpg, cadre.jpg) - prep_monstres_raw.py cuts them,
pack.py keys and sizes the pieces, make_dex_frame.py draws the frame in pixel art and writes skin.json. Needs Python with numpy, Pillow and scipy."""
import os
import shutil
import subprocess
import sys
import tempfile

here = os.path.dirname(os.path.abspath(__file__))


def keep_red(src, dst, width_pt, px_per_pt=3):
    """pack.py's keying turns a red picture grey (red is 'half magenta' for it: spill removal). The capsule is red and white: key only what is really magenta (high red AND blue, low green),
    connected to the border, then trim and resample at the skin's resolution."""
    import numpy as np
    from PIL import Image
    from scipy import ndimage
    rgb = np.asarray(Image.open(src).convert('RGB')).astype(int)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    magenta = (r > 170) & (b > 170) & (g < 120)
    lab, _ = ndimage.label(magenta)
    edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bg = np.isin(lab, list(edge))
    bg = ndimage.binary_dilation(bg, iterations=1)  # the JPEG's pink fringe
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    rgba = np.dstack([rgb.astype(np.uint8), alpha])
    ys, xs = np.where(alpha > 0)
    rgba = rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = rgba.shape[:2]
    target_w = int(round(width_pt * px_per_pt))
    target_h = max(1, int(round(h * target_w / w)))
    premult = rgba.astype(float)
    premult[..., :3] *= premult[..., 3:4] / 255
    out = np.dstack([np.asarray(Image.fromarray(np.clip(premult[..., i], 0, 255).astype(np.uint8)).resize((target_w, target_h), Image.LANCZOS)).astype(float) for i in range(4)])
    a = out[..., 3:4]
    out[..., :3] = np.where(a > 0, out[..., :3] * 255 / np.maximum(a, 1), 0)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA').save(dst)


def main(gemini, skin):
    raw = tempfile.mkdtemp(prefix='monstres-raw-')
    packed = tempfile.mkdtemp(prefix='monstres-packed-')
    py = sys.executable
    subprocess.run([py, os.path.join(here, 'prep_monstres_raw.py'), gemini, raw], check=True)
    widths = {'boule': 12, 'etincelle': 10, 'bouton_degats': 52, 'onglet_ferme': 44, 'onglet_ouvert': 44,
              'badge_electrique': 10, 'badge_eau': 10, 'badge_plante': 10, 'badge_feu': 10,
              'creature_electrique': 22, 'creature_eau': 30, 'creature_plante': 26, 'creature_feu': 30}
    options = []
    for name, w in widths.items():
        options += [f'--w-{name}', str(w)]
    subprocess.run([py, os.path.join(here, 'pack.py'), raw, packed, '--id', 'monstres', '--name', 'Monstres', '--theme', 'Monsters', *options], check=True)
    os.makedirs(skin, exist_ok=True)
    for f in os.listdir(skin):
        if f.endswith('.png') or f == 'skin.json':
            os.remove(os.path.join(skin, f))
    for f in os.listdir(packed):
        shutil.copy(os.path.join(packed, f), skin)
    keep_red(os.path.join(raw, 'boule.png'), os.path.join(skin, 'boule.png'), 12.0)
    subprocess.run([py, os.path.join(here, 'make_dex_frame.py'), skin], check=True)
    shutil.rmtree(raw, ignore_errors=True)
    shutil.rmtree(packed, ignore_errors=True)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
