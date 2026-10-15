"""ORIGINAL teasing sounds for the meter's banner (the teased player's), in many styles, and a new
batch for Halloween (teased and MVP) - written here as MIDI, played by recorded instruments
(GeneralUser GS through FluidSynth). Every melody is made up for the meter; nothing is quoted.
Pitch bends (a range of 12 semitones) make the slides: a falling whistle, a collapsing choir, a
crying harmonica.

usage: python compose_tease.py <midi out folder>"""
import os
import struct
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
TPB = 480
NAMES = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6, 'G': 7, 'G#': 8, 'Ab': 8, 'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}


def n(name):
    pitch, octave = (name[:2], name[2:]) if len(name) > 2 and name[1] in '#b' else (name[:1], name[1:])
    return 12 * (int(octave) + 1) + NAMES[pitch]


def vlq(value):
    out = [value & 0x7F]
    value >>= 7
    while value:
        out.insert(0, (value & 0x7F) | 0x80)
        value >>= 7
    return bytes(out)


class Song:
    def __init__(self, bpm):
        self.bpm = bpm
        self.events = []

    def program(self, ch, prog, volume=100, reverb=50, chorus=0, pan=64, bend_range=12):
        for order, data in ((0, bytes([0xC0 | ch, prog])), (1, bytes([0xB0 | ch, 7, volume])), (1, bytes([0xB0 | ch, 91, reverb])),
                            (1, bytes([0xB0 | ch, 93, chorus])), (1, bytes([0xB0 | ch, 10, pan])),
                            (1, bytes([0xB0 | ch, 101, 0])), (1, bytes([0xB0 | ch, 100, 0])), (1, bytes([0xB0 | ch, 6, bend_range])),
                            (1, bytes([0xB0 | ch, 38, 0])), (1, bytes([0xB0 | ch, 101, 127])), (1, bytes([0xB0 | ch, 100, 127]))):
            self.events.append((0, order, data))

    def note(self, ch, pitch, beat, beats, vel=100):
        p = n(pitch) if isinstance(pitch, str) else pitch
        on, off = int(beat * TPB), int((beat + beats) * TPB)
        self.events.append((on, 3, bytes([0x90 | ch, p, vel])))
        self.events.append((off, 2, bytes([0x80 | ch, p, 0])))

    def chord(self, ch, pitches, beat, beats, vel=95):
        for p in pitches:
            self.note(ch, p, beat, beats, vel)

    def drum(self, key, beat, vel=100, beats=0.25):
        self.note(9, key, beat, beats, vel)

    def bend(self, ch, beat, semitones, bend_range=12):
        """Pitch bend to <semitones> (within the range set by program)."""
        value = max(0, min(16383, int(8192 + semitones / bend_range * 8192)))
        self.events.append((int(beat * TPB), 2, bytes([0xE0 | ch, value & 0x7F, value >> 7])))

    def slide(self, ch, beat, beats, start, end, steps=40, curve=1.0):
        for i in range(steps + 1):
            t = i / steps
            self.bend(ch, beat + beats * t, start + (end - start) * (t ** curve))

    def wobble(self, ch, beat, beats, depth=0.35, rate=6.0):
        """A vibrato made of bends: <rate> cycles per second at the song's tempo."""
        import math
        steps = int(beats * 24)
        seconds_per_beat = 60 / self.bpm
        for i in range(steps + 1):
            b = beat + beats * i / steps
            self.bend(ch, b, depth * math.sin(2 * math.pi * rate * (b - beat) * seconds_per_beat))

    def cc(self, ch, beat, number, value):
        self.events.append((int(beat * TPB), 2, bytes([0xB0 | ch, number, value])))

    def save(self, name):
        tempo = int(60_000_000 / self.bpm)
        evs = sorted(self.events, key=lambda e: (e[0], e[1]))
        body, last = bytearray(), 0
        body += b'\x00\xFF\x51\x03' + tempo.to_bytes(3, 'big')
        for tick, _, data in evs:
            body += vlq(tick - last) + data
            last = tick
        body += vlq(TPB * 2) + b'\xFF\x2F\x00'
        with open(os.path.join(OUT, name), 'wb') as f:
            f.write(b'MThd' + struct.pack('>IHHH', 6, 0, 1, TPB))
            f.write(b'MTrk' + struct.pack('>I', len(body)) + body)
        print(name)


KICK, SNARE, CRASH, RIDE, HAT, CHINA, TOM_LOW, TOM_FLOOR, BRUSH, WOOD_LOW, TRIANGLE = 36, 38, 49, 51, 42, 52, 41, 43, 40, 77, 81

# ================= Teasing, all year =================

# 1 - The sad trombone, on a real trombone: four notes falling, the last one wobbling.
s = Song(96)
s.program(0, 57, 115, 50)
for i, p in enumerate(['F3', 'E3', 'Eb3']):
    s.note(0, p, i * 0.55, 0.5, 105)
s.note(0, 'D3', 1.65, 2.2, 110)
s.wobble(0, 1.85, 2.0, depth=0.45, rate=6.5)
s.bend(0, 3.9, 0)
s.save('taquin_trombone_triste.mid')

# 2 - Tuba: three heavy "bwomps" going down, the last one sagging, a bass drum under each.
s = Song(90)
s.program(0, 58, 120, 40)
for i, p in enumerate(['G2', 'F#2', 'F2']):
    s.note(0, p, i * 0.75, 0.6 if i < 2 else 1.8, 115)
    s.drum(KICK, i * 0.75, 90)
s.slide(0, 1.9, 1.2, 0, -3, curve=1.8)
s.bend(0, 3.4, 0)
s.save('taquin_tuba.mid')

# 3 - The tiniest violin, playing just for you: a slow sigh up high, a harp note to close.
s = Song(72)
s.program(0, 40, 100, 80)
s.program(1, 46, 90, 80)
for b, p, d, v in ((0, 'E6', 1.0, 85), (1.0, 'D6', 0.5, 80), (1.5, 'C6', 0.5, 78), (2.0, 'B5', 1.4, 75)):
    s.note(0, p, b, d, v)
s.wobble(0, 2.1, 1.3, depth=0.25, rate=5.5)
s.bend(0, 3.45, 0)
s.note(1, 'E3', 2.0, 2, 80)
s.save('taquin_petit_violon.mid')

# 4 - A cartoon fall: a whistle sliding all the way down, then the landing.
s = Song(100)
s.program(0, 78, 110, 40)
s.program(1, 47, 110, 50)            # timpani
s.note(0, 'C6', 0, 2.6, 100)
s.slide(0, 0.1, 2.4, 0, -12, steps=60, curve=1.4)
s.bend(0, 2.65, 0)
s.drum(WOOD_LOW, 2.6, 110)
s.note(1, 'C2', 2.6, 1.2, 120)
s.drum(CRASH, 2.65, 70, 1)
s.save('taquin_chute.mid')

# 5 - Muted trumpet, jazz club: a little blues line that droops at the end, brushes behind.
s = Song(110)
s.program(0, 59, 110, 60)
for b, p, d in ((0, 'Bb4', 0.4), (0.5, 'Ab4', 0.4), (1.0, 'F4', 0.4), (1.5, 'E4', 0.35), (2.0, 'Eb4', 1.6)):
    s.note(0, p, b, d, 100)
s.slide(0, 2.5, 1.0, 0, -2, curve=2)
s.bend(0, 3.65, 0)
for i in range(6):
    s.drum(BRUSH, i * 0.5, 45)
s.save('taquin_trompette_bouchee.mid')

# 6 - Drama: two short stabs, then the long fall into the low register, timpani and a gong.
s = Song(84)
s.program(0, 0, 110, 70)              # piano
s.program(1, 48, 110, 80)             # strings
s.program(2, 47, 120, 60)             # timpani
for b, d in ((0, 0.3), (0.5, 0.3)):
    s.chord(0, ['C3', 'Eb3', 'G3', 'C4'], b, d, 110)
    s.chord(1, ['C3', 'G3', 'Eb4'], b, d, 100)
s.chord(0, ['F#2', 'C3', 'Eb3', 'A3'], 1.2, 2.5, 120)
s.chord(1, ['F#2', 'C3', 'Eb3', 'A3'], 1.2, 2.5, 110)
s.note(2, 'C2', 1.2, 1.5, 125)
s.drum(CHINA, 1.2, 100, 2)
s.save('taquin_dramatique.mid')

# 7 - Accordion musette, Paris in the rain: a sad little waltz that stops in mid-air.
s = Song(132)
s.program(0, 21, 105, 60)
s.program(1, 21, 85, 50, pan=40)
for b, p, d in ((0, 'A4', 1), (1, 'G#4', 0.5), (1.5, 'A4', 0.5), (2, 'C5', 1), (3, 'B4', 0.5), (3.5, 'A4', 0.5), (4, 'F4', 1), (5, 'E4', 2)):
    s.note(0, p, b, d * 0.95, 95)
for bar, (bass, chord) in enumerate((('A2', ['C4', 'E4']), ('D3', ['D4', 'F4']), ('E2', ['D4', 'G#4']))):
    s.note(1, bass, bar * 2, 0.9, 85)
    s.chord(1, chord, bar * 2 + 1, 0.4, 70)
    s.chord(1, chord, bar * 2 + 1.5, 0.4, 65)
s.save('taquin_musette.mid')

# 8 - Blues harmonica that cries: a bent wail, a fall, one sad guitar chord.
s = Song(80)
s.program(0, 22, 110, 50)
s.program(1, 25, 90, 60)
s.note(0, 'G4', 0, 1.0, 105)
s.slide(0, 0, 0.4, -1, 0, steps=12)
s.note(0, 'Bb4', 1.0, 0.5, 100)
s.note(0, 'G4', 1.5, 1.6, 100)
s.slide(0, 1.9, 1.1, 0, -2.5, curve=1.5)
s.bend(0, 3.15, 0)
for k, p in enumerate(['E2', 'B2', 'D3', 'G#3', 'B3']):
    s.note(1, p, 1.5 + k * 0.04, 2, 80)
s.save('taquin_harmonica.mid')

# 9 - A choir that collapses: "aaaaah" falling a fourth, a low drum.
s = Song(90)
s.program(0, 52, 115, 90)
s.chord(0, ['A4', 'C5', 'E5'], 0, 3.0, 105)
s.slide(0, 0.6, 2.2, 0, -5, steps=50, curve=1.6)
s.bend(0, 3.05, 0)
s.drum(TOM_FLOOR, 2.8, 100)
s.save('taquin_choeur_qui_s_effondre.mid')

# ================= Halloween =================

# H1 (teased) - A cursed music box: a sweet lullaby, in minor, slowing and going out of tune.
s = Song(100)
s.program(0, 10, 100, 80)
melody = [('E5', 0, 0.5), ('A5', 0.5, 0.5), ('G5', 1, 0.5), ('E5', 1.5, 0.5), ('F5', 2, 0.75), ('D5', 2.75, 0.75), ('B4', 3.5, 1.2)]
for p, b, d in melody:
    s.note(0, p, b * 1.0 + max(0, b - 2) * 0.25, d, 85)
s.slide(0, 2.2, 2.6, 0, -1.3, curve=1.3)
s.bend(0, 4.9, 0)
s.save('halloween_taquin_boite_a_musique.mid')

# H2 (teased) - The organ of doom: chords sinking chromatically, a gong, timpani.
s = Song(70)
s.program(0, 19, 110, 100)
s.program(1, 47, 120, 70)
for i, root in enumerate(['A3', 'G#3', 'G3', 'F#3']):
    r = n(root)
    s.chord(0, [r - 12, r, r + 3, r + 7], i * 0.75, 0.7 if i < 3 else 2.2, 100 + i * 5)
s.note(1, 'F#2', 2.25, 1.5, 125)
s.drum(CHINA, 2.25, 95, 2)
s.save('halloween_taquin_orgue_du_destin.mid')

# H3 (teased) - Ghost choir: "oooh" in a tritone, sliding down into the dark.
s = Song(80)
s.program(0, 53, 115, 100)
s.program(1, 49, 90, 100)             # slow strings under
s.chord(0, ['D4', 'G#4', 'D5'], 0, 3.2, 100)
s.chord(1, ['D3', 'G#3'], 0, 3.2, 70)
s.slide(0, 0.5, 2.6, 0, -4, steps=50, curve=1.5)
s.bend(0, 3.25, 0)
s.save('halloween_taquin_choeur_fantome.mid')

# H4 (MVP) - Skeletons dancing: xylophone and pizzicato in D minor, ending in D major.
s = Song(150)
s.program(0, 13, 110, 60)             # xylophone
s.program(1, 45, 100, 60)             # pizzicato strings
s.program(2, 70, 95, 60)              # bassoon
tune = [('D5', 0), ('F5', 0.5), ('A5', 1), ('G#5', 1.5), ('A5', 2), ('F5', 2.5), ('D5', 3), ('E5', 3.5), ('F5', 4), ('E5', 4.5), ('C#5', 5)]
for p, b in tune:
    s.note(0, p, b, 0.45, 100)
for bar, (bass, chord) in enumerate((('D3', ['F4', 'A4']), ('D3', ['F4', 'A4']), ('A2', ['E4', 'C#5']))):
    s.note(2, bass, bar * 2, 0.9, 95)
    s.chord(1, chord, bar * 2 + 1, 0.4, 85)
    s.chord(1, chord, bar * 2 + 1.5, 0.4, 80)
s.chord(0, ['D5', 'F#5', 'A5', 'D6'], 6, 1.5, 115)
s.chord(1, ['D3', 'A3', 'F#4', 'D5'], 6, 1.5, 110)
s.note(2, 'D2', 6, 2, 105)
s.drum(CRASH, 6, 100, 2)
s.save('halloween_mvp_squelettes.mid')

# H5 (MVP) - The vampire's harpsichord: a baroque flourish in G minor, a G major chord at the end
# with the organ and a bell.
s = Song(120)
s.program(0, 6, 110, 70)              # harpsichord
s.program(1, 19, 95, 90)              # church organ
s.program(2, 14, 95, 90)              # tubular bells
run = ['G4', 'A4', 'Bb4', 'C5', 'D5', 'Eb5', 'F#5', 'G5', 'D5', 'Bb4', 'G4', 'D4']
for i, p in enumerate(run):
    s.note(0, p, i * 0.25, 0.3, 100)
s.chord(0, ['G3', 'B3', 'D4', 'G4', 'B4', 'D5'], 3.0, 2, 110)
s.chord(1, ['G2', 'D3', 'G3', 'B3', 'D4'], 3.0, 2.5, 100)
s.note(2, 'G4', 3.0, 3, 100)
s.save('halloween_mvp_clavecin_vampire.mid')

# H6 (MVP) - Night triumph: choir and orchestra, from E minor to E major, bells and a hit.
s = Song(96)
s.program(0, 52, 110, 90)             # choir
s.program(1, 48, 105, 80)             # strings
s.program(2, 61, 110, 70)             # brass
s.program(3, 14, 95, 90)              # bells
s.program(4, 55, 100, 60)             # orchestra hit
s.chord(0, ['E4', 'G4', 'B4'], 0, 1, 90); s.chord(1, ['E3', 'B3', 'G4'], 0, 1, 85)
s.chord(0, ['C4', 'E4', 'A4'], 1, 1, 95); s.chord(1, ['A2', 'E3', 'C4'], 1, 1, 90)
s.chord(0, ['B3', 'D#4', 'F#4'], 2, 1, 100); s.chord(1, ['B2', 'F#3', 'D#4'], 2, 1, 95)
s.chord(2, ['B3', 'D#4', 'F#4'], 2.5, 0.5, 100)
s.note(4, 'E4', 3, 1, 115)
s.chord(0, ['E4', 'G#4', 'B4', 'E5'], 3, 3, 115)
s.chord(1, ['E2', 'E3', 'B3', 'G#4'], 3, 3, 110)
s.chord(2, ['E3', 'B3', 'E4', 'G#4'], 3, 2.5, 115)
s.note(3, 'E5', 3, 3, 100); s.note(3, 'B4', 3.5, 2.5, 85)
s.drum(CRASH, 3, 110, 2)
s.save('halloween_mvp_triomphe_nocturne.mid')
