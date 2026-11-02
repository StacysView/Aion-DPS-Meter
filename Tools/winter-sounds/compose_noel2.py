from song_base import *

# Second round for Christmas (Stacy, 2026-11-02): the cube keeps one sound, the first one, faster at the start; the cube's 2, 3 and 5 become MVP jingles.

# ---- cube 1 (the one kept), faster: a short roll of bells, the notes come at once
s = Song(140)
s.program(0, 9, 108, 60); s.program(1, 46, 80, 60)
for i in range(6):
    s.drum(SLEIGH, i * 0.125, 55 + i * 8, 0.2)
for i in range(4):
    s.drum(WOOD_HI if i % 2 == 0 else WOOD_LO, 0.1 + i * 0.25, 65, 0.2)
mel(s, 0, 'D5:0.25 G5:0.25 B5:0.25 D6:0.5 B5:0.25 D6:1.5', 0.55, 102)
mel(s, 1, 'G3+D4+G4:2.5', 0.55, 70)
for i in range(8):
    s.drum(SLEIGH, 0.55 + i * 0.25, 80 - i * 8, 0.2)
s.save('noel_cube_1_traineau_qui_passe.mid')

# ---- MVP 6, from the midnight bell: the strokes grow, a choir blooms, a brass chord crowns it
s = Song(84)
s.program(0, 14, 115, 80); s.program(1, 53, 90, 85); s.program(2, 61, 108, 60); s.program(3, 47, 118, 50); s.program(4, 9, 90, 70)
for i, p in enumerate(['G4', 'G4', 'D5', 'G5']):
    s.note(0, p, i * 1.2, 3.5, 98 + i * 4)
s.chord(1, ['G3', 'D4', 'B4', 'D5'], 3.6, 4.5, 90)
mel(s, 2, 'r:4.8 G3+D4+G4+B4:3', 0, 112)
s.note(3, 'G2', 4.8, 3, 125)
mel(s, 4, 'D6:0.3 G6:0.3 B6:0.3 D7:2', 4.8, 90)
s.drum(CRASH, 4.8, 95, 2)
for i in range(10):
    s.drum(SLEIGH, 4.8 + i * 0.3, 60, 0.2)
s.save('noel_mvp_6_cloche_de_minuit_triomphale.mid')

# ---- MVP 7, from the shepherd's star: a twinkle that turns into a bright orchestral finish
s = Song(132)
s.program(0, 8, 105, 70); s.program(1, 46, 90, 65); s.program(2, 48, 98, 70); s.program(3, 56, 108, 60); s.program(4, 47, 118, 50); s.program(5, 112, 80, 70)
mel(s, 1, 'G3:0.125 B3:0.125 D4:0.125 G4:0.125 B4:0.125 D5:0.125 G5:0.125 B5:0.125 D6:0.125', 0, 85)
mel(s, 0, 'D6:0.25 B5:0.25 G5:0.25 B5:0.25 D6:0.25 G6:1', 1.2, 105)
mel(s, 2, 'G3+D4+B4:2 C4+E4+G4:1 D4+F#4+A4:1 G3+D4+G4+B4:3', 3.0, 95)
mel(s, 3, 'B4:0.5 D5:0.5 G5:1 F#5:0.5 A5:0.5 G5:2', 3.0, 108)
s.note(4, 'G2', 7.0, 2.5, 125)
s.chord(3, ['G4', 'D5', 'G5', 'B5'], 7.0, 3, 112)
s.drum(CRASH, 7.0, 95, 2)
mel(s, 5, 'G6:0.4 D7:2', 7.2, 80)
s.save('noel_mvp_7_etoile_du_berger_triomphale.mid')

# ---- MVP 8, from the ho ho ho: the tuba laughs, then the whole brass section answers in a fanfare
s = Song(116)
s.program(0, 58, 125, 40); s.program(1, 57, 100, 40); s.program(2, 56, 110, 55); s.program(3, 47, 118, 50); s.program(4, 9, 90, 60)
mel(s, 0, 'G2:0.4 G2:0.4 G2:0.4 r:0.2 D3:1.0', 0, 118)
mel(s, 1, 'G3+B3:0.4 G3+B3:0.4 G3+B3:0.4 r:0.2 D4+G4:1.0', 0, 92)
mel(s, 2, 'r:2.6 D5:0.5 G5:0.5 B5:1 A5:0.5 B5:0.5 D6:2.5', 0, 110)
mel(s, 1, 'r:2.6 G3+D4:0.5 G3+D4:0.5 C4+E4:1 D4+F#4:0.5 D4+F#4:0.5 G3+D4+G4+B4:2.5', 0, 98)
s.note(3, 'G2', 7.1, 2.5, 125)
s.drum(CRASH, 7.1, 95, 2)
for i in range(14):
    s.drum(SLEIGH, 5.6 + i * 0.125, 55 + i * 3, 0.2)
mel(s, 4, 'G6:0.4 D7:2', 7.1, 85)
s.save('noel_mvp_8_ho_ho_ho_triomphal.mid')
