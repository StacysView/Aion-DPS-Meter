using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// The boss-death banner's sounds, each meter playing the one about its own player only (Stacy,
/// 2026-10-11): the fanfare to the MVP, the sad trombone to the teased, nothing to the others.
/// <para>Both are made here as WAVs in memory - no file to ship, nothing downloaded, no rights to
/// anyone's music (the repository and the installer are public). The trombone ("wah wah wah waaah"):
/// four notes falling a semitone each, the last held with a wobble. The fanfare, an original one: a
/// three-note pickup, a C major chord, F and G chords, and a high C major chord held, over drum
/// hits, with an echo.</para>
/// <para>A sound file takes over, first found wins (see <see cref="SoundFile"/>): one's own on that PC
/// - fanfare.mp3 / .wav and trombone.mp3 / .wav in the meter's data folder (see
/// <see cref="AppDataFolder"/>) - then one shipped with the meter: Fanfare.mp3 / .wav and
/// Trombone.mp3 / .wav in assets/sounds, put in the repository by its owner (Stacy, 2026-10-11) and
/// installed with every update.</para>
/// <para>From 20 October to 4 November (Combat.LowDpsTease.IsHalloween), the sounds of
/// assets/sounds/halloween take turns before the shipped ones (see <see cref="SoundFor"/>): mvp_* for
/// the MVP, taquin_* for the teased - five chosen by Stacy (2026-10-15), made by
/// Tools/halloween-sounds/make_halloween.py.</para>
/// <para>All year the MVP's jingles of assets/sounds/mvp - eight original ones in as many styles,
/// played by recorded instruments (Tools/mvp-sounds) - take turns with the shipped fanfare (Stacy,
/// 2026-10-15). Every sound is played from a file, the meter's own included (see
/// <see cref="BuiltInFile"/>), at one volume: the settings' slider.</para>
/// </summary>
internal static class TeaseSound
{
    private const int SampleRate = 22050;
    private static byte[]? _trombone, _fanfare;
    private static System.Windows.Media.MediaPlayer? _player;
    private static readonly Dictionary<(bool Fanfare, bool Halloween, int Count), Combat.PhraseDeck> Turns = new();

    /// <summary>Plays a sound at <paramref name="volume"/> (0 to 1) without waiting for it (a failing
    /// audio device is ignored). Called on the UI thread (the poll timer), which the media player needs.</summary>
    public static void Play(bool fanfare, double volume)
    {
        try
        {
            DateTime now = DateTime.Now;
            bool halloween = Combat.LowDpsTease.IsHalloween(now);
            if ((SoundFor(fanfare, now, AppDataFolder.Path, AppContext.BaseDirectory, count => Turn(fanfare, halloween, count))
                    ?? BuiltInFile(fanfare)) is string path)
            {
                _player ??= new System.Windows.Media.MediaPlayer();
                _player.Volume = Math.Clamp(double.IsFinite(volume) ? volume : 0.5, 0, 1);
                _player.Open(new Uri(path));
                _player.Play();
            }
        }
        catch (Exception)
        {
            // No sound card, a device in use, an unreadable file: the banner shows all the same.
        }
    }

    /// <summary>The meter's own sound (<see cref="Fanfare"/> or the trombone of <see cref="Wav"/>) as a
    /// WAV file in the temporary folder, written once, so it plays at the volume like the others;
    /// null when it cannot be written.</summary>
    internal static string? BuiltInFile(bool fanfare)
    {
        try
        {
            byte[] wav = fanfare ? Fanfare() : Wav();
            string folder = Path.Combine(Path.GetTempPath(), "Aion DPS Meter sounds");
            string path = Path.Combine(folder, fanfare ? "fanfare.wav" : "trombone.wav");
            if (!File.Exists(path) || new FileInfo(path).Length != wav.Length)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, wav);
            }

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The sound file to play for this name, if any: the user's own in the data folder, else
    /// the one shipped in the install folder's assets/sounds; mp3 before wav. File names are not
    /// case-sensitive on Windows.</summary>
    internal static string? SoundFile(string name, string dataFolder, string installFolder) =>
        new[] { dataFolder, Path.Combine(installFolder, "assets", "sounds") }
            .SelectMany(folder => new[] { ".mp3", ".wav" }.Select(ext => Path.Combine(folder, name + ext)))
            .FirstOrDefault(File.Exists);

    /// <summary>The sound file to play on <paramref name="day"/>: the user's own first, then around
    /// Halloween one of assets/sounds/halloween (mvp_* or taquin_*, the one <paramref name="pick"/>
    /// chooses among them, by count); else for the MVP one of the shipped fanfare and the jingles of
    /// assets/sounds/mvp (picked the same way), for the teased the shipped trombone (see
    /// <see cref="SoundFile"/>); null for the meter's own sound.</summary>
    internal static string? SoundFor(bool fanfare, DateTime day, string dataFolder, string installFolder, Func<int, int> pick)
    {
        string name = fanfare ? "Fanfare" : "Trombone";
        if (new[] { ".mp3", ".wav" }.Select(ext => Path.Combine(dataFolder, name + ext)).FirstOrDefault(File.Exists) is string own)
        {
            return own;
        }

        string halloween = Path.Combine(installFolder, "assets", "sounds", "halloween");
        if (Combat.LowDpsTease.IsHalloween(day) && Directory.Exists(halloween))
        {
            var turns = Directory.GetFiles(halloween, (fanfare ? "mvp_" : "taquin_") + "*")
                .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (turns.Count > 0)
            {
                return turns[pick(turns.Count)];
            }
        }

        string? shipped = SoundFile(name, dataFolder, installFolder);
        string jingles = Path.Combine(installFolder, "assets", "sounds", "mvp");
        if (fanfare && Directory.Exists(jingles))
        {
            var turns = (shipped is null ? new List<string>() : new List<string> { shipped })
                .Concat(Directory.GetFiles(jingles)
                    .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (turns.Count > 0)
            {
                return turns[pick(turns.Count)];
            }
        }

        return shipped;
    }

    /// <summary>Sounds in turn: a shuffled round each (per kind, Halloween's apart), never the same
    /// twice in a row.</summary>
    private static int Turn(bool fanfare, bool halloween, int count)
    {
        if (!Turns.TryGetValue((fanfare, halloween, count), out Combat.PhraseDeck? deck))
        {
            Turns[(fanfare, halloween, count)] = deck = new Combat.PhraseDeck(count);
        }

        return deck.Draw();
    }

    /// <summary>The sad trombone, a whole WAV file: 16-bit mono PCM.</summary>
    internal static byte[] Wav() => _trombone ??= Encode(Melody(new (double, double, bool)[]
    {
        (293.66, 0.32, false), (277.18, 0.32, false), (261.63, 0.32, false), (246.94, 1.10, true),
    }));

    /// <summary>The MVP's fanfare, a whole WAV file: 16-bit mono PCM, about 2.7 s.</summary>
    internal static byte[] Fanfare() => _fanfare ??= Encode(FanfareSamples());

    private static float[] FanfareSamples()
    {
        const double G4 = 392.00, A4 = 440.00, B4 = 493.88, C5 = 523.25, D5 = 587.33, E5 = 659.25, F5 = 698.46, G5 = 783.99, C6 = 1046.50;
        var chords = new (double Start, double Seconds, double[] Notes, bool Held)[]
        {
            (0.00, 0.10, new[] { G4 }, false),
            (0.11, 0.10, new[] { G4 }, false),
            (0.22, 0.10, new[] { G4 }, false),
            (0.33, 0.42, new[] { C5, E5, G5 }, false),
            (0.78, 0.21, new[] { A4, C5, F5 }, false),
            (1.01, 0.21, new[] { B4, D5, G5 }, false),
            (1.24, 1.20, new[] { C5, E5, G5, C6 }, true),
        };
        double[] drums = { 0.33, 0.78, 1.01, 1.24 };
        var mix = new float[(int)(2.75 * SampleRate)];
        foreach ((double start, double seconds, double[] notes, bool held) in chords)
        {
            int from = (int)(start * SampleRate), count = (int)(seconds * SampleRate);
            for (int v = 0; v < notes.Length; v++)
            {
                double phase = 0, detune = 1 + 0.002 * (v - 1);
                for (int i = 0; i < count && from + i < mix.Length; i++)
                {
                    double t = (double)i / SampleRate;
                    double vibrato = held ? 1 + 0.008 * Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1, t / 0.3) : 1;
                    phase += 2 * Math.PI * notes[v] * detune * vibrato / SampleRate;
                    double tone = Math.Sin(phase) + 0.6 * Math.Sin(2 * phase) + 0.45 * Math.Sin(3 * phase)
                        + 0.25 * Math.Sin(4 * phase) + 0.12 * Math.Sin(5 * phase);
                    double attack = Math.Min(1, t / 0.02);
                    double release = Math.Min(1, (seconds - t) / (held ? 0.45 : 0.05));
                    double swell = held ? 0.85 + 0.15 * Math.Min(1, t / 0.4) : 1;
                    mix[from + i] += (float)(tone * attack * release * swell / notes.Length);
                }
            }
        }

        foreach (double at in drums)
        {
            int from = (int)(at * SampleRate);
            var noise = new Random(from);
            for (int i = 0; i < (int)(0.35 * SampleRate) && from + i < mix.Length; i++)
            {
                double t = (double)i / SampleRate;
                double thump = Math.Sin(2 * Math.PI * (110 - 50 * Math.Min(1, t / 0.15)) * t) * Math.Exp(-t * 12);
                mix[from + i] += (float)(0.9 * thump + 0.15 * (noise.NextDouble() * 2 - 1) * Math.Exp(-t * 40));
            }
        }

        // A short echo, as in a hall: two taps behind.
        int tap1 = (int)(0.12 * SampleRate), tap2 = (int)(0.24 * SampleRate);
        var withEcho = new float[mix.Length];
        for (int i = 0; i < mix.Length; i++)
        {
            withEcho[i] = mix[i] + (i >= tap1 ? 0.25f * mix[i - tap1] : 0) + (i >= tap2 ? 0.12f * mix[i - tap2] : 0);
        }

        return withEcho;
    }

    private static float[] Melody((double Hz, double Seconds, bool Wobble)[] notes)
    {
        var samples = new List<float>();
        foreach ((double hz, double seconds, bool wobble) in notes)
        {
            int count = (int)(seconds * SampleRate);
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double frequency = hz * (wobble ? 1 + 0.025 * Math.Sin(2 * Math.PI * 6 * t) : 1);
                phase += 2 * Math.PI * frequency / SampleRate;
                double tone = Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.3 * Math.Sin(3 * phase) + 0.15 * Math.Sin(4 * phase);
                double attack = Math.Min(1, t / 0.03);
                double release = Math.Min(1, (seconds - t) / (wobble ? 0.35 : 0.06));
                samples.Add((float)(tone * attack * release));
            }
        }

        return samples.ToArray();
    }

    /// <summary>Normalised to a comfortable level, as a 16-bit mono PCM WAV file.</summary>
    private static byte[] Encode(float[] samples)
    {
        float peak = samples.Length == 0 ? 1 : samples.Max(s => Math.Abs(s));
        double gain = peak > 0 ? 0.45 / peak : 0;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int dataBytes = samples.Length * 2;
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataBytes);
            writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(dataBytes);
            foreach (float sample in samples)
            {
                writer.Write((short)(sample * gain * short.MaxValue));
            }
        }

        return stream.ToArray();
    }
}
