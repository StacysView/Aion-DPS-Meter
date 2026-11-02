from song_base import *

# Second round for the winter (Stacy, 2026-11-02): other instruments - kalimba, ocarina, accordion, banjo, bagpipes, harpsichord, shakuhachi, marimba, theremin-like voice, steel drums, taiko.
# GM programs: 108 kalimba, 79 ocarina, 21 accordion, 105 banjo, 109 bagpipe, 6 harpsichord, 77 shakuhachi, 12 marimba, 85 lead voice, 114 steel drums, 116 taiko, 78 whistle, 74 recorder,
# 75 pan flute, 110 fiddle, 107 koto, 15 dulcimer, 104 sitar, 11 vibraphone, 69 english horn, 70 bassoon, 43 contrabass, 22 harmonica.

# ============ cube (short) ============
# 6 glass bell: a kalimba arpeggio and a vibraphone halo
s = Song(132)
s.program(0, 108, 112, 70); s.program(1, 11, 85, 85)
mel(s, 0, 'E5:0.18 G5:0.18 B5:0.18 E6:0.18 B5:0.18 G5:0.18 E6:1.2', 0, 100)
s.chord(1, ['E6', 'B6'], 1.2, 2.2, 80)
s.save('hiver_cube_6_clochette_de_verre.mid')

# 7 whistling wind: an ocarina swoops over two harp notes
s = Song(90)
s.program(0, 79, 105, 75); s.program(1, 46, 90, 80)
s.note(0, 'B4', 0, 0.6, 85)
s.slide(0, 0, 0.6, -3, 0, 12)
mel(s, 0, 'E5:0.5 B5:1.8', 0.6, 95)
s.slide(0, 1.1, 1.8, 0, 2.0, 20)
mel(s, 1, 'E3+B3:3', 0, 75)
s.save('hiver_cube_7_vent_siffleur.mid')

# 8 snow on the koto: a koto pluck then a shakuhachi breath
s = Song(76)
s.program(0, 107, 115, 70); s.program(1, 77, 95, 80)
mel(s, 0, 'A4:0.25 E5:0.25 A5:0.25 B5:1', 0, 105)
mel(s, 1, 'E5:3', 1.0, 85)
s.wobble(1, 1.0, 3, 0.25, 4.5)
s.save('hiver_cube_8_neige_sur_le_koto.mid')

# 9 frozen clockwork: a harpsichord tick-tock and a marimba rolls down
s = Song(150)
s.program(0, 6, 110, 55); s.program(1, 12, 105, 60)
mel(s, 0, 'A5:0.125 r:0.125 A5:0.125 r:0.125 E6:0.125 r:0.125 E6:0.125', 0, 100)
mel(s, 1, 'A5:0.2 G5:0.2 E5:0.2 D5:0.2 C5:0.2 A4:1.2', 0.9, 100)
s.save('hiver_cube_9_horloge_gelee.mid')

# ============ taquin (teasing) ============
# 6 the sneeze: an accordion wheezes down, a bassoon "aaah-tchoum", a timpani thud
s = Song(96)
s.program(0, 21, 112, 40); s.program(1, 70, 115, 45); s.program(2, 47, 112, 50)
mel(s, 0, 'E4:0.5 G4:0.5 B4:1.8', 0, 105)
s.slide(0, 1.0, 1.8, 0, 1.5, 16)
mel(s, 0, 'r:2.8 E4:0.15 E4:0.15', 0, 105)
mel(s, 1, 'r:3.0 E3:1.2', 0, 118)
s.slide(1, 3.0, 1.2, 0, -4.0, 18)
s.note(2, 'E2', 3.0, 0.5, 125)
s.drum(CRASH, 3.0, 60, 1)
s.save('hiver_taquin_6_atchoum.mid')

# 7 the slip: a whistle climbs, a banjo strum hesitates, a fall of pizzicato and a crash
s = Song(120)
s.program(0, 78, 105, 40); s.program(1, 105, 108, 40); s.program(2, 45, 100, 40); s.program(3, 47, 118, 50)
mel(s, 1, 'C4+E4+G4:0.5 C4+E4+G4:0.5 r:0.5', 0, 95)
mel(s, 0, 'C6:0.4 E6:0.4 G6:0.8', 1.6, 100)
s.slide(0, 2.5, 0.9, 0, -8, 24)
mel(s, 2, 'G5:0.15 F5:0.15 E5:0.15 D5:0.15 C5:0.15 B4:0.15 A4:0.15 G4:0.15', 2.6, 100)
s.drum(CRASH, 4.0, 100, 1.5)
s.note(3, 'C2', 4.0, 1.0, 125)
s.save('hiver_taquin_7_glissade_ratee.mid')

# 8 the shy yeti: bagpipes drone and a lonely tune that dies away, a tiny kalimba plink
s = Song(72)
s.program(0, 109, 112, 55); s.program(1, 108, 90, 70); s.program(2, 43, 100, 50)
mel(s, 0, 'D4:1.2 F4:0.6 E4:0.6 D4:1.2 A3:2.6', 0, 105)
s.note(2, 'D2', 0, 6, 95)
s.slide(0, 4.6, 2.2, 0, -1.8, 18)
mel(s, 1, 'D6:0.3', 7.0, 80)
s.save('hiver_taquin_8_le_yeti_timide.mid')

# 9 numb toes: a harpsichord scampers, a fiddle shivers, a harmonica sighs
s = Song(168)
s.program(0, 6, 108, 50); s.program(1, 110, 100, 50); s.program(2, 22, 105, 40)
mel(s, 0, 'C5:0.25 D5:0.25 E5:0.25 D5:0.25 C5:0.25 D5:0.25 E5:0.25 D5:0.25 C5:0.25 D5:0.25 E5:0.25 G5:0.5', 0, 100)
mel(s, 1, 'A5:0.125 A#5:0.125 A5:0.125 A#5:0.125 A5:0.125 A#5:0.125 A5:0.125 A#5:0.125', 3.5, 90)
mel(s, 2, 'G4:1.5 E4:3', 4.5, 105)
s.slide(2, 6, 3, 0, -1.5, 18)
s.save('hiver_taquin_9_orteils_gelés.mid'.replace('é', 'e'))
