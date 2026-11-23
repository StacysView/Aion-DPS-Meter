"""Makes the little retro sounds of the Monsters theme (original, synthesised here: nothing sampled): the boss's life warnings and the victory jingle.

Usage: python make_sounds.py <out folder, Client/assets/sounds/moments>
16 bit mono, 22 050 Hz, square waves with a short envelope. A file of the same name (mp3 first) in the meter's data folder wins over these.
"""
import math
import struct
import sys
import wave

RATE = 22050


def tone(freq, seconds, volume=0.5, duty=0.5):
    n = int(RATE * seconds)
    out = []
    for i in range(n):
        t = i / RATE
        phase = (t * freq) % 1.0
        v = 1.0 if phase < duty else -1.0
        attack = min(1.0, i / (RATE * 0.004))
        release = min(1.0, (n - i) / (RATE * 0.03))
        out.append(v * volume * attack * release)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def write(path, samples):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, s)) * 32000)) for s in samples))


def main(folder):
    # life under half: two soft beeps
    warn = tone(784, 0.11) + silence(0.07) + tone(784, 0.11)
    write(f'{folder}/hp_warn.wav', warn)
    # life under a fifth: three quick high beeps, twice
    crit = []
    for _ in range(2):
        for _ in range(3):
            crit += tone(1175, 0.07, duty=0.25) + silence(0.04)
        crit += silence(0.1)
    write(f'{folder}/hp_crit.wav', crit)
    # victory: a rising arpeggio and a held chord
    notes = [(523, 0.11), (659, 0.11), (784, 0.11), (1047, 0.11), (784, 0.09), (1047, 0.30)]
    win = []
    for f, d in notes:
        win += tone(f, d, 0.45, duty=0.25)
    chord = [a + b + c for a, b, c in zip(tone(1047, 0.55, 0.22, 0.25), tone(1319, 0.55, 0.2, 0.25), tone(1568, 0.55, 0.18, 0.25))]
    win += chord
    write(f'{folder}/victory.wav', win)
    # level up: the arpeggio of the victory, higher and faster
    up = []
    for f in (659, 784, 988, 1319, 1568, 1976):
        up += tone(f, 0.075, 0.42, duty=0.25)
    up += tone(2093, 0.4, 0.4, duty=0.25)
    write(f'{folder}/level_up.wav', up)
    # shiny: a quick twinkle going up, each note softer than the one before it
    tw = []
    for i, f in enumerate((1568, 1976, 2349, 3136, 2349, 3136)):
        tw += tone(f, 0.07, 0.34 - i * 0.03, duty=0.25) + silence(0.025)
    tw += tone(3951, 0.3, 0.2, duty=0.25)
    write(f'{folder}/shiny.wav', tw)
    print('wrote hp_warn, hp_crit, victory, level_up, shiny')


if __name__ == '__main__':
    main(sys.argv[1])
