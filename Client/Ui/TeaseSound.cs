using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// The boss-death banner's sounds, made here as WAVs in memory - no file to ship, nothing downloaded,
/// a brass-like tone from a few harmonics. The sad trombone ("wah wah wah waaah"): four notes falling
/// a semitone each, the last one held with a wobble. The fanfare: a rising major arpeggio and its
/// octave, held. Each meter plays the one about its own player: the fanfare to the MVP, the trombone
/// to the teased and to everyone else.
/// </summary>
internal static class TeaseSound
{
    private const int SampleRate = 22050;
    private static byte[]? _trombone, _fanfare;
    private static System.Media.SoundPlayer? _trombonePlayer, _fanfarePlayer;

    /// <summary>Plays a sound without waiting for it (a failing audio device is ignored).</summary>
    public static void Play(bool fanfare)
    {
        try
        {
            var player = fanfare
                ? _fanfarePlayer ??= new System.Media.SoundPlayer(new MemoryStream(Fanfare()))
                : _trombonePlayer ??= new System.Media.SoundPlayer(new MemoryStream(Wav()));
            player.Play();
        }
        catch (Exception)
        {
            // No sound card, a device in use: the banner shows all the same.
        }
    }

    /// <summary>The sad trombone, a whole WAV file: 16-bit mono PCM.</summary>
    internal static byte[] Wav() => _trombone ??= Build(new (double, double, bool)[]
    {
        (293.66, 0.32, false), (277.18, 0.32, false), (261.63, 0.32, false), (246.94, 1.10, true),
    });

    /// <summary>The MVP's fanfare: C, E, G, then the high C held with a wobble.</summary>
    internal static byte[] Fanfare() => _fanfare ??= Build(new (double, double, bool)[]
    {
        (523.25, 0.13, false), (659.25, 0.13, false), (783.99, 0.13, false), (1046.50, 0.75, true),
    });

    private static byte[] Build((double Hz, double Seconds, bool Wobble)[] notes)
    {
        var samples = new List<short>();
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
                samples.Add((short)(tone / 1.95 * attack * release * 0.35 * short.MaxValue));
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int dataBytes = samples.Count * 2;
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
            foreach (short sample in samples)
            {
                writer.Write(sample);
            }
        }

        return stream.ToArray();
    }
}
