using System.Speech.Synthesis;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// Alerts of the new raid (Stacy, 2026-11-27): the boss's windup names the mechanic, a text in the middle of the screen and a voice say it
/// while the boss is still winding up (never a word in the game chat). The skill ids come from two captures where Stacy said each mechanic's
/// name aloud (voice markers) after it happened: the word fell 3 s after the start of the same windup each time (drop near 1803770 twice,
/// bomb roll 1803860 twice, no look 1805730 twice); flaque 1803820 and grab 1803725 once each in this order of the cycle (drop near, flaque,
/// grab); chains, the thrown bomb and the waves are not known yet.
/// </summary>
public static class RaidAlerts
{
    /// <summary>The mechanic's key for a boss windup, null when the skill is not one of the raid's mechanics.</summary>
    public static string? KeyOf(int skillId) => skillId switch
    {
        1803770 => "dropnear",
        1803820 => "puddle",
        1803725 => "grab",
        1803860 => "bombroll",
        1805730 => "nolook",
        _ => null,
    };

    /// <summary>What is shown and said, by language (fr, en, de).</summary>
    public static string TextOf(string key, string language)
    {
        int col = language == "fr" ? 0 : language == "de" ? 2 : 1;
        string[] row = key switch
        {
            "dropnear" => ["DRAUPNIR", "DRAUPNIR", "DRAUPNIR"],
            "puddle" => ["FLAQUE", "PUDDLE", "PFÜTZE"],
            "grab" => ["GRAB", "GRAB", "GRAB"],
            "bombroll" => ["BOMB ROLL", "BOMB ROLL", "BOMB ROLL"],
            _ => ["NO LOOK", "NO LOOK", "NO LOOK"],
        };
        return row[col];
    }

    private static Color ColorOf(string key) => key switch
    {
        "dropnear" => Color.FromRgb(0x7F, 0xD8, 0xFF),
        "puddle" => Color.FromRgb(0xFF, 0xB3, 0x47),
        "grab" => Color.FromRgb(0xFF, 0x5B, 0x6E),
        "bombroll" => Color.FromRgb(0xFF, 0x8A, 0x3D),
        _ => Color.FromRgb(0x6C, 0xA8, 0xFF),
    };

    private static SpeechSynthesizer? _voice;
    private static DateTime _lastShown = DateTime.MinValue;

    /// <summary>Shows (and says, when <paramref name="voice"/>) the alert of <paramref name="key"/>. Called on the UI thread.</summary>
    public static void Fire(string key, string language, bool screen, bool voice, double volume)
    {
        // a mechanic already shown a second ago is not shown again (a windup frame can come twice)
        if ((DateTime.UtcNow - _lastShown).TotalMilliseconds < 800)
        {
            return;
        }

        _lastShown = DateTime.UtcNow;
        string text = TextOf(key, language);
        if (screen)
        {
            ScreenAnnounce.Show(text, ColorOf(key), 64, 0, 1.6);
        }

        if (voice)
        {
            if (!Play(key, volume))
            {
                Say(text.ToLowerInvariant(), language, volume);
            }
        }
    }

    private static System.Windows.Media.MediaPlayer? _player;

    /// <summary>The recorded voice of the alert (assets/sounds/raid/KEY.mp3, made with a neural voice once and for all: no internet at the raid, the English words said in English). False when the file is not there.</summary>
    private static bool Play(string key, double volume)
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "raid", key + ".mp3");
            if (!System.IO.File.Exists(path))
            {
                return false;
            }

            _player ??= new System.Windows.Media.MediaPlayer();
            _player.Stop();
            _player.Volume = Math.Clamp(volume * 1.2, 0.2, 1);
            _player.Open(new Uri(path));
            _player.Play();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException or System.IO.IOException)
        {
            return false;
        }
    }

    private static void Say(string text, string language, double volume)
    {
        try
        {
            _voice ??= new SpeechSynthesizer();
            _voice.SpeakAsyncCancelAll();
            _voice.Volume = (int)Math.Clamp(volume * 100, 0, 100);
            _voice.Rate = 2;
            _voice.SpeakAsync(text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.IO.IOException)
        {
            // no voice installed: the text on the screen is enough
        }
    }
}
