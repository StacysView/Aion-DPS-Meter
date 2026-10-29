using System.IO;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The Christmas sounds and phrases of the banner (1 December - 6 January, Stacy 2026-10-29): the sounds of assets/sounds/christmas take turns for the MVP and the teased
/// in season only and only with the setting on, a player's own file still first; the phrases exist in English and French.</summary>
public static class SelfCheckChristmas
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Christmas sounds and phrases:");
        string root = Path.Combine(Path.GetTempPath(), "aiondps-christmas-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data"), install = Path.Combine(root, "install");
        string sounds = Path.Combine(install, "assets", "sounds"), christmas = Path.Combine(sounds, "christmas");
        try
        {
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(christmas);
            foreach (string name in new[] { "mvp_a.wav", "mvp_b.wav", "taquin_a.wav", "cube_a.wav" })
            {
                File.WriteAllBytes(Path.Combine(christmas, name), new byte[] { 1 });
            }

            var dec25 = new DateTime(2026, 12, 25, 20, 0, 0);
            var jan7 = new DateTime(2027, 1, 7, 20, 0, 0);
            string? Sound(bool fanfare, DateTime day, int index, bool on = true) => TeaseSound.SoundFor(fanfare, day, data, install, count => index % count, on);
            bool turns = Sound(true, dec25, 0) == Path.Combine(christmas, "mvp_a.wav") && Sound(true, dec25, 1) == Path.Combine(christmas, "mvp_b.wav")
                && Sound(false, dec25, 0) == Path.Combine(christmas, "taquin_a.wav");
            bool outOfSeason = Sound(true, jan7, 0) is null && Sound(false, dec25, 0, on: false) is null;
            File.WriteAllBytes(Path.Combine(data, "fanfare.mp3"), new byte[] { 1 });
            bool ownFirst = string.Equals(Sound(true, dec25, 0), Path.Combine(data, "fanfare.mp3"), StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  -> in season the Christmas sounds take turns, a player's own first; out of season or with the setting off, none: {turns && outOfSeason && ownFirst}");

            var loc = LocalizationManager.Instance;
            bool phrases = true;
            foreach ((string key, int count) in new[] { ("Tease.Christmas", 10), ("Tease.ChristmasDeaths", 6), ("Tease.ChristmasMvp", 6) })
            {
                for (int i = 1; i <= count; i++)
                {
                    foreach (string language in new[] { "en", "fr" })
                    {
                        string previous = loc.Language;
                        loc.Language = language;
                        string text = loc[$"{key}.{i}"];
                        loc.Language = previous;
                        phrases &= text != $"{key}.{i}" && text.Contains("{0}") && text.Contains("{1}") && !text.Contains('—');
                    }
                }
            }

            Console.WriteLine($"  -> 10 DPS phrases, 6 on deaths and 6 for the MVP, in English and French, each with the name and the number: {phrases}");
            bool setting = new MeterSettings().ChristmasSounds;
            Console.WriteLine($"  -> on by default: {setting}");
            return turns && outOfSeason && ownFirst && phrases && setting;
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
