"""Draws the ten trainer rank badges of the Monsters theme (stand-ins in pixel art, original; Gemini's drawings replace them file by file: rang_1.png ... rang_10.png).

Usage: python make_ranks.py <out folder, Client/assets/skins/monstres>
Each badge is 30 x 30 (a 15 x 15 grid doubled): a round medal whose metal, centre and decoration grow with the rank; the tenth is a winged crown with gems.
"""
import math
import sys
from PIL import Image

OUT = (22, 22, 30)
METALS = [  # rim, body, light
    ((90, 90, 100), (150, 154, 166), (205, 210, 222)),     # 1 stone
    ((120, 70, 30), (190, 120, 60), (235, 170, 100)),      # 2 bronze
    ((120, 70, 30), (190, 120, 60), (235, 170, 100)),      # 3 bronze
    ((100, 108, 124), (176, 186, 204), (235, 242, 255)),   # 4 silver
    ((100, 108, 124), (176, 186, 204), (235, 242, 255)),   # 5 silver
    ((150, 100, 10), (236, 180, 40), (255, 235, 140)),     # 6 gold
    ((150, 100, 10), (236, 180, 40), (255, 235, 140)),     # 7 gold
    ((30, 90, 150), (70, 160, 235), (190, 235, 255)),      # 8 sapphire
    ((100, 40, 150), (170, 90, 230), (235, 200, 255)),     # 9 amethyst
    ((150, 90, 0), (255, 200, 40), (255, 250, 190)),       # 10 sun gold
]
CENTRE = [None, (255, 255, 255), (255, 240, 200), (255, 255, 255), (230, 80, 80), (255, 255, 255), (60, 140, 255), (255, 255, 255), (255, 230, 120), (255, 255, 255)]


def disc(px, cx, cy, r, colour):
    for y in range(15):
        for x in range(15):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                px[x, y] = colour + (255,)


def star(px, cx, cy, r, colour):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rad = r if i % 2 == 0 else r * 0.45
        pts.append((cx + rad * math.cos(a), cy + rad * math.sin(a)))
    for y in range(15):
        for x in range(15):
            inside = False
            j = len(pts) - 1
            for i in range(len(pts)):
                xi, yi = pts[i]
                xj, yj = pts[j]
                if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-9) + xi:
                    inside = not inside
                j = i
            if inside:
                px[x, y] = colour + (255,)


def badge(rank):
    rim, body, light = METALS[rank - 1]
    img = Image.new('RGBA', (15, 15), (0, 0, 0, 0))
    px = img.load()
    if rank == 10:
        # the wings first, behind
        for side in (-1, 1):
            for k in range(5):
                x = 7 + side * (4 + k // 2)
                for y in range(4 + k, 8 + k // 2):
                    if 0 <= x < 15:
                        px[x, y] = (255, 250, 220, 255) if (x + y) % 2 == 0 else (255, 214, 110, 255)
    disc(px, 7, 7, 6.2 if rank != 10 else 5.2, OUT)
    disc(px, 7, 7, 5.4 if rank != 10 else 4.5, rim)
    disc(px, 7, 7, 4.4 if rank != 10 else 3.6, body)
    for x, y in ((5, 4), (6, 4), (5, 5), (4, 5)):
        px[x, y] = light + (255,)
    if CENTRE[rank - 1] and rank in (2, 4, 6, 8):
        disc(px, 7, 7, 1.6, CENTRE[rank - 1])
    elif CENTRE[rank - 1]:
        star(px, 7, 7, 3.4, CENTRE[rank - 1])
    if rank == 9:
        for x in (4, 7, 10):  # a crown's three points
            px[x, 2] = (255, 230, 120, 255)
            px[x, 3] = (255, 230, 120, 255)
    if rank == 10:
        for x, y, c in ((4, 2, (255, 70, 90)), (7, 1, (90, 220, 255)), (10, 2, (120, 255, 120))):
            px[x, y] = c + (255,)
            px[x, y + 1] = c + (255,)
        for x in range(4, 11):
            px[x, 3] = (255, 220, 80, 255)
    return img.resize((30, 30), Image.NEAREST)


def main(folder):
    for rank in range(1, 11):
        badge(rank).save(f'{folder}/rang_{rank}.png')
    print('wrote rang_1 ... rang_10')


if __name__ == '__main__':
    main(sys.argv[1])
