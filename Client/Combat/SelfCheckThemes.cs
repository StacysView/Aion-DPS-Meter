using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// Every theme must define every token: a missing Brush.X does not throw at runtime, it silently
/// renders that element with no brush at all - a control that vanishes only when one particular
/// theme is picked is exactly the bug a checklist catches and a screenshot does not.
/// </summary>
public static class SelfCheckThemes
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Theme dictionaries (every theme defines every token):");
        bool ok = true;
        foreach (string theme in ThemeManager.Themes)
        {
            IReadOnlyList<string> missing;
            try
            {
                missing = ThemeManager.MissingTokens(theme);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  -> {theme}: failed to load ({ex.GetType().Name}: {ex.Message})");
                ok = false;
                continue;
            }

            bool complete = missing.Count == 0;
            Console.WriteLine($"  -> {theme} complete: {complete}{(complete ? "" : " (missing: " + string.Join(", ", missing) + ")")}");
            ok &= complete;
        }

        bool fallback = ThemeManager.Normalize("no-such-theme") == ThemeManager.DefaultTheme && ThemeManager.Normalize("midnight") == "Midnight"
            && ThemeManager.Normalize("Light") == ThemeManager.DefaultTheme; // Skeeve's Light is not offered here
        bool sizes = ThemeManager.FontSizeFor("Small") < ThemeManager.FontSizeFor("Medium") && ThemeManager.FontSizeFor("Medium") < ThemeManager.FontSizeFor("Large");
        Console.WriteLine($"  -> unknown theme name falls back to {ThemeManager.DefaultTheme}, names are case-insensitive: {fallback}");
        Console.WriteLine($"  -> font sizes ordered Small < Medium < Large: {sizes}");

        // Every text size the windows read (Fs.N) exists and grows with the chosen size; Medium = as written.
        var small = new System.Windows.ResourceDictionary();
        var medium = new System.Windows.ResourceDictionary();
        var large = new System.Windows.ResourceDictionary();
        ThemeManager.AddFontSizes(small, ThemeManager.FontSizeFor("Small"));
        ThemeManager.AddFontSizes(medium, ThemeManager.FontSizeFor("Medium"));
        ThemeManager.AddFontSizes(large, ThemeManager.FontSizeFor("Large"));
        bool scaled = ThemeManager.FontScaleSizes.All(pt =>
            medium[ThemeManager.FontKey(pt)] is double m && Math.Abs(m - pt) < 0.001
            && small[ThemeManager.FontKey(pt)] is double s && s < m
            && large[ThemeManager.FontKey(pt)] is double l && l > m)
            && ThemeManager.FontKey(11.5) == "Fs.11.5";
        Console.WriteLine($"  -> text sizes Fs.N scale with the font size (Medium = as written): {scaled}");

        // The overlay's background takes the theme's colours at the set opacity; Dark = the look of 2026-10-15.
        bool overlay = true;
        foreach (string theme in ThemeManager.Themes)
        {
            var dictionary = ThemeManager.Load(theme);
            var brush = ThemeManager.OverlayBackground(dictionary, 0.6) as System.Windows.Media.LinearGradientBrush;
            var top = (System.Windows.Media.Color)dictionary["Color.SurfaceTop"];
            overlay &= brush is not null && brush.GradientStops[0].Color == System.Windows.Media.Color.FromArgb(153, top.R, top.G, top.B);
        }

        var dark = ThemeManager.Load("Dark");
        overlay &= (System.Windows.Media.Color)dark["Color.SurfaceTop"] == System.Windows.Media.Color.FromRgb(0x14, 0x1C, 0x24)
            && (System.Windows.Media.Color)dark["Color.Brand"] == System.Windows.Media.Color.FromRgb(0x7B, 0x5C, 0xF0);
        Console.WriteLine($"  -> overlay background from the theme's colours at the set opacity, Dark unchanged: {overlay}");

        // Each variant really differs from Dark (a theme that changes nothing is the bug of 2026-10-16).
        bool distinct = ThemeManager.Themes.Where(t => t != "Dark").All(t =>
            !Equals(ThemeManager.Load(t)["Color.Brand"], dark["Color.Brand"]) && !Equals(ThemeManager.Load(t)["Color.SurfaceTop"], dark["Color.SurfaceTop"]));
        Console.WriteLine($"  -> every other theme has its own surfaces and brand colour: {distinct}");
        return ok && fallback && sizes && scaled && overlay && distinct;
    }
}
