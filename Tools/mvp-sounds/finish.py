"""Trims the renders' silent tail (a short fade), and brings them all to one loudness."""
import glob, os, sys, wave
import numpy as np
src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
for f in sorted(glob.glob(os.path.join(src, '*.wav'))):
    w = wave.open(f); ch, sr = w.getnchannels(), w.getframerate()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).reshape(-1, ch) / 32767
    loud = np.where(np.abs(x).max(axis=1) > 0.004)[0]
    end = min(len(x), loud[-1] + int(0.35 * sr))
    x = x[:end].copy()
    fade = int(0.3 * sr)
    x[-fade:] *= np.linspace(1, 0, fade)[:, None]
    win = sr // 2
    loudest = max(np.sqrt(np.mean(x[i:i + win] ** 2)) for i in range(0, max(1, len(x) - win), win // 4))
    x *= 0.2 / loudest
    x = np.tanh(x * 1.2) / np.tanh(1.2)
    x *= 0.89 / max(0.89, np.abs(x).max())
    out = os.path.join(dst, os.path.basename(f))
    with wave.open(out, 'wb') as o:
        o.setnchannels(ch); o.setsampwidth(2); o.setframerate(sr)
        o.writeframes((x * 32767).astype(np.int16).tobytes())
    print(f'{os.path.basename(f):20} {len(x) / sr:4.1f} s, peak {np.abs(x).max():.2f}')
