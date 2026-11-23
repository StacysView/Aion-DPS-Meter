using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Ui.Skin;

namespace AionDPS.Combat;

/// <summary>
/// The skin engine (Stacy, 2026-10-30; Ui/Skin, Tools/skin-pack/SPEC.md): a skin folder with a skin.json and its images is read, its frame becomes a 3 x 3 grid of the 9 pieces at their own
/// size with the overhang outside the panel, its named images are found, and a missing or broken skin is null (the vector decor then stays).
/// </summary>
public static class SelfCheckSkin
{
    private static void Png(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x40; pixels[i + 1] = 0xA0; pixels[i + 2] = 0xC8; pixels[i + 3] = 0xFF;
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Skins (assets/skins, Tools/skin-pack):");
        string folder = Path.Combine(Path.GetTempPath(), "aiondps-skin-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            // pieces at 3 pixels per point: corners 66 x 54 px (22 x 18 pt), edges 1 px thick in the stretched direction
            Png(Path.Combine(folder, "frame_tl.png"), 66, 54);
            Png(Path.Combine(folder, "frame_tr.png"), 66, 54);
            Png(Path.Combine(folder, "frame_bl.png"), 66, 30);
            Png(Path.Combine(folder, "frame_br.png"), 66, 30);
            Png(Path.Combine(folder, "frame_t.png"), 1, 54);
            Png(Path.Combine(folder, "frame_b.png"), 1, 30);
            Png(Path.Combine(folder, "frame_l.png"), 66, 1);
            Png(Path.Combine(folder, "frame_r.png"), 66, 1);
            Png(Path.Combine(folder, "boule.png"), 36, 43);
            var json = new
            {
                version = 1, id = "test", name = "Test", theme = "Winter",
                frame = new
                {
                    ppp = 1.0 / 3, overhang = new { left = 14.0, top = 12.0, right = 14.0, bottom = 6.0 }, corner = new { w = 22.0, h = 18.0 }, band = 1.5,
                    pieces = new { tl = "frame_tl.png", t = "frame_t.png", tr = "frame_tr.png", l = "frame_l.png", r = "frame_r.png", bl = "frame_bl.png", b = "frame_b.png", br = "frame_br.png" },
                },
                images = new Dictionary<string, object> { ["boule"] = new { file = "boule.png", w = 12.0, h = 14.4 }, ["flocons"] = new[] { new { file = "boule.png", w = 30.0, h = 30.0 } }, ["info"] = new { file = "missing.png", w = 11.0, h = 11.0 } },
            };
            File.WriteAllText(Path.Combine(folder, "skin.json"), JsonSerializer.Serialize(json));

            SkinPack? skin = SkinPack.LoadFrom(folder);
            bool loaded = skin is { Id: "test", Theme: "Winter", Frame: not null } && skin.Flakes.Count == 1 && skin.Get("boule") is not null
                && skin.Get("info") is null; // a listed image whose file is missing is left out, not an error
            Console.WriteLine($"  -> a skin.json and its files are read; an image whose file is missing is left out: {loaded}");

            Grid? frame = skin is null ? null : SkinFrame.Build(skin);
            bool grid = frame is not null && frame.Children.Count == 8 && frame.ColumnDefinitions.Count == 3 && Math.Abs(frame.ColumnDefinitions[0].Width.Value - 22) < 0.01
                && frame.ColumnDefinitions[1].Width.IsStar && Math.Abs(frame.RowDefinitions[0].Height.Value - 18) < 0.01 && Math.Abs(frame.RowDefinitions[2].Height.Value - 10) < 0.01
                && frame.Margin == new Thickness(-14, -12, -14, -6) && !frame.IsHitTestVisible;
            Console.WriteLine($"  -> the frame is a 3 x 3 grid: corners at their own size (22 x 18 pt), stretching edges, the overhang outside the panel, no clicks: {grid}");

            Image? bauble = skin is null ? null : SkinFrame.Picture(skin, "boule");
            bool picture = bauble is not null && Math.Abs(bauble.Width - 12) < 0.01 && Math.Abs(bauble.Height - 14.4) < 0.01 && SkinFrame.Picture(skin!, "nothing") is null;
            Console.WriteLine($"  -> a named image comes at its size, an unknown one is null: {picture}");

            File.WriteAllText(Path.Combine(folder, "skin.json"), "{ not json");
            bool broken = SkinPack.LoadFrom(folder) is null && SkinPack.LoadFrom(Path.Combine(folder, "none")) is null && SkinPack.Load("no-such-skin") is null;
            Console.WriteLine($"  -> a broken skin.json, a missing folder, a skin that is not there: nothing (the vector decor stays): {broken}");
            bool ids = SkinPack.IdFor("Winter") == "hiver" && SkinPack.IdFor("Dark") is null && SkinPack.IdFor("Monsters") == "monstres";
            Console.WriteLine($"  -> the Winter theme is dressed by the skin 'hiver': {ids}");

            // The shipped Monsters skin (Stacy, 2026-11-21): its frame in 8 pieces and the capsule, the tabs, the button, the star and the bar are all there; and its sounds
            // play with that theme only.
            SkinPack? monsters = SkinPack.Load("monstres");
            bool monstersSkin = monsters is { Theme: "Monsters", Frame: not null } && monsters.Frame!.Pieces.Count == 8
                && new[] { "boule", "bouton_degats", "etincelle", "onglet_ferme", "onglet_ouvert" }.All(name => monsters.Get(name) is not null);
            string before = Ui.ThemeManager.CurrentTheme;
            try
            {
                Ui.ThemeManager.CurrentTheme = "Winter";
                bool silent = !Ui.MomentSound.ThemeOn;
                Ui.ThemeManager.CurrentTheme = "Monsters";
                monstersSkin &= silent && Ui.MomentSound.ThemeOn;
            }
            finally
            {
                Ui.ThemeManager.CurrentTheme = before;
            }

            monstersSkin &= Ui.MomentSound.FileFor(Ui.MomentSound.LudraTheme, Path.Combine(folder, "none"), AppContext.BaseDirectory) is not null
                && Ui.MomentSound.FileFor(Ui.MomentSound.BossStart, Path.Combine(folder, "none"), AppContext.BaseDirectory) is not null
                && Ui.MomentSound.FileFor(Ui.MomentSound.CubeOpen, Path.Combine(folder, "none"), AppContext.BaseDirectory) is not null;
            // the extras of the dex frame: the four creatures on its edge and the four coins in its band, three blinking lights, the hit-point bars, the coin of each class
            monstersSkin &= monsters is { HpBars: true } && monsters.Decos.Count == 8 && monsters.Decos.Count(d => d.Bob > 0) == 4 && monsters.Frame!.Lights.Count == 3 && monsters.Frame.PixelArt
                && Ui.Skin.ClassTypeBadgeConverter.TypeOf("Elementalist") == "feu" && Ui.Skin.ClassTypeBadgeConverter.TypeOf("Templar") == "eau"
                && Ui.Skin.ClassTypeBadgeConverter.TypeOf("Cleric") == "plante" && Ui.Skin.ClassTypeBadgeConverter.TypeOf("Assassin") == "electrique"
                && Ui.Skin.ClassTypeBadgeConverter.TypeOf("Nobody") is null
                && !ReferenceEquals(Ui.Skin.HpBarBrushConverter.For(100), Ui.Skin.HpBarBrushConverter.For(30)) && !ReferenceEquals(Ui.Skin.HpBarBrushConverter.For(30), Ui.Skin.HpBarBrushConverter.For(5))
                && ReferenceEquals(Ui.Skin.HpBarBrushConverter.For(50), Ui.Skin.HpBarBrushConverter.For(99));
            // The background music: every sound file of the two music folders, by name, the install's before the data folder's; other files and absent folders are skipped; the
            // named monsters of the open world are no boss fight.
            string root = Path.Combine(Path.GetTempPath(), "aiondps-music-" + Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "install"), data = Path.Combine(root, "data");
            Directory.CreateDirectory(Path.Combine(install, "assets", "sounds", "music"));
            Directory.CreateDirectory(Path.Combine(data, "music"));
            File.WriteAllText(Path.Combine(install, "assets", "sounds", "music", "b.mp3"), "x");
            File.WriteAllText(Path.Combine(install, "assets", "sounds", "music", "a.WAV"), "x");
            File.WriteAllText(Path.Combine(install, "assets", "sounds", "music", "README.md"), "x");
            File.WriteAllText(Path.Combine(data, "music", "c.mp3"), "x");
            List<string> tracks = Ui.BackgroundMusic.FilesIn(install, data);
            bool music = tracks.Select(Path.GetFileName).SequenceEqual(new[] { "a.WAV", "b.mp3", "c.mp3" }) && Ui.BackgroundMusic.FilesIn(Path.Combine(root, "nothing"), Path.Combine(root, "nothing")).Count == 0
                && Ui.MomentSound.IsFieldBoss("Verteron") && Ui.MomentSound.IsFieldBoss("Abyss") && !Ui.MomentSound.IsFieldBoss("Chalice of Muspel") && !Ui.MomentSound.IsFieldBoss(null);
            Directory.Delete(root, true);
            monstersSkin &= music;
            Console.WriteLine($"  -> the Monsters skin is complete, and its three sounds are there and play with that theme only: {monstersSkin} (background music files: {music})");
            return loaded && grid && picture && broken && ids && monstersSkin;
        }
        finally
        {
            SkinPack.ClearCache();
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
