"""Halloween sounds for the meter's banner: three for the teased, three for the MVP.
Synthesized here. Two quote works long in the public domain - Chopin's funeral march (1839) and
Bach's Toccata in D minor (c. 1705) - played by this script, no recording used; the rest is original."""
import os
import sys
import wave
import numpy as np

SR = 44100
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
FLATS = {'C#': 'Db', 'D#': 'Eb', 'F#': 'Gb', 'G#': 'Ab', 'A#': 'Bb'}
NOTE = {}
for o in range(0, 8):
    for i, n in enumerate(NAMES):
        hz = 440.0 * 2 ** ((o - 4) + (i - 9) / 12)
        NOTE[f'{n}{o}'] = hz
        if n in FLATS:
            NOTE[f'{FLATS[n]}{o}'] = hz
rng = np.random.default_rng(13)


def buf(seconds):
    return np.zeros(int(seconds * SR))


def env(n, attack, release):
    e = np.ones(n)
    a, r = max(1, int(attack * SR)), max(1, int(release * SR))
    e[:a] = np.linspace(0, 1, a)
    e[-r:] *= np.linspace(1, 0, r)
    return e


def organ(freq, seconds, bright=1.0):
    """Pipe organ: drawbars 16', 8', 4', 2 2/3', 2', a slow tremulant."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    trem = 1 + 0.004 * np.sin(2 * np.pi * 5.2 * t)
    sig = np.zeros(n)
    for mult, w in ((0.5, 0.6), (1, 1.0), (2, 0.55 * bright), (3, 0.3 * bright), (4, 0.25 * bright), (6, 0.1 * bright)):
        phase = 2 * np.pi * np.cumsum(np.full(n, freq * mult) * trem) / SR
        sig += w * np.sin(phase)
    return sig * env(n, 0.03, min(0.25, seconds / 3))


def bell(freq, seconds):
    """A church bell: inharmonic partials, long decay."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    sig = np.zeros(n)
    for ratio, w, decay in ((0.5, 0.7, 0.9), (1.0, 1.0, 1.4), (1.19, 0.5, 2.0), (1.5, 0.4, 2.5), (2.0, 0.45, 3.0), (2.5, 0.25, 4.0), (3.0, 0.2, 5.0), (4.2, 0.12, 6.5)):
        sig += w * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t * decay)
    return sig * env(n, 0.002, 0.3)


def theremin(path, seconds, vibrato=0.02):
    """A gliding sine with a wide vibrato; path = [(time fraction, Hz), ...]."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    xs = np.array([p[0] for p in path]) * seconds
    f = np.interp(t, xs, [p[1] for p in path])
    f = f * (1 + vibrato * np.sin(2 * np.pi * 6.0 * t) * np.clip(t / 0.3, 0, 1))
    phase = 2 * np.pi * np.cumsum(f) / SR
    return (np.sin(phase) + 0.15 * np.sin(2 * phase)) * env(n, 0.15, 0.5)


def thunder(seconds=2.5, loud=1.0):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    noise = rng.uniform(-1, 1, n)
    low = np.convolve(noise, np.ones(60) / 60, mode='same') * 6
    crack = noise * np.exp(-t * 18) * 0.6
    rumble = low * (np.exp(-t * 1.4) * (1 + 0.5 * np.sin(2 * np.pi * 3 * t)))
    return (crack + rumble) * loud * env(n, 0.005, 0.6)


def wind(seconds, loud=0.15):
    n = int(seconds * SR)
    noise = rng.uniform(-1, 1, n)
    smooth = np.convolve(noise, np.ones(30) / 30, mode='same')
    t = np.arange(n) / SR
    return smooth * (0.6 + 0.4 * np.sin(2 * np.pi * 0.35 * t)) * loud * 8 * env(n, 0.5, 0.8)


def add(track, sig, at, gain=1.0):
    s = int(at * SR)
    e = min(len(track), s + len(sig))
    track[s:e] += sig[:e - s] * gain


def chord(track, notes, at, seconds, inst=organ, gain=0.5):
    for n in notes:
        add(track, inst(NOTE[n], seconds), at, gain / len(notes) ** 0.5)


def reverb(x, wet=0.35):
    out = x.copy()
    for delay, g in ((0.031, 0.55), (0.043, 0.5), (0.061, 0.45), (0.089, 0.38), (0.131, 0.3), (0.197, 0.22), (0.283, 0.15)):
        d = int(delay * SR)
        y = np.zeros_like(x)
        y[d:] = x[:-d] * g
        out += y * wet
    return out


def save(name, track):
    track = reverb(track)
    win = int(0.5 * SR)
    loudest = max(np.sqrt(np.mean(track[i:i + win] ** 2)) for i in range(0, max(1, len(track) - win), win // 4))
    track *= 0.22 / loudest
    track = np.tanh(track * 1.3) / np.tanh(1.3)
    track *= 0.85 / max(0.85, np.max(np.abs(track)))
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((track * 32767).astype(np.int16).tobytes())
    print(name, f'{len(track) / SR:.1f} s')


# ---- For the teased ----
# 1 - Chopin's funeral march (public domain), on a deep organ: "dum, dum-da-dum..."
a = buf(4.6)
beat = 0.55
melody = [('Bb3', 0, 1), ('Bb3', 1, 0.75), ('Bb3', 1.75, 0.25), ('Bb3', 2, 1),
          ('Db4', 3, 0.75), ('C4', 3.75, 0.25), ('C4', 4, 0.75), ('Bb3', 4.75, 0.25), ('Bb3', 5, 0.75), ('A3', 5.75, 0.25), ('Bb3', 6, 1.6)]
for n, start, length in melody:
    add(a, organ(NOTE[n], length * beat * 0.95, bright=0.7), start * beat, 0.55)
for start, length, notes in ((0, 3, ['Bb1', 'F2', 'Bb2']), (3, 3, ['Gb1', 'Db2', 'Bb2']), (6, 1.8, ['Bb1', 'F2', 'Db3'])):
    chord(a, notes, start * beat, length * beat, organ, 0.5)
save('taquin_1_marche_funebre.wav', a)

# 2 - A ghost: theremin "wooOOOooo" over the wind, ending low and sad
b = buf(3.4)
add(b, wind(3.4), 0, 1.0)
add(b, theremin([(0, 330), (0.25, 520), (0.45, 470), (0.7, 300), (1.0, 165)], 3.2, 0.025), 0.1, 0.8)
save('taquin_2_fantome.wav', b)

# 3 - The death knell: three slow tolls of a deep bell, a low drone underneath
c = buf(5.0)
for i in range(3):
    add(c, bell(NOTE['G2'], 2.6), i * 1.25, 0.9)
chord(c, ['G1', 'D2'], 0, 4.6, organ, 0.25)
save('taquin_3_glas.wav', c)

# ---- For the MVP ----
# 4 - Bach's Toccata in D minor (public domain), the famous opening on a full organ
d = buf(5.2)
run = [('A5', 0.00, 0.09), ('G5', 0.09, 0.09), ('A5', 0.18, 0.9),
       ('G5', 1.40, 0.11), ('F5', 1.51, 0.11), ('E5', 1.62, 0.11), ('D5', 1.73, 0.11), ('C#5', 1.84, 0.45), ('D5', 2.35, 1.0)]
for n, start, length in run:
    add(d, organ(NOTE[n], length), start, 0.45)
    add(d, organ(NOTE[n] / 2, length), start, 0.35)
chord(d, ['D2', 'A2', 'D3', 'F3', 'A3', 'D4'], 3.3, 1.9, organ, 0.8)
save('mvp_4_toccata.wav', d)

# 5 - Victory from the crypt: organ in D minor turning to D major, thunder on the last chord
e = buf(4.8)
chord(e, ['D3', 'F3', 'A3', 'D4'], 0.0, 0.55, organ, 0.6)
chord(e, ['Bb2', 'D3', 'F3', 'Bb3'], 0.6, 0.55, organ, 0.6)
chord(e, ['A2', 'C#3', 'E3', 'A3'], 1.2, 0.55, organ, 0.6)
chord(e, ['D3', 'F#3', 'A3', 'D4', 'F#4', 'A4'], 1.85, 2.6, organ, 0.9)
add(e, thunder(2.8), 1.85, 0.45)
save('mvp_5_crypte_victoire.wav', e)

# 6 - Midnight bells: a peal of four bells, then a big bell and an organ chord
f = buf(4.8)
for i, n in enumerate(['E5', 'C5', 'D5', 'G4']):
    add(f, bell(NOTE[n], 1.6), i * 0.32, 0.5)
add(f, bell(NOTE['C4'], 3.2), 1.45, 0.8)
chord(f, ['C3', 'G3', 'C4', 'E4', 'G4'], 1.45, 3.0, organ, 0.55)
save('mvp_6_cloches_minuit.wav', f)
