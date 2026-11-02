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




def mel(s, ch, text, beat0=0.0, vel=100, shift=0, scale=1.0, gate=0.92):
    """'G4:1 B4:0.5 r:1 C4+E4+G4:2' -> notes in a row; r = rest; + = chord."""
    b = beat0
    for tok in text.split():
        name, length = tok.split(':')
        length = float(length) * scale
        if name != 'r':
            for part in name.split('+'):
                s.note(ch, n(part) + shift, b, max(0.08, length * gate), vel)
        b += length
    return b


WOOD_HI, WOOD_LO, CLAVES, COWBELL = 76, 77, 75, 56
