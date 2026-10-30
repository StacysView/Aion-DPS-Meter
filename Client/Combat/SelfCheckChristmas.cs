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
            // The Christmas theme (its design to come) is not picked by the dates for now: here it is picked by hand.
            string pickedBefore = ThemeManager.Mode;
            ThemeManager.Mode = "Christmas";
            bool turns = Sound(true, dec25, 0) == Path.Combine(christmas, "mvp_a.wav") && Sound(true, dec25, 1) == Path.Combine(christmas, "mvp_b.wav")
                && Sound(false, dec25, 0) == Path.Combine(christmas, "taquin_a.wav");
            bool outOfSeason = Sound(false, dec25, 0, on: false) is null;
            ThemeManager.Mode = "Auto";
            outOfSeason &= Sound(true, jan7, 0) is null && Sound(true, dec25, 0) is null;
            ThemeManager.Mode = "Christmas";
            File.WriteAllBytes(Path.Combine(data, "fanfare.mp3"), new byte[] { 1 });
            bool ownFirst = string.Equals(Sound(true, dec25, 0), Path.Combine(data, "fanfare.mp3"), StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  -> in season the Christmas sounds take turns, a player's own first; out of season or with the setting off, none: {turns && outOfSeason && ownFirst}");

            // A theme is complete (Stacy, 2026-10-29): picked by hand in July, the Christmas theme brings its sounds, the Halloween theme its own.
            File.Delete(Path.Combine(data, "fanfare.mp3"));
            string halloweenFolder = Path.Combine(sounds, "halloween");
            Directory.CreateDirectory(halloweenFolder);
            File.WriteAllBytes(Path.Combine(halloweenFolder, "mvp_h.wav"), new byte[] { 1 });
            var july14 = new DateTime(2027, 7, 14, 20, 0, 0);
            ThemeManager.Mode = "Auto";
            bool none = Sound(true, july14, 0) is null;
            string chosen = ThemeManager.Mode;
            ThemeManager.Mode = "Christmas";
            bool byChristmasTheme = Sound(true, july14, 0) == Path.Combine(christmas, "mvp_a.wav") && ThemeManager.ChristmasOn(july14) && !ThemeManager.HalloweenOn(july14);
            ThemeManager.Mode = "Halloween";
            bool byHalloweenTheme = Sound(true, july14, 0) == Path.Combine(halloweenFolder, "mvp_h.wav") && ThemeManager.HalloweenOn(july14) && !ThemeManager.ChristmasOn(july14);
            // In Halloween's dates the theme picked wins: with the Christmas theme the Halloween sounds stay quiet (Stacy, 2026-10-29).
            var oct29 = new DateTime(2026, 10, 29, 20, 0, 0);
            ThemeManager.Mode = "Christmas";
            bool pickedWins = ThemeManager.ChristmasOn(oct29) && !ThemeManager.HalloweenOn(oct29);
            ThemeManager.Mode = "Dark";
            pickedWins &= !ThemeManager.HalloweenOn(oct29) && !ThemeManager.ChristmasOn(oct29); // the default theme picked again: nothing seasonal, whatever the date
            ThemeManager.Mode = "Auto";
            pickedWins &= ThemeManager.HalloweenOn(oct29) && !ThemeManager.ChristmasOn(oct29) && !ThemeManager.HalloweenOn(july14);
            Console.WriteLine($"  -> on 29 October a theme picked wins over Halloween's dates, the default one included; Auto follows the dates: {pickedWins}");
            turns &= pickedWins;
            ThemeManager.Mode = chosen;
            ThemeManager.Mode = pickedBefore;
            Console.WriteLine($"  -> out of season, the Christmas theme picked brings the Christmas sounds and the Halloween theme the Halloween ones: {none && byChristmasTheme && byHalloweenTheme}");
            turns &= none && byChristmasTheme && byHalloweenTheme;
            File.WriteAllBytes(Path.Combine(data, "fanfare.mp3"), new byte[] { 1 });

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
            bool personal = new[] { "Boulenbouche", "Azaède", "Kayzia", "bobow", "Mahérann" }.All(name => PersonalTeases.ForChristmas(name).Count >= 4
                && PersonalTeases.ForChristmas(name).All(p => p.Contains("{1}") && !p.Contains('—') && string.Format(p, name, "4 200").Contains("4 200")))
                && PersonalTeases.ForChristmas("BOULENBOUCHE").Count == PersonalTeases.ForChristmas("Boulenbouche").Count && PersonalTeases.ForChristmas("Diva").Count == 0;
            Console.WriteLine($"  -> Christmas jokes of their own for the five pseudos of the group (4 or more each, the DPS in each): {personal}");
            string shipped = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "christmas");
            bool fifteen = Directory.Exists(shipped) && new[] { ("cube_", 1), ("mvp_", 7), ("taquin_", 5) }.All(kind =>
            {
                string[] files = Directory.GetFiles(shipped, kind.Item1 + "*.wav");
                return files.Length == kind.Item2 && files.All(f => new FileInfo(f).Length > 60_000 && File.ReadAllBytes(f).AsSpan(0, 4).SequenceEqual("RIFF"u8));
            });
            Console.WriteLine($"  -> the Christmas sounds Stacy chose are shipped (1 for the cube, 7 for the MVP, 5 for the teased, real WAVs): {fifteen}");
            bool setting = new MeterSettings().ChristmasSounds;
            Console.WriteLine($"  -> on by default: {setting}");
            return turns && outOfSeason && ownFirst && phrases && personal && fifteen && setting;
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
