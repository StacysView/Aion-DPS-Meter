using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// The Monsters theme's battle music (Stacy, 2026-11-23): the files of assets/sounds/battle (and the "battle" folder of the meter's data folder) play one after the other, in a loop and
/// from a track picked at random, during the fight with the last boss of a dungeon (the third one). It stops when that boss falls or the fight is over; the background music gives way
/// to it. On the UI thread.
/// </summary>
internal static class BattleMusic
{
    private static readonly string[] Extensions = [".mp3", ".wav", ".wma", ".m4a"];

    private static System.Windows.Media.MediaPlayer? _player;
    private static List<string> _tracks = new();
    private static int _index;
    private static int _failures;

    /// <summary>The track files, by name: the install's folder, then the data folder's.</summary>
    internal static List<string> FilesIn(string installFolder, string dataFolder) =>
        new[] { Path.Combine(installFolder, "assets", "sounds", "battle"), Path.Combine(dataFolder, "battle") }
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder).Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Whether a track plays now.</summary>
    public static bool Playing { get; private set; }

    public static bool HasTracks => FilesIn(AppContext.BaseDirectory, AppDataFolder.Path).Count > 0;

    /// <summary>Starts at a random track (nothing when another theme is on or there is no file).</summary>
    public static void Start(double volume)
    {
        if (!MomentSound.ThemeOn || Playing)
        {
            return;
        }

        try
        {
            _tracks = FilesIn(AppContext.BaseDirectory, AppDataFolder.Path);
            if (_tracks.Count == 0)
            {
                return;
            }

            _index = Random.Shared.Next(_tracks.Count);
            _failures = 0;
            _player ??= CreatePlayer();
            _player.Volume = Gain(volume);
            Playing = true;
            Open();
        }
        catch (Exception)
        {
            Playing = false; // no sound card, a file that cannot be read: nothing to hear, nothing breaks
        }
    }

    public static void Stop()
    {
        if (!Playing)
        {
            return;
        }

        Playing = false;
        try
        {
            _player?.Stop();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>A fight's music: louder than the background music, softer than the files' own level.</summary>
    internal static double Gain(double volume)
    {
        double v = SoundMix.Scale(volume);
        return v * v * 0.35;
    }

    private static void Open()
    {
        if (_player is null || _tracks.Count == 0)
        {
            Playing = false;
            return;
        }

        _player.Open(new Uri(_tracks[_index % _tracks.Count]));
        _player.Play();
    }

    private static System.Windows.Media.MediaPlayer CreatePlayer()
    {
        var player = new System.Windows.Media.MediaPlayer();
        player.MediaEnded += (_, _) =>
        {
            if (Playing)
            {
                _failures = 0;
                _index = (_index + 1) % Math.Max(1, _tracks.Count);
                Open();
            }
        };
        player.MediaFailed += (_, _) =>
        {
            if (Playing && ++_failures <= _tracks.Count)
            {
                _index = (_index + 1) % Math.Max(1, _tracks.Count);
                Open();
            }
            else
            {
                Playing = false;
            }
        };
        return player;
    }
}
