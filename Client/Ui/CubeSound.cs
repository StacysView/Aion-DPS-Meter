using System.IO;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The Hidden Cube beep (Stacy, 2026-10-17, "très important"): short sounds made by the meter itself, written once to
/// %TEMP% and played through a few MediaPlayers in turn so a fast beat (0.15 s near a cube) never cuts the previous beep.
/// </summary>
public static class CubeSound
{
    /// <summary>The sounds to choose from, in the settings' order.</summary>
    public static readonly string[] Kinds = { "ping", "double", "sonar", "crystal" };

    private const int SampleRate = 44100;
    private static readonly MediaPlayer[] Players = { new(), new(), new() };
    private static int _next;
    private static readonly Dictionary<string, Uri> Files = new();

    public static void Play(string? kind, double volume)
    {
        if (volume <= 0 || FileFor(kind) is not Uri file)
        {
            return;
        }

        MediaPlayer player = Players[_next++ % Players.Length];
        player.Open(file);
        player.Volume = Math.Clamp(volume, 0, 1);
        player.Play();
    }

    /// <summary>The arrival chime: a cube has just appeared nearby (three rising notes, unlike any radar beep).</summary>
    public static void PlayArrival(double volume)
    {
        // Halloween (20 October - 4 November, like the rest of the theme): a ghost's "Boooo" instead of the three notes (Stacy, 2026-10-20).
        string kind = AionDPS.Combat.LowDpsTease.IsHalloween(DateTime.Now) ? BooKind : ArrivalKind;
        if (volume <= 0 || FileFor(kind) is not Uri file)
        {
            return;
        }

        MediaPlayer player = Players[_next++ % Players.Length];
        player.Open(file);
        player.Volume = Math.Clamp(volume, 0, 1);
        player.Play();
    }

    private const string ArrivalKind = "arrival";
    private const string BooKind = "boo";

    private static Uri? FileFor(string? kind)
    {
        kind = kind is ArrivalKind or BooKind || Kinds.Contains(kind) ? kind! : Kinds[0];
        if (Files.TryGetValue(kind, out Uri? cached))
        {
            return cached;
        }

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "Aion DPS Meter sounds");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"cube_{kind}.wav");
            File.WriteAllBytes(path, Encode(Samples(kind)));
            return Files[kind] = new Uri(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>ping: one bright 1.4 kHz tone; double: two short beeps; sonar: a low swept pulse; crystal: a soft chime (two partials).</summary>
    private static float[] Samples(string kind) => kind switch
    {
        "double" => Concat(Tone(1250, 0.06, 0.004), Silence(0.04), Tone(1250, 0.06, 0.004)),
        "sonar" => Sweep(650, 900, 0.16),
        "crystal" => Mix(Tone(1760, 0.22, 0.05), Tone(2637, 0.22, 0.08), 0.5),
        ArrivalKind => Concat(Tone(1047, 0.11, 0.06), Tone(1319, 0.11, 0.06), Tone(1568, 0.28, 0.12)),
        BooKind => Boo(),
        _ => Tone(1400, 0.09, 0.025),
    };

    private static float[] Tone(double hz, double seconds, double decay)
    {
        int n = (int)(SampleRate * seconds);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double attack = Math.Min(1, t / 0.004);
            s[i] = (float)(Math.Sin(2 * Math.PI * hz * t) * attack * Math.Exp(-t / Math.Max(decay, 0.001)));
        }

        return s;
    }

    private static float[] Sweep(double fromHz, double toHz, double seconds)
    {
        int n = (int)(SampleRate * seconds);
        var s = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double k = i / (double)n;
            phase += 2 * Math.PI * (fromHz + (toHz - fromHz) * k) / SampleRate;
            s[i] = (float)(Math.Sin(phase) * Math.Sin(Math.PI * k));
        }

        return s;
    }

    private static float[] Silence(double seconds) => new float[(int)(SampleRate * seconds)];

    /// <summary>A ghost's "Boooo": a low voice on an "oo" vowel (harmonics shaped by its two formants, ~300 and ~800 Hz), rising a
    /// little then falling, with a slow wobble and a breathy edge; about a second.</summary>
    private static float[] Boo()
    {
        const double seconds = 1.05;
        int n = (int)(SampleRate * seconds);
        var s = new float[n];
        var random = new Random(31); // a fixed seed: the same ghost every time
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double k = t / seconds;
            // pitch: 150 Hz up to 175 then down to 105, with a 5 Hz wobble
            double pitch = (k < 0.25 ? 150 + 100 * k : 175 - 93 * (k - 0.25)) * (1 + 0.025 * Math.Sin(2 * Math.PI * 5 * t));
            phase += 2 * Math.PI * pitch / SampleRate;
            double voice = 0;
            for (int h = 1; h <= 12; h++)
            {
                double f = pitch * h;
                double gain = Math.Exp(-Math.Pow((f - 300) / 120, 2)) + 0.45 * Math.Exp(-Math.Pow((f - 800) / 180, 2)) + 0.05 / h;
                voice += gain * Math.Sin(phase * h);
            }

            double breath = (random.NextDouble() * 2 - 1) * 0.08;
            double envelope = Math.Min(1, t / 0.09) * (k > 0.7 ? Math.Max(0, (1 - k) / 0.3) : 1);
            s[i] = (float)((voice * 0.35 + breath) * envelope);
        }

        // keep it within [-1, 1]
        float peak = s.Max(Math.Abs);
        if (peak > 0.95f)
        {
            for (int i = 0; i < n; i++)
            {
                s[i] *= 0.95f / peak;
            }
        }

        return s;
    }

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static float[] Mix(float[] a, float[] b, double bGain) =>
        Enumerable.Range(0, Math.Max(a.Length, b.Length)).Select(i => (i < a.Length ? a[i] : 0) + (float)((i < b.Length ? b[i] : 0) * bGain)).ToArray();

    /// <summary>16-bit mono PCM WAV, peak at 0.5.</summary>
    private static byte[] Encode(float[] samples)
    {
        float peak = samples.Length == 0 ? 1 : samples.Max(Math.Abs);
        double gain = peak > 0 ? 0.5 / peak : 0;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int data = samples.Length * 2;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + data);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(SampleRate);
            w.Write(SampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8.ToArray());
            w.Write(data);
            foreach (float v in samples)
            {
                w.Write((short)(v * gain * short.MaxValue));
            }
        }

        return stream.ToArray();
    }
}
