using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// The Monsters theme's background music (Stacy, 2026-11-23): every sound file of assets/sounds/music (and of the "music" folder of the meter's data folder) plays one after the
/// other, in a loop, while the theme is on; a button of the overlay pauses or resumes it and another goes to the next one. It gives way to the boss fight's own sounds
/// (<see cref="MomentSound"/>) and comes back after them. On the UI thread.
/// </summary>
internal static class BackgroundMusic
{
    private static readonly string[] Extensions = [".mp3", ".wav", ".wma", ".m4a"];

    private static System.Windows.Media.MediaPlayer? _player;
    private static List<string> _tracks = new();
    private static int _index = -1;
    private static bool _paused;       // the player chose "pause"
    private static bool _gaveWay = true; // a boss sound plays, or another theme is on (true until the theme is known to be Monsters: it never starts under another theme)
    private static bool _running;      // a track is loaded and told to play
    private static int _failures;
    private static double _volume = 0.35;

    static BackgroundMusic()
    {
        // the theme changes (a pick in the settings, the season, the start): the music follows at once, and never plays under another theme
        ThemeManager.Changed += () => GiveWay(!MomentSound.ThemeOn || MomentSound.Playing is not null);
    }

    /// <summary>Raised when the playing state or the list changes (the overlay's button follows it).</summary>
    public static event Action? Changed;

    /// <summary>Whether there is at least one track to play.</summary>
    public static bool HasTracks => _tracks.Count > 0;

    /// <summary>Whether the music is playing now (not paused, not giving way).</summary>
    public static bool IsPlaying => _running && !_paused && !_gaveWay && MomentSound.ThemeOn;

    /// <summary>Whether the player paused it (kept in the settings).</summary>
    public static bool Paused => _paused;

    /// <summary>The track files, in the order they play: the install's folder, then the data folder's, each by name.</summary>
    internal static List<string> FilesIn(string installFolder, string dataFolder) =>
        new[] { Path.Combine(installFolder, "assets", "sounds", "music"), Path.Combine(dataFolder, "music") }
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder).Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Looks for the tracks again and sets the volume and the pause the settings keep.</summary>
    public static void Configure(double volume, bool paused)
    {
        _volume = Math.Clamp(double.IsFinite(volume) ? volume : 0.35, 0, 1);
        _paused = paused;
        List<string> found = FilesIn(AppContext.BaseDirectory, AppDataFolder.Path);
        if (!found.SequenceEqual(_tracks))
        {
            _tracks = found;
            _index = -1;
            _running = false;
            try
            {
                _player?.Stop();
            }
            catch (Exception)
            {
            }
        }

        _gaveWay = !MomentSound.ThemeOn || MomentSound.Playing is not null;
        Apply();
    }

    /// <summary>Another theme is on, or a boss sound plays: the music steps aside; false brings it back.</summary>
    public static void GiveWay(bool away)
    {
        if (_gaveWay != away)
        {
            _gaveWay = away;
            Apply();
        }
    }

    /// <summary>Pause or resume (the button); returns whether it plays now.</summary>
    public static bool Toggle()
    {
        _paused = !_paused;
        Apply();
        return IsPlaying;
    }

    /// <summary>The next track (the button).</summary>
    public static void Next()
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        _paused = false;
        _running = false;
        _index = (_index + 1) % _tracks.Count;
        Apply();
    }

    /// <summary>The volume, 0 to 1 (times the master volume, and softer than the sounds of the boss fights).</summary>
    public static void SetVolume(double volume)
    {
        _volume = Math.Clamp(double.IsFinite(volume) ? volume : 0.35, 0, 1);
        if (_player is not null)
        {
            _player.Volume = Gain();
        }
    }

    private static double Gain()
    {
        double v = SoundMix.Scale(_volume);
        return v * v * 0.35; // a background: softer than the fight sounds' slider scale
    }

    private static void Apply()
    {
        try
        {
            if (_tracks.Count == 0)
            {
                _player?.Stop();
                _running = false;
            }
            else if (_paused || _gaveWay || !MomentSound.ThemeOn)
            {
                _player?.Pause();
            }
            else
            {
                _player ??= CreatePlayer();
                _player.Volume = Gain();
                if (_running)
                {
                    _player.Play();
                }
                else
                {
                    StartTrack();
                }
            }
        }
        catch (Exception)
        {
            // No sound card, a device in use, an unreadable file: nothing to hear, nothing breaks.
        }

        Changed?.Invoke();
    }

    private static System.Windows.Media.MediaPlayer CreatePlayer()
    {
        var player = new System.Windows.Media.MediaPlayer();
        player.MediaEnded += (_, _) =>
        {
            _failures = 0;
            _running = false;
            _index = (_index + 1) % Math.Max(1, _tracks.Count);
            Apply();
        };
        player.MediaFailed += (_, _) =>
        {
            // a file that does not play: the next one, and stop when none of them plays
            if (++_failures > _tracks.Count)
            {
                _running = false;
                _paused = true;
                Changed?.Invoke();
                return;
            }

            _running = false;
            _index = (_index + 1) % Math.Max(1, _tracks.Count);
            Apply();
        };
        return player;
    }

    private static void StartTrack()
    {
        if (_player is null || _tracks.Count == 0)
        {
            return;
        }

        if (_index < 0 || _index >= _tracks.Count)
        {
            _index = 0;
        }

        _player.Open(new Uri(_tracks[_index]));
        _player.Play();
        _running = true;
    }

    /// <summary>The name of the track that plays (without folder or extension), for the button's tip; empty when none.</summary>
    public static string Current => _index >= 0 && _index < _tracks.Count ? Path.GetFileNameWithoutExtension(_tracks[_index]) : "";
}
