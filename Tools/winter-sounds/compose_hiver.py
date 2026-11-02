from song_base import *

# ============ HIVER - cube (a hidden cube appears: short, cold, glassy) ============
# 1 frost: a celesta cascade rising, a crystal pad
s = Song(150)
s.program(0, 8, 105, 75); s.program(1, 98, 80, 80); s.program(2, 9, 85, 70)
for i, p in enumerate(['D5', 'F5', 'A5', 'C6', 'D6', 'F6', 'A6', 'D7']):
    s.note(0, p, i * 0.18, 0.5, 90 + i * 2)
s.chord(1, ['D5', 'A5', 'D6'], 1.4, 2.2, 80)
s.note(2, 'D7', 1.5, 1.8, 90)
s.save('hiver_cube_1_givre.mid')

# 2 icicle: a rising swell then three glassy pings
s = Song(110)
s.program(0, 11, 105, 80); s.program(1, 14, 95, 80)
s.drum(119 if False else 49, 0, 40, 0.2)
for i, p in enumerate(['E6', 'B6', 'E7']):
    s.note(0, p, 1.0 + i * 0.4, 1.5, 105 - i * 8)
s.note(1, 'E5', 1.0, 3, 85)
s.save('hiver_cube_2_glacon.mid')

# 3 aurora: a halo pad and a harp glissando over a fifth
s = Song(80)
s.program(0, 94, 95, 90); s.program(1, 46, 95, 80); s.program(2, 53, 70, 85)
s.chord(0, ['E3', 'B3', 'E4', 'G4'], 0, 4.5, 85)
mel(s, 1, 'E4:0.2 G4:0.2 B4:0.2 E5:0.2 G5:0.2 B5:0.2 E6:2', 0.3, 95)
s.chord(2, ['B4', 'E5', 'G5'], 1.5, 3.5, 72)
s.save('hiver_cube_3_aurore.mid')

# 4 distant blizzard: low fifths, a wind, a single far bell
s = Song(70)
s.program(0, 49, 90, 85); s.program(1, 122, 70, 60); s.program(2, 14, 85, 90)
s.chord(0, ['D2', 'A2', 'D3'], 0, 4, 80)
s.note(1, 'C3', 0, 5.5, 70)
s.note(2, 'A5', 2.5, 4, 75)
s.save('hiver_cube_4_tempete_lointaine.mid')

# 5 crystal: a music box falling down a pentatonic scale and a triangle
s = Song(138)
s.program(0, 10, 110, 80); s.program(1, 8, 80, 75)
mel(s, 0, 'E7:0.2 D7:0.2 B6:0.2 A6:0.2 F#6:0.2 E6:1.2', 0, 100)
mel(s, 1, 'E6:0.2 D6:0.2 B5:0.2 A5:0.2 F#5:0.2 E5:1.2', 0.1, 70)
s.drum(81, 1.4, 90, 1.5)
s.save('hiver_cube_5_cristal.mid')

# ============ HIVER - MVP (a cold triumph) ============
# 1 polar triumph: low brass and timpani, strings tremolo, D minor opening to D major
s = Song(108)
s.program(0, 61, 110, 60); s.program(1, 44, 95, 70); s.program(2, 47, 120, 50); s.program(3, 14, 85, 80)
mel(s, 0, 'D3+A3+D4:2 F3+C4+F4:2 G3+D4+G4:2 A3+E4+A4:2 D3+A3+D4+F#4:4', 0, 105)
mel(s, 1, 'D4+A4:2 F4+C5:2 G4+D5:2 A4+E5:2 D4+A4+F#5:4', 0, 90)
mel(s, 0, 'r:8 D5:0.5 F#5:0.5 A5:1 D6:2', 0, 112)
s.note(2, 'D2', 0, 1.5, 118)
s.note(2, 'D2', 8, 4, 125)
s.drum(CRASH, 8, 95, 3)
s.chord(3, ['D5', 'A5', 'D6'], 8, 4, 100)
s.save('hiver_mvp_1_triomphe_polaire.mid')

# 2 northern lights: a choir rising, a harp, a celesta, slowly opening
s = Song(76)
s.program(0, 53, 100, 90); s.program(1, 46, 90, 80); s.program(2, 8, 90, 80); s.program(3, 94, 80, 90); s.program(4, 47, 105, 60)
mel(s, 0, 'E4+B4:2 G4+D5:2 A4+E5:2 B4+F#5:2 E5+G5+B5:5', 0, 95)
mel(s, 3, 'E3+B3+E4:11', 0, 80)
mel(s, 1, 'E3:0.25 G3:0.25 B3:0.25 E4:0.25 G4:0.25 B4:0.25 E5:0.25 G5:0.25 B5:0.25 E6:1.5', 8, 90)
mel(s, 2, 'B5:0.5 E6:0.5 G6:0.5 B6:2', 8.5, 90)
s.note(4, 'E2', 8, 3, 118)
s.save('hiver_mvp_2_aurore_boreale.mid')

# 3 ice fortress: organ and horns march in E minor, bells strike like blocks of ice
s = Song(96)
s.program(0, 19, 105, 70); s.program(1, 60, 110, 60); s.program(2, 14, 100, 80); s.program(3, 47, 118, 50)
mel(s, 0, 'E2+B2:1 E2+B2:1 G2+D3:1 A2+E3:1 B2+F#3:1 B2+F#3:1 E2+B2+E3:2', 0, 100)
mel(s, 1, 'E4:1 G4:1 B4:1 E5:1 D5:1 B4:1 E5:2 r:1 B4:0.5 E5:0.5 G5:1 F#5:1 E5:1 B5:3', 0, 108)
for i, p in enumerate(['E4', 'B4', 'E5']):
    s.note(2, p, 8 + i * 0.5, 2.5, 100)
s.chord(1, ['E3', 'B3', 'E4', 'G4', 'B4'], 9.5, 4, 112)
s.chord(0, ['E2', 'B2', 'E3'], 9.5, 4, 110)
s.note(3, 'E2', 9.5, 3, 125)
s.drum(CRASH, 9.5, 100, 3)
s.save('hiver_mvp_3_forteresse_de_glace.mid')

# 4 dance of the crystals: celesta, glockenspiel, pizzicato and a flute in a quick G major, ending on a sparkling chord
s = Song(160)
s.program(0, 8, 105, 70); s.program(1, 9, 90, 70); s.program(2, 45, 95, 60); s.program(3, 73, 100, 65); s.program(4, 47, 112, 50)
mel(s, 0, 'G5:0.25 B5:0.25 D6:0.25 G6:0.25 F#6:0.25 D6:0.25 B5:0.25 D6:0.25 E6:0.25 G6:0.25 B6:0.25 G6:0.25 D6:0.5 B5:0.5 G5:1', 0, 100)
mel(s, 3, 'r:4 D6:0.5 G6:0.5 B6:1 A6:0.5 F#6:0.5 G6:2.5', 0, 105)
mel(s, 2, 'G3:0.5 D4:0.5 B3:0.5 D4:0.5 C4:0.5 E4:0.5 D4:0.5 A3:0.5 G3:0.5 D4:0.5 B3:0.5 D4:0.5 D3:0.5 A3:0.5 G3:1', 0, 95)
s.chord(1, ['G5', 'B5', 'D6', 'G6', 'B6'], 8, 4, 100)
s.chord(0, ['G6', 'D7'], 8, 4, 95)
s.note(4, 'G2', 8, 2, 120)
s.drum(CRASH, 8, 85, 2)
s.save('hiver_mvp_4_danse_des_cristaux.mid')

# 5 frost riders: strings in a gallop, horns, snare, from A minor to a bright A major
s = Song(144)
s.program(0, 48, 105, 60); s.program(1, 60, 110, 60); s.program(2, 47, 118, 50); s.program(3, 56, 108, 60)
for i in range(16):
    s.note(0, 'A3' if i % 4 else 'A2', i * 0.5, 0.35, 95)
    s.note(0, 'E4' if i % 2 else 'C4', i * 0.5, 0.35, 85)
for i in range(16):
    s.drum(SNARE if i % 4 == 3 else KICK if i % 4 == 0 else HAT, i * 0.5, 80, 0.2)
mel(s, 1, 'A3:1 C4:0.5 E4:0.5 A4:1 G4:0.5 E4:0.5 F4:1 A4:0.5 C5:0.5 B4:1 E4:1 A4:2', 0, 108)
mel(s, 3, 'r:8 A4:0.5 C#5:0.5 E5:1 A5:2', 0, 110)
s.chord(1, ['A3', 'C#4', 'E4', 'A4'], 8, 4, 112)
s.chord(3, ['E5', 'A5', 'C#6'], 8, 4, 108)
s.note(2, 'A1', 8, 3, 125)
s.drum(CRASH, 8, 95, 3)
s.save('hiver_mvp_5_chevauchee_du_givre.mid')

# ============ HIVER - taquin (teasing the one who did little) ============
# 1 chattering teeth: a xylophone and a woodblock shiver, a sad trombone
s = Song(150)
s.program(0, 13, 110, 40); s.program(1, 57, 118, 45)
for i in range(14):
    s.note(0, 'A5' if i % 2 else 'A#5', i * 0.125, 0.1, 85 + (i % 3) * 5)
    s.drum(WOOD_HI, i * 0.125, 60, 0.1)
mel(s, 1, 'r:1.8 F3:1 E3:1 D#3:1 D3:3', 0, 112)
s.slide(1, 5.8, 3, 0, -2.0, 24)
s.save('hiver_taquin_1_dents_qui_claquent.mid')

# 2 frozen music box: it plays, slows down, and freezes on a note; a crackle
s = Song(90)
s.program(0, 10, 115, 70); s.program(1, 9, 80, 70)
b = 0.0
gap = 0.4
for p in ['E6', 'D6', 'C6', 'D6', 'E6', 'E6', 'E6']:
    s.note(0, p, b, gap * 1.4, 100)
    b += gap
    gap *= 1.35
s.note(0, 'D6', b, 0.15, 95)
s.drum(81, b + 0.3, 40, 0.3)
mel(s, 1, 'C7:0.1 E7:0.1 B6:0.1 G7:0.1', b + 0.9, 70)
s.save('hiver_taquin_2_boite_gelee.mid')

# 3 footsteps in the snow: pizzicato steps, a tuba falling, a soft "flump"
s = Song(100)
s.program(0, 45, 105, 40); s.program(1, 58, 120, 50); s.program(2, 47, 100, 50)
mel(s, 0, 'C4:0.5 r:0.25 C4:0.5 r:0.25 D4:0.5 r:0.25 D4:0.5 r:0.25 E4:0.5 r:0.25 E4:0.5', 0, 100)
mel(s, 1, 'G2:0.9 F2:0.9 E2:0.9 D2:2.5', 4.5, 118)
s.slide(1, 6.2, 2.5, 0, -3.0, 24)
s.note(2, 'C2', 8.7, 0.5, 80)
s.save('hiver_taquin_3_pas_dans_la_neige.mid')

# 4 runny nose: a bassoon whines up and down, a clarinet answers with a sniff
s = Song(88)
s.program(0, 70, 115, 50); s.program(1, 71, 100, 50)
mel(s, 0, 'D3:1.5 F3:1.2 D3:1.5 C3:3', 0, 112)
s.slide(0, 0, 1.5, 0, 1.2, 16)
s.slide(0, 4.2, 3.0, 0, -2.5, 24)
mel(s, 1, 'r:7.5 A5:0.15 B5:0.15 A5:0.15', 0, 80)
s.save('hiver_taquin_4_nez_qui_coule.mid')

# 5 melting snowman: steel drums slide down and deflate, a tuba plop
s = Song(112)
s.program(0, 114, 112, 50); s.program(1, 58, 120, 45)
mel(s, 0, 'G5:0.5 E5:0.5 C5:0.5 G4:0.5 E4:1.2', 0, 105)
s.slide(0, 2.0, 2.5, 0, -6.0, 28, 1.3)
s.note(0, 'C4', 2.0, 2.6, 85)
mel(s, 1, 'C3:0.4 r:0.1 C2:1.5', 4.8, 118)
s.slide(1, 4.9, 1.5, 0, -2.0, 18)
s.save('hiver_taquin_5_bonhomme_fondu.mid')
