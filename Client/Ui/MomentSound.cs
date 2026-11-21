using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// Stacy's own sounds at three moments (2026-11-21): the start of a boss fight (boss_start), Eternal Ludra's fight as a music in a loop (ludra_theme),
/// and the end chest of the dungeon (cube_open). Files of assets/sounds/moments; a file of the same name in the meter's data folder wins, mp3 before
/// wav. The boss start and the theme share one player: the theme replaces the start, a start does not cut the theme. On the UI thread.
/// </summary>
internal static class MomentSound
{
    public const string BossStart = "boss_start", LudraTheme = "ludra_theme", CubeOpen = "cube_open";

    /// <summary>The theme these sounds belong to (Stacy, 2026-11-21: "QUE dans le thème Pokémon"): with any other theme they never play.</summary>
    public const string Theme = "Monsters";

    /// <summary>Whether the theme drawn now is the one of these sounds.</summary>
    public static bool ThemeOn => string.Equals(ThemeManager.CurrentTheme, Theme, StringComparison.OrdinalIgnoreCase);

    private static System.Windows.Media.MediaPlayer? _music, _effect;
    private static string? _playing;
    private static System.Windows.Threading.DispatcherTimer? _cut;

    /// <summary>What the music player plays now (<see cref="BossStart"/>, <see cref="LudraTheme"/>), null when quiet.</summary>
    public static string? Playing => _playing;

    internal static string? FileFor(string name, string dataFolder, string installFolder) =>
        new[] { dataFolder, Path.Combine(installFolder, "assets", "sounds", "moments") }
            .SelectMany(folder => new[] { ".mp3", ".wav" }.Select(ext => Path.Combine(folder, name + ext)))
            .FirstOrDefault(File.Exists);

    /// <summary>Starts <paramref name="name"/> on the music player (<paramref name="loop"/>: again and again until <see cref="Stop"/>).</summary>
    public static void Start(string name, double volume, bool loop, TimeSpan? cutAfter = null)
    {
        if (!ThemeOn || (_playing == LudraTheme && name != LudraTheme))
        {
            return; // another theme; or Ludra's music is on: a boss start does not cut it
        }

        try
        {
            if (FileFor(name, AppDataFolder.Path, AppContext.BaseDirectory) is not string path)
            {
                return;
            }

            if (_music is null)
            {
                _music = new System.Windows.Media.MediaPlayer();
                _music.MediaEnded += (_, _) =>
                {
                    if (_playing == LudraTheme)
                    {
                        _music.Position = TimeSpan.Zero;
                        _music.Play();
                    }
                    else
                    {
                        _playing = null;
                    }
                };
            }

            _music.Stop();
            _music.Volume = SoundMix.Scale(volume);
            _music.Open(new Uri(path));
            _music.Play();
            _playing = loop ? LudraTheme : name;
            _cut?.Stop();
            if (cutAfter is TimeSpan after && !loop)
            {
                _cut = new System.Windows.Threading.DispatcherTimer { Interval = after };
                _cut.Tick += (_, _) =>
                {
                    _cut?.Stop();
                    Stop(name);
                };
                _cut.Start();
            }
        }
        catch (Exception)
        {
            // No sound card, a device in use, an unreadable file: nothing to hear, nothing breaks.
        }
    }

    /// <summary>Stops the music player when it plays <paramref name="name"/> (any, when null).</summary>
    public static void Stop(string? name = null)
    {
        if (_playing is null || (name is not null && _playing != name))
        {
            return;
        }

        try
        {
            _music?.Stop();
        }
        catch (Exception)
        {
        }

        _playing = null;
    }

    /// <summary>Plays a short sound over the music (the end chest).</summary>
    public static void Effect(string name, double volume)
    {
        if (!ThemeOn)
        {
            return;
        }

        try
        {
            if (FileFor(name, AppDataFolder.Path, AppContext.BaseDirectory) is string path)
            {
                _effect ??= new System.Windows.Media.MediaPlayer();
                _effect.Volume = SoundMix.Scale(volume);
                _effect.Open(new Uri(path));
                _effect.Play();
            }
        }
        catch (Exception)
        {
        }
    }
}
