from song_base import *
import random

# ============ NOËL - cube (a hidden cube appears: short) ============
# 1 sleigh passing: bells swell, clip-clop, a glockenspiel arpeggio
s = Song(132)
s.program(0, 9, 105, 60); s.program(1, 46, 80, 60)
for i in range(16):
    s.drum(SLEIGH, i * 0.25, 40 + i * 4, 0.2)
for i in range(8):
    s.drum(WOOD_HI if i % 2 == 0 else WOOD_LO, 0.5 + i * 0.5, 70, 0.2)
mel(s, 0, 'D5:0.25 G5:0.25 B5:0.25 D6:0.5 B5:0.25 D6:1.5', 3.0, 100)
mel(s, 1, 'G3+D4+G4:3', 3.0, 70)
for i in range(8):
    s.drum(SLEIGH, 3.0 + i * 0.25, 85 - i * 9, 0.2)
s.save('noel_cube_1_traineau_qui_passe.mid')

# 2 midnight bell: three strokes of a tubular bell, a choir "ooh" blooms
s = Song(70)
s.program(0, 14, 115, 80); s.program(1, 53, 85, 80); s.program(2, 8, 70, 70)
for i, p in enumerate(['G4', 'G4', 'D5']):
    s.note(0, p, i * 1.5, 3.5, 105 - i * 5)
s.chord(1, ['G3', 'D4', 'B4'], 3.0, 4.5, 80)
mel(s, 2, 'B5:0.5 D6:0.5 G6:2', 3.5, 80)
s.save('noel_cube_2_cloche_de_minuit.mid')

# 3 star: a celesta twinkle falling, a harp gliss up, a high bell
s = Song(120)
s.program(0, 8, 105, 70); s.program(1, 46, 90, 65); s.program(2, 112, 80, 70)
mel(s, 1, 'G3:0.125 B3:0.125 D4:0.125 G4:0.125 B4:0.125 D5:0.125 G5:0.125 B5:0.125 D6:0.125', 0, 85)
mel(s, 0, 'D6:0.25 B5:0.25 G5:0.25 B5:0.25 D6:0.25 G6:1.6', 1.2, 105)
mel(s, 2, 'G6:0.5 D7:2', 2.2, 70)
s.save('noel_cube_3_etoile_du_berger.mid')

# 4 We Wish You a Merry Christmas, the first words, on a xylophone and a glockenspiel
s = Song(168)
s.program(0, 13, 105, 55); s.program(1, 9, 90, 60); s.program(2, 33, 80, 40)
mel(s, 0, 'D5:0.5 G5:0.5 G5:0.25 A5:0.25 G5:0.25 F#5:0.25 E5:0.5 E5:0.5 E5:1.5', 0, 105)
mel(s, 1, 'D6:0.5 G6:0.5 G6:0.25 A6:0.25 G6:0.25 F#6:0.25 E6:0.5 E6:0.5 E6:1.5', 0, 80)
mel(s, 2, 'G2:1 D3:1 C3:1 G2:1.5', 0, 80)
for i in range(7):
    s.drum(SLEIGH, i * 0.5, 55, 0.2)
s.save('noel_cube_4_joyeux_noel.mid')

# 5 ho ho ho: a tuba laugh, three notes, a sleigh bell flourish
s = Song(110)
s.program(0, 58, 125, 40); s.program(1, 57, 95, 40); s.program(2, 9, 90, 60)
mel(s, 0, 'G2:0.4 G2:0.4 G2:0.4 r:0.2 D3:1.2', 0, 118)
mel(s, 1, 'G3+B3:0.4 G3+B3:0.4 G3+B3:0.4 r:0.2 D4+G4:1.2', 0, 90)
for i in range(10):
    s.drum(SLEIGH, 2.4 + i * 0.125, 60 + i * 3, 0.2)
mel(s, 2, 'G5:0.5 D6:0.5 G6:1', 2.5, 90)
s.save('noel_cube_5_ho_ho_ho.mid')

# ============ NOËL - MVP (a triumph) ============
# 1 gallop of the sleigh: trumpets on a driving pulse, timpani, bells
s = Song(150)
s.program(0, 56, 112, 55); s.program(1, 61, 100, 55); s.program(2, 47, 120, 45); s.program(3, 9, 85, 60)
mel(s, 0, 'G4:0.5 B4:0.5 D5:0.5 G5:1.5 F#5:0.5 E5:0.5 D5:0.5 B4:1.5 E5:0.5 D5:0.5 C5:0.5 E5:1 D5:0.5 B4:0.5 G4:0.5 D5:0.5 G5:3', 0, 108)
mel(s, 1, 'G3+D4:0.5 r:0.5 G3+D4:0.5 r:0.5 G3+D4:0.5 r:0.5 G3+D4:0.5 r:0.5 C4+E4:0.5 r:0.5 C4+E4:0.5 r:0.5 D4+F#4:0.5 r:0.5 D4+F#4:0.5 r:0.5', 0, 90)
for i in range(32):
    s.drum(SLEIGH, i * 0.25, 60, 0.2)
for i in range(16):
    s.note(2, 'G2', i * 0.5, 0.4, 100)
s.chord(0, ['G3', 'D4', 'G4', 'B4', 'D5'], 10.5, 4, 112)
s.chord(1, ['G2', 'D3', 'G3'], 10.5, 4, 110)
s.drum(CRASH, 10.5, 95, 2)
mel(s, 3, 'D6:0.25 G6:0.25 B6:0.25 D7:2', 10.5, 90)
s.save('noel_mvp_1_galop_du_traineau.mid')

# 2 hosanna: organ and choir climbing, trumpets in the answer
s = Song(112)
s.program(0, 19, 105, 70); s.program(1, 52, 100, 75); s.program(2, 56, 110, 60); s.program(3, 47, 118, 50)
mel(s, 1, 'G4+B4+D5:1.5 A4+C5+E5:0.5 B4+D5+G5:1.5 C5+E5+A5:0.5 D5+G5+B5:4', 0, 100)
mel(s, 0, 'G2+D3:4 C3+G3:2 D3+A3:2 G2+D3+G3:4', 0, 95)
mel(s, 2, 'r:8 D5:0.5 G5:0.5 B5:1 D6:3', 0, 110)
s.note(3, 'G2', 8, 2.5, 120)
s.chord(2, ['G4', 'D5', 'G5', 'B5'], 8, 5, 112)
s.chord(1, ['G4', 'B4', 'D5', 'G5'], 8, 5, 105)
s.drum(CRASH, 8, 95, 2)
s.save('noel_mvp_2_hosanna.mid')

# 3 the bells ring changes: tubular bells and glockenspiel, a peal that fills, then a brass chord
s = Song(180)
s.program(0, 14, 110, 80); s.program(1, 9, 95, 70); s.program(2, 61, 105, 55); s.program(3, 47, 118, 50)
order = ['G4', 'A4', 'B4', 'D5', 'E5']
random.seed(3)
b = 0.0
for rnd in range(5):
    seq = order[:]
    random.shuffle(seq)
    for p in seq:
        s.note(0, p, b, 1.2, 95 + rnd * 3)
        s.note(1, n(p) + 12, b + 0.01, 0.7, 80)
        b += 0.5
s.chord(2, ['G3', 'B3', 'D4', 'G4', 'B4'], b, 3.5, 112)
s.note(3, 'G2', b, 3, 125)
s.chord(0, ['G4', 'D5', 'G5'], b, 3.5, 105)
s.drum(CRASH, b, 90, 2)
for i in range(12):
    s.drum(SLEIGH, b + i * 0.25, 70, 0.2)
s.save('noel_mvp_3_sonnerie_des_cloches.mid')

# 4 Santa's swagger: a tuba-and-trombone march, snare, "ho ho ho" rhythm, a big finish
s = Song(124)
s.program(0, 58, 118, 45); s.program(1, 57, 105, 45); s.program(2, 56, 108, 55); s.program(3, 47, 110, 50)
mel(s, 0, 'C2:1 G2:1 C2:1 G2:1 F2:1 C3:1 G2:1 G2:1 C2:1 G2:1 C2:1 G2:1 F2:1 G2:1 C2:2', 0, 110)
mel(s, 1, 'E3+G3:0.5 r:0.5 E3+G3:0.5 r:0.5 E3+G3:0.5 r:0.5 E3+G3:0.5 r:0.5', 0, 85, scale=1.0)
mel(s, 2, 'r:4 E5:0.5 E5:0.5 E5:1 G5:1 E5:1 C5:0.5 D5:0.5 E5:2 r:1 G5:0.5 A5:0.5 G5:1 E5:1 C6:3', 0, 108)
for i in range(30):
    s.drum(SNARE if i % 2 == 1 else KICK, i * 0.5, 70 + (i % 2) * 20, 0.2)
for i in range(60):
    s.drum(SLEIGH, i * 0.25, 45, 0.2)
s.chord(2, ['C4', 'E4', 'G4', 'C5', 'E5'], 15, 4, 112)
s.chord(0, ['C2', 'G2', 'C3'], 15, 4, 118)
s.drum(CRASH, 15, 100, 3)
s.save('noel_mvp_4_le_pere_noel_defile.mid')

# 5 the star of Bethlehem: strings swell, a trumpet solo, a choir, a harp gliss and a timpani roll
s = Song(84)
s.program(0, 48, 95, 80); s.program(1, 56, 108, 70); s.program(2, 52, 95, 80); s.program(3, 46, 90, 70); s.program(4, 47, 115, 55)
mel(s, 0, 'D3+A3+D4:4 G3+D4+B4:4 A3+E4+C5:2 D3+A3+D4+F#4:2 G3+D4+G4+B4:6', 0, 90)
mel(s, 1, 'r:4 D5:1 G5:1 B5:2 A5:1 F#5:1 G5:5', 0, 108)
mel(s, 3, 'G3:0.25 B3:0.25 D4:0.25 G4:0.25 B4:0.25 D5:0.25 G5:0.25 B5:0.25 D6:0.25 G6:2', 12, 85)
s.chord(2, ['G4', 'B4', 'D5', 'G5'], 12, 6, 100)
for i in range(16):
    s.note(4, 'G2', 10 + i * 0.125, 0.15, 60 + i * 4)
s.note(4, 'G2', 12, 3, 125)
s.drum(CRASH, 12, 90, 3)
s.save('noel_mvp_5_etoile_de_bethleem.mid')

# ============ NOËL - taquin (teasing the one who did little) ============
# 1 the sleigh breaks down: the bells slow, a sad trombone slides, a clunk
s = Song(120)
s.program(0, 57, 118, 45); s.program(1, 9, 80, 60)
gap = 0.25
b = 0.0
for i in range(10):
    s.drum(SLEIGH, b, 80 - i * 4, 0.2)
    b += gap
    gap *= 1.22
s.note(0, 'A3', b, 1.0, 110)
s.note(0, 'G3', b + 1.0, 1.0, 108)
s.note(0, 'F#3', b + 2.0, 1.0, 106)
s.note(0, 'F3', b + 3.0, 3.0, 104)
s.slide(0, b + 3.0, 3.0, 0, -2.5, 30, 1.5)
s.drum(CRASH, b + 6.3, 50, 1)
s.drum(WOOD_LO, b + 6.3, 110, 0.3)
s.save('noel_taquin_1_traineau_en_panne.mid')

# 2 the cracked bell: a tubular bell that will not stay in tune
s = Song(80)
s.program(0, 14, 118, 70); s.program(1, 71, 85, 55)
s.note(0, 'C5', 0, 3.5, 110)
s.slide(0, 0, 3.5, 0, -0.6, 40)
s.note(0, 'C5', 3.0, 3.5, 100)
s.slide(0, 3.0, 3.5, 0.5, -1.5, 40)
mel(s, 1, 'r:3.5 E4:1 D4:1 C4:3', 0, 85)
s.wobble(1, 5.5, 3.0, 0.5, 4.0)
s.save('noel_taquin_2_cloche_felee.mid')

# 3 Father Christmas falls asleep: a snoring tuba and a music box that runs down
s = Song(70)
s.program(0, 58, 125, 45); s.program(1, 10, 110, 70)
mel(s, 0, 'G2:1.5 r:0.3 G2:1.8 r:0.4 G2:1.8', 0, 118)
s.slide(0, 0, 1.5, 0, -0.8, 12)
mel(s, 1, 'G5:0.5 E5:0.5 G5:0.5 C6:0.7 B5:0.8 G5:0.9 E5:1.1 C5:1.6', 0.2, 95)
s.save('noel_taquin_3_le_pere_noel_ronfle.mid')

# 4 the empty present: a triumphant "ta-da", then the air goes out of it
s = Song(132)
s.program(0, 56, 118, 55); s.program(1, 70, 110, 45); s.program(2, 9, 85, 60)
mel(s, 0, 'C5:0.25 C5:0.25 C5:0.25 E5:0.25 G5:1.5', 0, 112)
s.chord(0, ['C5', 'E5', 'G5'], 1.5, 0.1, 100)
mel(s, 2, 'C6:0.25 E6:0.25 G6:0.25 C7:1', 1.5, 85)
mel(s, 1, 'D3:0.75 C3:0.75 B2:0.75 A#2:2.5', 3.0, 112)
s.slide(1, 5.2, 1.5, 0, -3.0, 20)
s.save('noel_taquin_4_cadeau_vide.mid')

# 5 the sulky reindeer: an oboe plods through Jingle Bells and gives up
s = Song(96)
s.program(0, 68, 112, 55); s.program(1, 9, 80, 60)
mel(s, 0, 'E4:1 E4:1 E4:2.2 E4:1.2 E4:1.4 E4:2.8 E4:1.4 G4:1.6 C4:2 D4:1 E4:4', 0, 105, scale=0.8)
s.slide(0, 14, 4, 0, -2.0, 24)
s.drum(SLEIGH, 17.5, 60, 0.3)
mel(s, 1, 'G6:2', 18, 70)
s.save('noel_taquin_5_le_renne_boude.mid')
