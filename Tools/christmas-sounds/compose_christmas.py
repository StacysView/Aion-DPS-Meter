"""Christmas sounds for the meter, in season (1 December to 6 January): five for a hidden cube appearing, five for the MVP, five for the teased
(Stacy, 2026-10-29). Written here as MIDI, played by recorded instruments - the GeneralUser GS SoundFont through FluidSynth, as the other jingles (see
../mvp-sounds/README.md). The tunes are carols long in the public domain - Jingle Bells (1857), Silent Night (1818), Deck the Halls (a Welsh air of the
16th century), We Wish You a Merry Christmas (a traditional carol), Joy to the World (1719 / 1839), O Come All Ye Faithful (18th century), Shchedryk, the
Carol of the Bells (Leontovych, 1916) - arranged here; the rest is made up for the meter.

usage: python compose_christmas.py <midi out folder>"""
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



KICK, SNARE, CRASH, RIDE, HAT, TOM_LOW, TRIANGLE, SLEIGH, SHAKER = 36, 38, 49, 51, 42, 41, 81, 83, 82

# General MIDI programs used (0-based): 8 celesta, 9 glockenspiel, 10 music box, 11 vibraphone, 14 tubular bells, 19 church organ, 46 harp, 47 timpani,
# 48 strings, 52 choir aahs, 53 voice oohs, 56 trumpet, 57 trombone, 58 tuba, 60 french horn, 61 brass section.

JINGLE = [('E4', 0, 1), ('E4', 1, 1), ('E4', 2, 2), ('E4', 4, 1), ('E4', 5, 1), ('E4', 6, 2), ('E4', 8, 1), ('G4', 9, 1), ('C4', 10, 1.5), ('D4', 11.5, 0.5), ('E4', 12, 4)]
SILENT = [('G4', 0, 1.5), ('A4', 1.5, 0.5), ('G4', 2, 1), ('E4', 3, 3), ('G4', 6, 1.5), ('A4', 7.5, 0.5), ('G4', 8, 1), ('E4', 9, 3), ('D5', 12, 2), ('D5', 14, 1), ('B4', 15, 3)]
BELLS = [('G4', 0, 1), ('F#4', 1, 0.5), ('G4', 1.5, 0.5), ('D4', 2, 1)]   # the four-note figure of Shchedryk (Leontovych, 1916), in 3/4
DECK = [('G5', 0, 1.5), ('F5', 1.5, 0.5), ('E5', 2, 1), ('D5', 3, 1), ('C5', 4, 1), ('D5', 5, 1), ('E5', 6, 1), ('C5', 7, 1), ('D5', 8, 0.5), ('E5', 8.5, 0.5), ('F5', 9, 0.5),
        ('D5', 9.5, 0.5), ('E5', 10, 1.5), ('D5', 11.5, 0.5), ('C5', 12, 1), ('B4', 13, 1), ('C5', 14, 2)]
WISH = [('D4', 0, 1), ('G4', 1, 1), ('G4', 2, 0.5), ('A4', 2.5, 0.5), ('G4', 3, 0.5), ('F#4', 3.5, 0.5), ('E4', 4, 1), ('E4', 5, 1), ('E4', 6, 1), ('A4', 7, 1), ('A4', 8, 0.5),
        ('B4', 8.5, 0.5), ('A4', 9, 0.5), ('G4', 9.5, 0.5), ('F#4', 10, 1), ('D4', 11, 1), ('D4', 12, 1), ('B4', 13, 1), ('B4', 14, 0.5), ('C5', 14.5, 0.5), ('B4', 15, 0.5), ('A4', 15.5, 0.5),
        ('G4', 16, 1), ('E4', 17, 1), ('D4', 18, 1)]
JOY = [('C5', 0, 1.5), ('B4', 1.5, 0.5), ('A4', 2, 1), ('G4', 3, 1.5), ('F4', 4.5, 0.5), ('E4', 5, 1), ('D4', 6, 1), ('C4', 7, 2)]
ADESTE = [('G4', 0, 1), ('G4', 1, 1), ('D4', 2, 1), ('G4', 3, 1), ('A4', 4, 1), ('D4', 5, 2), ('B4', 7, 1), ('A4', 8, 1), ('B4', 9, 1), ('C5', 10, 1), ('B4', 11, 2), ('A4', 13, 1), ('G4', 14, 2)]


def line(s, ch, notes, vel=100, beat0=0.0, shift=0, scale=1.0, gate=0.95):
    """Plays [(note, start, length)] on channel ch; shift = semitones, scale = stretch of the times."""
    for name, start, length in notes:
        p = n(name) + shift
        s.note(ch, p, beat0 + start * scale, max(0.1, length * scale * gate), vel)


def sleigh(s, beat, beats, step=0.25, vel=70):
    b = beat
    while b < beat + beats:
        s.drum(SLEIGH, b, vel, 0.2)
        b += step


# ================= a hidden cube appears (short, quick, recognisable) =================
# 1 - Sleigh bells shaken, three rising glockenspiel notes
s = Song(120)
s.program(0, 9, 110, 60)
s.program(1, 8, 90, 60)
sleigh(s, 0, 1.6, 0.125, 75)
for i, p in enumerate(['G5', 'B5', 'D6']):
    s.note(0, p, 0.25 + i * 0.3, 0.5, 105)
s.note(0, 'G6', 1.0, 1.6, 100)
s.note(1, 'G5', 1.0, 1.6, 80)
s.save('cube_1_cloches_traineau.mid')

# 2 - Jingle Bells' first line on a glockenspiel, sleigh bells under it
s = Song(150)
s.program(0, 9, 110, 60)
s.program(1, 8, 80, 60)
line(s, 0, JINGLE[:3], 105, shift=12, scale=0.5)
line(s, 1, JINGLE[:3], 70, shift=0, scale=0.5)
sleigh(s, 0, 2.4, 0.25, 65)
s.note(0, 'C6', 2.6, 1.6, 95)
s.note(1, 'C5', 2.6, 1.6, 80)
s.save('cube_2_jingle.mid')

# 3 - Carol of the Bells: the four-note figure, three times, on tubular bells and a glockenspiel
s = Song(200)
s.program(0, 14, 100, 70)
s.program(1, 9, 100, 60)
for rep in range(2):
    line(s, 0, BELLS, 100, beat0=rep * 3, shift=12, scale=1.0)
    line(s, 1, BELLS, 85, beat0=rep * 3, shift=24, scale=1.0)
s.note(0, 'G4', 6, 2, 95)
s.save('cube_3_carillon.mid')

# 4 - Silent Night's opening on a music box
s = Song(100)
s.program(0, 10, 115, 70)
s.program(1, 46, 70, 70)
line(s, 0, SILENT[:4], 100, shift=12, scale=1.0)
s.note(1, 'C3', 0, 6, 70)
s.note(1, 'G3', 0, 6, 60)
s.save('cube_4_boite_a_musique.mid')

# 5 - "Fa la la la la": a falling line on a celesta, a brass stab, bells
s = Song(140)
s.program(0, 8, 110, 60)
s.program(1, 61, 100, 50)
for i, p in enumerate(['G5', 'F5', 'E5', 'D5', 'C5']):
    s.note(0, p, i * 0.3, 0.4, 105)
s.chord(1, ['C4', 'E4', 'G4', 'C5'], 1.7, 1.4, 100)
s.note(0, 'C6', 1.7, 1.4, 100)
sleigh(s, 1.6, 1.6, 0.125, 70)
s.save('cube_5_fa_la_la.mid')

# ================= the MVP (a triumph) =================
# 1 - Joy to the World: the falling scale on brass and bells, a big chord, timpani
s = Song(150)
s.program(0, 61, 105, 55)
s.program(1, 14, 90, 60)
s.program(2, 47, 120, 50)
line(s, 0, JOY, 105, scale=1.0)
line(s, 1, JOY, 80, shift=12, scale=1.0)
s.chord(0, ['C3', 'G3', 'C4', 'E4', 'G4', 'C5'], 9, 4, 110)
s.note(2, 'C2', 9, 3, 125)
s.drum(CRASH, 9, 80, 2)
sleigh(s, 9, 3, 0.25, 70)
s.save('mvp_1_joie_du_monde.mid')

# 2 - O Come All Ye Faithful: the first line on brass, then a choir chord
s = Song(132)
s.program(0, 61, 105, 55)
s.program(1, 52, 100, 70)
s.program(2, 47, 120, 50)
s.program(3, 14, 80, 60)
line(s, 0, ADESTE[:6], 105, scale=1.0)
s.chord(1, ['G3', 'D4', 'G4', 'B4', 'D5'], 7, 5, 105)
s.chord(0, ['G2', 'D3', 'G3'], 7, 5, 95)
s.note(2, 'G2', 7, 3, 120)
s.note(3, 'G5', 7, 5, 85)
s.save('mvp_2_adeste.mid')

# 3 - We Wish You a Merry Christmas: the first lines on brass, a peal, a final chord
s = Song(160)
s.program(0, 61, 105, 55)
s.program(1, 9, 95, 55)
s.program(2, 47, 120, 50)
line(s, 0, WISH[:11], 105, scale=1.0)
s.chord(0, ['G3', 'B3', 'D4', 'G4', 'B4'], 7, 4, 110)
s.note(2, 'G2', 7, 3, 125)
for i, p in enumerate(['G5', 'B5', 'D6', 'G6']):
    s.note(1, p, 7 + i * 0.25, 2.5, 95)
sleigh(s, 7, 4, 0.25, 70)
s.save('mvp_3_bons_voeux.mid')

# 4 - Jingle Bells at a gallop: the chorus fast on a trumpet and a glockenspiel over sleigh bells, a last chord
s = Song(215)
s.program(0, 56, 105, 55)
s.program(1, 9, 90, 55)
s.program(2, 61, 100, 55)
s.program(3, 47, 120, 50)
line(s, 0, JINGLE, 105, scale=1.0, gate=0.85)
line(s, 1, JINGLE, 80, shift=12, scale=1.0, gate=0.7)
sleigh(s, 0, 16, 0.5, 60)
s.chord(2, ['C3', 'G3', 'C4', 'E4', 'G4'], 16, 4, 110)
s.note(3, 'C2', 16, 3, 125)
s.drum(CRASH, 16, 85, 2)
s.save('mvp_4_traineau_triomphal.mid')

# 5 - Gloria: a choir rising in arpeggio, a held chord, bells, a roll of timpani
s = Song(115)
s.program(0, 52, 105, 75)
s.program(1, 48, 85, 70)
s.program(2, 47, 120, 55)
s.program(3, 14, 85, 65)
for i, p in enumerate(['C4', 'E4', 'G4', 'C5', 'E5', 'G5']):
    s.note(0, p, i * 0.5, 1.6, 100)
s.chord(0, ['C3', 'G3', 'C4', 'E4', 'G4', 'C5'], 3.6, 6, 110)
s.chord(1, ['C3', 'G3', 'C4', 'E4'], 3.6, 6, 90)
for i in range(8):
    s.note(2, 'C2', 3.0 + i * 0.1, 0.2, 70 + i * 7)
s.note(2, 'C2', 3.8, 3, 125)
for i, p in enumerate(['C6', 'E6', 'G6', 'C7']):
    s.note(3, p, 3.7 + i * 0.2, 3, 85)
s.save('mvp_5_gloria.mid')

# ================= the teased (a joke) =================
# 1 - Jingle Bells slow and flat on a glockenspiel, sleigh bells fading, ending on a sad trombone
s = Song(125)
s.program(0, 9, 105, 60)
s.program(1, 57, 115, 50)
for i, (name, start, length) in enumerate(JINGLE[:6]):
    s.note(0, n(name) + 12 - i // 2, start * 1.0 + i * 0.12, length * 0.85, 100)
sleigh(s, 0, 5, 0.5, 45)
for i, p in enumerate(['F3', 'E3', 'Eb3']):
    s.note(1, p, 7.5 + i * 0.6, 0.55, 105)
s.note(1, 'D3', 9.3, 2.4, 110)
s.wobble(1, 9.5, 2.2, depth=0.45, rate=6.5)
s.bend(1, 11.9, 0)
s.save('taquin_1_jingle_pas_bien.mid')

# 2 - Silent Night on a music box that runs down: slower and slower, flatter and flatter, stopping in mid-air
s = Song(150)
s.program(0, 10, 115, 70)
s.program(1, 58, 100, 40)
t = 0.0
for i, (name, start, length) in enumerate(SILENT[:5]):
    s.note(0, n(name) + 12, t, max(0.4, length * 0.7 * (1 + 0.12 * i)), 95)
    s.bend(0, t + 0.01, -0.12 * i)
    t += max(0.8, length) * (1 + 0.16 * i)
s.bend(0, t + 0.1, 0)
s.note(1, 'D2', t + 0.1, 1.6, 100)
s.slide(1, t + 0.4, 1.3, 0, -3, curve=1.8)
s.bend(1, t + 2.0, 0)
s.save('taquin_2_boite_fatiguee.mid')

# 3 - Hoarse bells: Carol of the Bells stumbling out of tune, then a trombone's wah-wah
s = Song(200)
s.program(0, 14, 100, 60)
s.program(1, 57, 115, 50)
t = 0.0
for rep in range(3):
    for i, (name, start, length) in enumerate(BELLS):
        s.note(0, n(name) + 12, t + start * (1 + 0.1 * rep), length * 0.8, 95)
        s.bend(0, t + start * (1 + 0.1 * rep) + 0.005, -0.5 * rep + (0.2 if i % 2 else -0.15))
    t += 3 * (1 + 0.1 * rep)
s.bend(0, t + 1, 0)
for i, p in enumerate(['G3', 'F#3', 'F3']):
    s.note(1, p, t + 0.5 + i * 0.6, 0.55, 105)
s.note(1, 'E3', t + 2.3, 2.2, 110)
s.wobble(1, t + 2.5, 2.0, depth=0.4, rate=6.5)
s.bend(1, t + 4.6, 0)
s.save('taquin_3_cloches_enrouees.mid')

# 4 - Santa is not pleased: three low "ho ho ho" on a bass choir, dropping, then a trombone growl
s = Song(100)
s.program(0, 52, 118, 45)
s.program(1, 57, 110, 45)
for i, (b, d, p) in enumerate(((0.2, 0.6, 'D3'), (1.1, 0.6, 'C#3'), (2.0, 1.3, 'B2'))):
    s.note(0, p, b, d, 115)
    s.slide(0, b, d, 0, -2.5, steps=10, curve=1.0)
    s.bend(0, b + d + 0.02, 0)
sleigh(s, 1.8, 2, 0.5, 40)
s.note(1, 'D3', 3.6, 2.4, 110)
s.slide(1, 3.8, 2.0, 0, -5, steps=40, curve=1.5)
s.wobble(1, 4.0, 1.5, depth=0.3, rate=5.0)
s.bend(1, 6.2, 0)
s.save('taquin_4_pere_noel_fache.mid')

# 5 - The tired reindeer: Deck the Halls on a plodding tuba, slower and lower, then the sleigh bells stop
s = Song(135)
s.program(0, 58, 118, 40)
s.program(1, 60, 80, 40)
t = 0.0
for i, (name, start, length) in enumerate(DECK[:6]):
    d = max(0.6, length) * (1 + 0.12 * i)
    s.note(0, n(name) - 24, t, d * 0.9, 110)
    s.bend(0, t + 0.005, -0.1 * i)
    t += d
s.bend(0, t + 0.1, 0)
s.note(0, 'C2', t + 0.1, 2.2, 112)
s.slide(0, t + 0.5, 1.8, 0, -6, steps=40, curve=1.6)
s.bend(0, t + 2.6, 0)
sleigh(s, 0, t, 0.5, 40)
s.drum(KICK, t + 0.1, 100)
s.save('taquin_5_renne_fatigue.mid')
