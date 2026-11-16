using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// The Shugo Festival's reminders, said aloud instead of shown in the middle of the screen (Stacy, 2026-11-16: "un mec qui crie Shugo, en mode japonais énervé").
/// Two files of assets/sounds/shugo, so that a recording can replace either by the same name: shugo_5min (5 minutes before) and shugo_open (it opens).
/// A file of the same name in the meter's data folder (see <see cref="AppDataFolder"/>) wins; mp3 before wav.
/// </summary>
internal static class ShugoSound
{
    private static System.Windows.Media.MediaPlayer? _player;

    /// <summary>The file to play for a reminder, or null when there is none.</summary>
    internal static string? FileFor(bool open, string dataFolder, string installFolder)
    {
        string name = open ? "shugo_open" : "shugo_5min";
        return new[] { dataFolder, Path.Combine(installFolder, "assets", "sounds", "shugo") }
            .SelectMany(folder => new[] { ".mp3", ".wav" }.Select(ext => Path.Combine(folder, name + ext)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Plays a reminder at <paramref name="volume"/> (0 to 1); a missing file or a failing audio device is ignored. On the UI thread, like the media player.</summary>
    public static void Play(bool open, double volume)
    {
        try
        {
            if (FileFor(open, AppDataFolder.Path, AppContext.BaseDirectory) is string path)
            {
                _player ??= new System.Windows.Media.MediaPlayer();
                _player.Volume = SoundMix.Scale(volume);
                _player.Open(new Uri(path));
                _player.Play();
            }
        }
        catch (Exception)
        {
            // No sound card, a device in use, an unreadable file: the footer's countdown is still there.
        }
    }
}
