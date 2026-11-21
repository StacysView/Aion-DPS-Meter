"""usage: python make_creatures.py <skin folder> : draws the four creatures of the Monsters skin in pixel art, in code - an ORIGINAL design for each type (an electric chick with a lightning
crest, a fire bear cub with a flame tuft, a water whale pup with a spout, a plant bunny with leaf ears), in the manner of the small monster sprites of the 1990s portable consoles: a black
outline, three tones of shading with the light from the top left, big shiny eyes. They copy no existing creature. Written as creature_<type>.png (32 x 32 art pixels, enlarged 6 times with
the nearest pixel) and added to skin.json's images (26 x 26 points)."""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

N = 32
SCALE = 6
BLACK = (24, 22, 32)
WHITE = (255, 255, 255)


class Sprite:
    def __init__(self):
        self.rgb = np.zeros((N, N, 3), np.uint8)
        self.mask = np.zeros((N, N), bool)

    def _fill(self, inside, bbox, tones, vertical=None):
        x0, y0, x1, y1 = bbox
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        rx, ry = max(1.0, (x1 - x0) / 2), max(1.0, (y1 - y0) / 2)
        for y in range(N):
            for x in range(N):
                if not inside[y, x]:
                    continue
                if vertical:
                    k = min(len(vertical) - 1, int((y - y0) / max(1, (y1 - y0)) * len(vertical)))
                    color = vertical[k]
                else:
                    light, mid, dark = tones
                    t = -(((x + 0.5 - cx) / rx) * 0.45 + ((y + 0.5 - cy) / ry) * 0.75)
                    color = light if t > 0.3 else dark if t < -0.34 else mid
                self.rgb[y, x] = color
                self.mask[y, x] = True

    def ellipse(self, cx, cy, rx, ry, tones, vertical=None):
        yy, xx = np.mgrid[0:N, 0:N]
        inside = ((xx + 0.5 - cx) / rx) ** 2 + ((yy + 0.5 - cy) / ry) ** 2 <= 1
        self._fill(inside, (cx - rx, cy - ry, cx + rx, cy + ry), tones, vertical)

    def poly(self, points, tones, vertical=None):
        img = Image.new('1', (N, N), 0)
        ImageDraw.Draw(img).polygon(points, fill=1)
        inside = np.asarray(img)
        xs = [p[0] for p in points]
        ys = [p[1] for p in points]
        self._fill(inside, (min(xs), min(ys), max(xs), max(ys)), tones, vertical)

    def px(self, x, y, color, w=1, h=1):
        self.rgb[y:y + h, x:x + w] = color
        self.mask[y:y + h, x:x + w] = True

    def eye(self, x, y, h=3):
        self.px(x, y, BLACK, 2, h)
        self.px(x, y, WHITE)

    def outline(self):
        m = self.mask
        edge = np.zeros_like(m)
        edge[1:, :] |= m[:-1, :]
        edge[:-1, :] |= m[1:, :]
        edge[:, 1:] |= m[:, :-1]
        edge[:, :-1] |= m[:, 1:]
        edge &= ~m
        self.rgb[edge] = BLACK
        self.mask |= edge

    def save(self, path):
        rgba = np.zeros((N, N, 4), np.uint8)
        rgba[..., :3] = self.rgb
        rgba[..., 3] = np.where(self.mask, 255, 0)
        Image.fromarray(rgba, 'RGBA').resize((N * SCALE, N * SCALE), Image.NEAREST).save(path)


def tones(light, mid, dark):
    return (light, mid, dark)


def electric():
    s = Sprite()
    yellow = tones((255, 238, 120), (246, 206, 52), (206, 154, 30))
    cream = tones((255, 250, 222), (244, 234, 196), (222, 208, 164))
    orange = tones((255, 170, 60), (240, 128, 28), (200, 90, 20))
    s.poly([(23, 19), (26, 13), (26, 17), (30, 9), (28, 19), (30, 19), (25, 25), (25, 21)], tones((255, 214, 80), (236, 170, 36), (196, 124, 24)))
    s.ellipse(15, 20, 10, 9, yellow)
    s.ellipse(15, 22.5, 6.2, 5.8, cream)
    s.ellipse(6.5, 21.5, 2.8, 4, tones((236, 190, 50), (214, 160, 32), (176, 120, 22)))
    s.poly([(10, 13), (11, 5), (14, 10), (16, 2), (18, 10), (21, 5), (21, 13)], tones((255, 244, 140), (252, 222, 70), (226, 176, 40)))
    s.poly([(13, 16), (18, 16), (15.5, 19.5)], orange)
    s.ellipse(11, 29, 3.2, 1.6, orange)
    s.ellipse(20, 29, 3.2, 1.6, orange)
    s.eye(10, 13)
    s.eye(19, 13)
    s.outline()
    return s


def fire():
    s = Sprite()
    fur = tones((250, 164, 76), (232, 112, 42), (178, 70, 28))
    cream = tones((255, 238, 200), (244, 216, 168), (222, 188, 140))
    s.ellipse(9, 11, 3.2, 3.2, fur)
    s.ellipse(23, 11, 3.2, 3.2, fur)
    s.ellipse(9, 11, 1.6, 1.6, cream)
    s.ellipse(23, 11, 1.6, 1.6, cream)
    s.ellipse(16, 24, 8.5, 6.5, fur)
    s.ellipse(16, 25.5, 4.8, 4.5, cream)
    s.ellipse(7.5, 24.5, 2.6, 3.6, tones((236, 130, 56), (214, 98, 36), (168, 64, 26)))
    s.ellipse(24.5, 24.5, 2.6, 3.6, tones((236, 130, 56), (214, 98, 36), (168, 64, 26)))
    s.ellipse(12, 29.5, 3.2, 1.8, tones((226, 112, 44), (198, 84, 30), (160, 60, 24)))
    s.ellipse(20, 29.5, 3.2, 1.8, tones((226, 112, 44), (198, 84, 30), (160, 60, 24)))
    s.ellipse(16, 15.5, 9.2, 7.6, fur)
    s.poly([(12, 9), (13, 3), (16, 6), (17, 0), (19, 6), (21, 2), (21, 9)], None, vertical=[(255, 230, 96), (252, 176, 52), (240, 110, 36), (206, 58, 30)])
    s.ellipse(16, 18.5, 4.4, 3.2, cream)
    s.px(15, 17, BLACK, 2, 1)
    s.px(14, 20, BLACK)
    s.px(17, 20, BLACK)
    s.eye(10, 13, 2)
    s.eye(20, 13, 2)
    s.outline()
    return s


def water():
    s = Sprite()
    blue = tones((130, 198, 252), (70, 146, 230), (42, 100, 186))
    white = tones((244, 250, 255), (214, 232, 248), (182, 208, 232))
    drop = tones((196, 232, 255), (156, 210, 252), (120, 180, 240))
    s.poly([(22, 18), (27, 12), (26, 17), (31, 13), (30, 19), (26, 22)], blue)
    s.ellipse(24, 21, 3, 3, blue)
    s.ellipse(12, 9.5, 1.5, 2.2, drop)      # the spout: a few drops over the head
    s.ellipse(15.5, 6, 1.3, 1.8, drop)
    s.ellipse(9, 7, 1.1, 1.5, drop)
    s.ellipse(18, 10.5, 1, 1.4, drop)
    s.ellipse(14, 20.5, 11.2, 8.6, blue)
    s.ellipse(13, 24, 8.2, 4.6, white)
    s.ellipse(8, 23, 3, 2.3, tones((96, 166, 238), (56, 120, 208), (36, 84, 164)))
    s.eye(7, 17)
    s.px(4, 21, BLACK)
    s.px(5, 22, BLACK)
    s.px(6, 22, BLACK)
    s.outline()
    return s


def plant():
    s = Sprite()
    green = tones((148, 218, 104), (92, 180, 74), (58, 130, 54))
    leaf = tones((160, 226, 110), (104, 196, 84), (62, 148, 62))
    cream = tones((252, 246, 218), (234, 226, 186), (208, 200, 156))
    s.poly([(9, 12), (4, 5), (10, 2), (13, 10)], leaf)
    s.poly([(19, 10), (22, 2), (28, 5), (23, 12)], leaf)
    s.ellipse(16, 19.5, 10.4, 9, green)
    s.ellipse(16, 22, 6.6, 5.4, cream)
    s.ellipse(11, 28.5, 3.6, 2, tones((100, 180, 80), (70, 146, 62), (50, 110, 48)))
    s.ellipse(21, 28.5, 3.6, 2, tones((100, 180, 80), (70, 146, 62), (50, 110, 48)))
    s.ellipse(16, 10.5, 2.4, 2.4, tones((255, 176, 196), (244, 128, 160), (206, 92, 128)))
    s.px(15, 10, (255, 232, 110), 2, 1)
    s.px(15, 22, (236, 128, 150), 2, 1)
    s.px(14, 24, BLACK)
    s.px(17, 24, BLACK)
    s.eye(10, 16)
    s.eye(20, 16)
    s.outline()
    return s


def main(folder):
    path = os.path.join(folder, 'skin.json')
    skin = json.load(open(path, encoding='utf-8'))
    for key, make in (('electrique', electric), ('eau', water), ('plante', plant), ('feu', fire)):
        name = f'creature_{key}.png'
        make().save(os.path.join(folder, name))
        skin['images'][f'creature_{key}'] = {'file': name, 'w': 26.0, 'h': 26.0}
    json.dump(skin, open(path, 'w', encoding='utf-8'), indent=2)
    print('creatures drawn')


if __name__ == '__main__':
    main(sys.argv[1])
