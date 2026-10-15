"""Eight ORIGINAL victory jingles for the meter's MVP, one per style, written here as MIDI and played
by real recorded instruments (GeneralUser GS through FluidSynth). Every melody below is made up for
the meter: no existing tune is quoted.

usage: python compose_mvp.py <midi out folder>"""
import os
import struct
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
TPB = 480
NAMES = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6, 'G': 7, 'G#': 8, 'Ab': 8, 'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}


def n(name):
    """'C4' -> 60."""
    pitch, octave = (name[:2], name[2:]) if len(name) > 2 and name[1] in '#b' else (name[:1], name[1:])
    return 12 * (int(octave) + 1) + NAMES[pitch]


class Song:
    def __init__(self, bpm):
        self.bpm = bpm
        self.events = []  # (tick, order, bytes)

    def program(self, ch, prog, volume=100, reverb=50, chorus=0, pan=64):
        for order, data in ((0, bytes([0xC0 | ch, prog])), (1, bytes([0xB0 | ch, 7, volume])), (1, bytes([0xB0 | ch, 91, reverb])),
                            (1, bytes([0xB0 | ch, 93, chorus])), (1, bytes([0xB0 | ch, 10, pan]))):
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

    def save(self, name):
        tempo = int(60_000_000 / self.bpm)
        evs = sorted(self.events, key=lambda e: (e[0], e[1]))
        body, last = bytearray(), 0
        body += b'\x00\xFF\x51\x03' + tempo.to_bytes(3, 'big')
        for tick, _, data in evs:
            body += vlq(tick - last) + data
            last = tick
        body += vlq(TPB * 2) + b'\xFF\x2F\x00'  # two beats of tail for the reverb
        with open(os.path.join(OUT, name), 'wb') as f:
            f.write(b'MThd' + struct.pack('>IHHH', 6, 0, 1, TPB))
            f.write(b'MTrk' + struct.pack('>I', len(body)) + body)
        print(name)


def vlq(value):
    out = [value & 0x7F]
    value >>= 7
    while value:
        out.insert(0, (value & 0x7F) | 0x80)
        value >>= 7
    return bytes(out)


KICK, SNARE, CRASH, RIDE, HAT, OPEN_HAT, TOM_LOW, TOM_MID, TOM_HIGH, SPLASH, CLAP = 36, 38, 49, 51, 42, 46, 41, 45, 48, 55, 39
WOOD_HIGH, WOOD_LOW, TAMBOURINE, SHAKER = 76, 77, 54, 70

# 1 - Orchestral, epic: horns call, the brass climb, strings and timpani, a big C major landing.
s = Song(116)
s.program(0, 60, 110, 70)          # French horns
s.program(1, 61, 112, 70)          # brass section
s.program(2, 48, 100, 80)          # strings
s.program(3, 47, 120, 60)          # timpani
s.program(4, 56, 105, 70)          # trumpet
for b, p, d in ((0, 'G3', 0.5), (0.5, 'C4', 0.5), (1, 'E4', 1.0)):
    s.note(0, p, b, d, 105)
s.chord(2, ['C3', 'G3', 'E4'], 0, 2, 70)
s.note(3, 'C2', 0, 0.5, 110); s.note(3, 'G1', 1, 0.5, 100)
for b, p, d in ((2, 'F4', 0.5), (2.5, 'A4', 0.5), (3, 'C5', 0.75), (3.75, 'B4', 0.25)):
    s.note(4, p, b, d, 110)
s.chord(1, ['F3', 'A3', 'C4'], 2, 1, 100); s.chord(1, ['G3', 'B3', 'D4'], 3, 1, 105)
s.chord(2, ['F3', 'C4', 'A4'], 2, 1, 80); s.chord(2, ['G3', 'D4', 'B4'], 3, 1, 85)
for i in range(8):
    s.note(3, 'G1', 3 + i * 0.125, 0.125, 70 + i * 6)
s.note(4, 'E5', 4, 3, 120); s.note(4, 'C5', 4, 3, 110)
s.chord(1, ['C3', 'G3', 'C4', 'E4', 'G4'], 4, 3, 120)
s.chord(0, ['C3', 'G3', 'E4'], 4, 3, 110)
s.chord(2, ['C2', 'C3', 'G3', 'E4', 'C5'], 4, 3, 100)
s.note(3, 'C2', 4, 1, 127)
s.drum(CRASH, 4, 110, 2)
s.save('mvp_epique.mid')

# 2 - Rock: a power-chord riff, drums, a fill, the last chord held over a crash.
s = Song(150)
s.program(0, 30, 112, 30)          # distortion guitar
s.program(1, 33, 110, 20)          # finger bass
s.program(2, 29, 95, 40, chorus=30, pan=40)   # overdriven, doubled left
riff = [('E3', 0, 0.5), ('E3', 0.5, 0.5), ('G3', 1, 0.5), ('A3', 1.5, 1), ('E3', 2.5, 0.5), ('G3', 3, 0.5), ('Bb3', 3.5, 0.5), ('A3', 4, 1)]
for p, b, d in riff:
    root = n(p)
    s.chord(0, [root, root + 7, root + 12], b, d * 0.9, 110)
    s.chord(2, [root + 12, root + 19], b, d * 0.9, 90)
    s.note(1, root - 12, b, d * 0.9, 105)
for b in range(5):
    s.drum(KICK, b, 115); s.drum(SNARE, b + 0.5, 110); s.drum(HAT, b, 70); s.drum(HAT, b + 0.5, 70)
for i, t in enumerate((TOM_HIGH, TOM_HIGH, TOM_MID, TOM_MID, TOM_LOW, TOM_LOW)):
    s.drum(t, 5 + i * 0.1667, 100 + i * 4)
s.chord(0, ['E3', 'B3', 'E4', 'G#4'], 6, 3, 120)
s.note(1, 'E2', 6, 3, 115)
s.drum(KICK, 6, 127); s.drum(CRASH, 6, 120, 2); s.drum(SPLASH, 6, 90)
s.save('mvp_rock.mid')

# 3 - Big band jazz: swung brass stabs, walking bass, a sax lick, a C6/9 to end.
s = Song(168)
s.program(0, 61, 105, 60)          # brass section
s.program(1, 32, 105, 30)          # acoustic bass
s.program(2, 65, 100, 60)          # alto sax
s.program(3, 56, 100, 60, pan=80)  # trumpet
sw = lambda beat: beat if beat == int(beat) else int(beat) + 0.66
for b, ch in ((0, ['E4', 'G4', 'Bb4', 'D5']), (1.5, ['F4', 'A4', 'C5', 'Eb5']), (3, ['E4', 'G4', 'Bb4', 'D5'])):
    s.chord(0, ch, sw(b), 0.5, 110)
for i, p in enumerate(['C2', 'E2', 'G2', 'A2', 'Bb2', 'A2', 'G2', 'E2']):
    s.note(1, p, i * 0.5 * 1.0, 0.45, 95)
for b, p, d in ((0.5, 'G4', 0.33), (sw(0.5) + 0.34, 'A4', 0.33), (1, 'Bb4', 0.5), (2, 'B4', 0.33), (sw(2.5), 'C5', 0.5), (3.5, 'E5', 0.4)):
    s.note(2, p, b, d, 100)
for b in range(4):
    s.drum(RIDE, b, 80); s.drum(RIDE, sw(b + 0.5), 65); s.drum(HAT, b + 1 if b % 2 == 0 else b, 60)
s.chord(0, ['C4', 'E4', 'A4', 'D5'], 4, 3, 115)
s.note(3, 'G5', 4, 3, 110)
s.note(2, 'E5', 4, 3, 95)
s.note(1, 'C2', 4, 3, 105)
s.drum(CRASH, 4, 100, 2); s.drum(KICK, 4, 110)
s.save('mvp_jazz.mid')

# 4 - Japanese RPG victory: harp run, strings, a flute and trumpet tune, a bell at the end.
s = Song(132)
s.program(0, 46, 100, 70)          # harp
s.program(1, 48, 95, 80)           # strings
s.program(2, 73, 105, 70)          # flute
s.program(3, 56, 105, 70)          # trumpet
s.program(4, 14, 90, 80)           # tubular bells
for i, p in enumerate(['C4', 'E4', 'G4', 'C5', 'E5', 'G5', 'C6', 'E6']):
    s.note(0, p, i * 0.125, 0.6, 90)
s.chord(1, ['C3', 'G3', 'E4'], 0, 2, 70)
for b, p, d in ((1, 'G5', 0.5), (1.5, 'E5', 0.25), (1.75, 'G5', 0.25), (2, 'A5', 0.5), (2.5, 'G5', 0.5), (3, 'F5', 0.5), (3.5, 'D5', 0.5)):
    s.note(2, p, b, d, 100)
s.chord(1, ['F3', 'A3', 'F4'], 2, 1, 80); s.chord(1, ['G3', 'B3', 'D4'], 3, 1, 85)
s.note(3, 'E5', 4, 2.5, 110); s.note(3, 'C5', 4, 2.5, 100)
s.chord(1, ['C3', 'G3', 'C4', 'E4', 'G4'], 4, 3, 100)
for i, p in enumerate(['C5', 'E5', 'G5', 'C6']):
    s.note(0, p, 4 + i * 0.1, 2.5, 95)
s.note(4, 'C5', 4, 3, 100)
s.save('mvp_rpg.mid')

# 5 - Western: a whistled tune over a strummed guitar and clip-clop, a twang to close.
s = Song(108)
s.program(0, 78, 105, 60)          # whistle
s.program(1, 25, 100, 40)          # steel guitar
s.program(2, 105, 90, 40, pan=90)  # banjo
for b, p, d in ((0, 'A4', 0.5), (0.5, 'D5', 0.75), (1.25, 'E5', 0.25), (1.5, 'F5', 1), (2.5, 'E5', 0.5), (3, 'D5', 0.5), (3.5, 'A4', 0.5)):
    s.note(0, p, b, d, 100)
for b, ch in ((0, ['D3', 'A3', 'D4', 'F4']), (1, ['D3', 'A3', 'D4', 'F4']), (2, ['C3', 'G3', 'C4', 'E4']), (3, ['A2', 'E3', 'A3', 'C#4'])):
    for k, p in enumerate(ch):
        s.note(1, p, b + k * 0.03, 0.9, 85)
for i in range(8):
    s.drum(WOOD_HIGH if i % 2 == 0 else WOOD_LOW, i * 0.5, 80)
s.note(0, 'D5', 4, 2.5, 105)
for k, p in enumerate(['D3', 'A3', 'D4', 'F#4', 'A4']):
    s.note(1, p, 4 + k * 0.05, 2.5, 100)
s.note(2, 'D5', 4, 1, 90); s.note(2, 'A4', 4.5, 1, 80)
s.save('mvp_western.mid')

# 6 - Mariachi: trumpets in thirds, guitars and guitarrón strumming, a shaken end.
s = Song(176)
s.program(0, 56, 112, 60, pan=50)  # trumpet 1
s.program(1, 56, 105, 60, pan=78)  # trumpet 2
s.program(2, 24, 100, 40)          # nylon guitar
s.program(3, 32, 105, 30)          # acoustic bass
s.program(4, 40, 90, 60, pan=30)   # violin
tune = [('C5', 0, 0.5), ('D5', 0.5, 0.5), ('E5', 1, 1), ('F5', 2, 0.5), ('E5', 2.5, 0.5), ('D5', 3, 0.5), ('B4', 3.5, 0.5), ('C5', 4, 0.5), ('E5', 4.5, 0.5), ('G5', 5, 1)]
for p, b, d in tune:
    s.note(0, p, b, d * 0.95, 112)
    s.note(1, n(p) - 4 if p[0] in 'CFG' else n(p) - 3, b, d * 0.95, 100)
    s.note(4, n(p) + 12, b, d * 0.95, 70)
for b in range(6):
    s.note(3, 'C2' if b % 2 == 0 else 'G1', b, 0.5, 100)
    for k, p in enumerate(['C3', 'E3', 'G3', 'C4']):
        s.note(2, p, b + 0.5 + k * 0.02, 0.4, 80)
s.chord(0, ['C5', 'E5'], 6, 2.5, 118); s.chord(1, ['G4', 'C5'], 6, 2.5, 110)
s.note(3, 'C2', 6, 2.5, 110)
for i in range(10):
    s.drum(TAMBOURINE, 6 + i * 0.12, 70 + i * 3)
s.save('mvp_mariachi.mid')

# 7 - Gospel: church organ and choir swelling to an "aah", piano runs, a clap.
s = Song(96)
s.program(0, 19, 100, 80)          # church organ
s.program(1, 52, 110, 90)          # choir aahs
s.program(2, 0, 100, 60)           # piano
for b, ch in ((0, ['C3', 'G3', 'C4', 'E4']), (1, ['F3', 'A3', 'C4', 'F4']), (2, ['G3', 'B3', 'D4', 'G4'])):
    s.chord(0, ch, b, 1, 90)
    s.chord(1, ch[1:], b, 1, 80 + b * 10)
for i, p in enumerate(['C5', 'D5', 'E5', 'G5', 'A5', 'C6']):
    s.note(2, p, 2.4 + i * 0.1, 0.4, 90)
s.chord(0, ['C3', 'G3', 'C4', 'E4', 'G4'], 3, 3.5, 105)
s.chord(1, ['E4', 'G4', 'C5', 'E5'], 3, 3.5, 120)
s.chord(2, ['C3', 'C4', 'E4', 'G4', 'C5'], 3, 2, 100)
s.drum(CLAP, 3, 100); s.drum(CRASH, 3, 80, 2)
s.save('mvp_gospel.mid')

# 8 - Electro: a pulse that builds, a snare roll, the drop on a big chord.
s = Song(128)
s.program(0, 81, 100, 50)          # saw lead
s.program(1, 38, 110, 20)          # synth bass
s.program(2, 89, 85, 80)           # warm pad
s.program(3, 55, 100, 60)          # orchestra hit
for i in range(16):
    b = i * 0.25
    s.note(0, ['A4', 'C5', 'E5', 'A5'][i % 4] if i < 8 else ['C5', 'E5', 'G5', 'C6'][i % 4], b, 0.2, 70 + i * 3)
s.chord(2, ['A3', 'C4', 'E4'], 0, 2, 70); s.chord(2, ['C4', 'E4', 'G4'], 2, 2, 80)
for b in range(4):
    s.drum(KICK, b, 110)
for i in range(16):
    s.drum(SNARE, 2 + i * 0.125, 50 + i * 4)
s.note(3, 'F4', 4, 1, 120)
s.chord(0, ['F4', 'A4', 'C5', 'F5'], 4, 2.5, 115)
s.note(1, 'F1', 4, 2.5, 120)
s.chord(2, ['F3', 'A3', 'C4', 'F4'], 4, 3, 100)
s.drum(KICK, 4, 127); s.drum(CRASH, 4, 120, 2)
s.save('mvp_electro.mid')
