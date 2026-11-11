using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The Big Noob song (Stacy, 2026-11-09): sung by a music generator for the player with the most deaths in the Ludra run (assets/sounds/big_noob.mp3; a file of the
/// same name in the meter's data folder replaces it). Played by the table's own button, or by itself for that player (setting RunDeathsSong); one song at a time,
/// stopped by the button and when the table closes. Called on the UI thread, which the media player needs.
/// </summary>
internal static class RunDeathsSong
{
    private static MediaPlayer? _player;

    /// <summary>Raised when the song starts or stops (the button's label follows it).</summary>
    public static event Action? Changed;

    public static bool IsPlaying { get; private set; }

    public static string? FilePath() => TeaseSound.SoundFile("big_noob", AppDataFolder.Path, AppContext.BaseDirectory);

    /// <summary>Off: the song was taken out of the meter (Stacy, 2026-11-11): no button, never played by itself.</summary>
    public static bool Available => false;

    public static void Play(double volume)
    {
        try
        {
            if (FilePath() is not string path)
            {
                return;
            }

            if (_player is null)
            {
                _player = new MediaPlayer();
                _player.MediaEnded += (_, _) => SetPlaying(false);
                _player.MediaFailed += (_, _) => SetPlaying(false);
            }

            _player.Volume = Math.Clamp(double.IsFinite(volume) ? volume : 0.5, 0, 1);
            _player.Open(new Uri(path));
            _player.Play();
            SetPlaying(true);
        }
        catch (Exception)
        {
            // No sound card, a device in use, an unreadable file: the table shows all the same.
            SetPlaying(false);
        }
    }

    public static void Stop()
    {
        try
        {
            _player?.Stop();
            _player?.Close();
        }
        catch (Exception)
        {
            // Nothing to stop.
        }

        SetPlaying(false);
    }

    private static void SetPlaying(bool playing)
    {
        if (IsPlaying != playing)
        {
            IsPlaying = playing;
            Changed?.Invoke();
        }
    }
}
