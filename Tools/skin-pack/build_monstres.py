"""usage: python build_monstres.py <gemini folder> <skin folder> : the whole Monsters skin from Gemini's two pictures (planche.jpg, cadre.jpg) - prep_monstres_raw.py cuts them,
pack.py keys and sizes the pieces, make_dex_frame.py draws the frame in pixel art and writes skin.json. Needs Python with numpy, Pillow and scipy."""
import os
import shutil
import subprocess
import sys
import tempfile

here = os.path.dirname(os.path.abspath(__file__))


def main(gemini, skin):
    raw = tempfile.mkdtemp(prefix='monstres-raw-')
    packed = tempfile.mkdtemp(prefix='monstres-packed-')
    py = sys.executable
    subprocess.run([py, os.path.join(here, 'prep_monstres_raw.py'), gemini, raw], check=True)
    widths = {'boule': 12, 'etincelle': 10, 'bouton_degats': 52, 'onglet_ferme': 44, 'onglet_ouvert': 44, 'filet_or': 64,
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
    subprocess.run([py, os.path.join(here, 'make_dex_frame.py'), skin], check=True)
    shutil.rmtree(raw, ignore_errors=True)
    shutil.rmtree(packed, ignore_errors=True)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
