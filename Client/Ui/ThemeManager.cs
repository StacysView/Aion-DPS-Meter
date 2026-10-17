using System.Windows;

namespace AionDPS.Ui;

/// <summary>
/// Swaps the color theme and base font size at runtime. Every window's XAML refers to
/// {DynamicResource Brush.X}/Color.X/FontSize.Base rather than literal colors, and those keys live
/// in one merged dictionary per theme (Ui/Themes/*.xaml) - replacing that dictionary repaints
/// every open window, including the click-through overlay chips. Program.cs applies the saved
/// choice before the first window exists; MainWindow re-applies it when Settings are saved.
/// MeterSettings.Theme/FontSize had a Settings dropdown for a long time without anything reading
/// them - this is what finally does.
/// </summary>
public static class ThemeManager
{
    /// <summary>The themes offered in Settings (2026-10-17): the "modern classic" look and three
    /// variants of it. Skeeve's Light and HighContrast are not kept: the windows lay translucent
    /// white veils over their surfaces, made for a dark background.</summary>
    public static readonly IReadOnlyList<string> Themes = new[] { "Dark", "Midnight", "Elyos", "Asmodian" };

    public const string DefaultTheme = "Dark";

    /// <summary>Every token a theme must define - SelfCheck verifies each dictionary against this list.</summary>
    public static readonly IReadOnlyList<string> Tokens = new[]
    {
        "Window", "TitleBar", "Panel", "RowAlt", "Control", "ControlRaised", "ControlHover", "ControlHoverStrong",
        "Border", "ScrollThumb", "CheckBorder", "CheckBorderHover", "TextDisabled", "TextMuted", "TextSubtle", "Text",
        "Accent", "RowEnemy", "OverlayBg", "OverlayEnemy", "OverlayText", "Warning", "WarningSoft", "Danger", "DangerSoft",
        // the "modern classic" look of the overlay, Settings, spells and history windows
        "SurfaceTop", "SurfaceBottom", "Card", "Frame", "FrameHover", "FrameStrong", "Field", "Divider", "WindowEdge",
        "Button", "ButtonEdge", "Thumb", "SideLine", "Heading", "Body", "BodySoft", "Label", "Muted", "Dim", "Faint",
        "NavText", "Brand", "BrandEdge", "BrandLight", "BrandText", "BrandSelect", "BrandNav", "OnBrand", "OverlayFrame", "ModeSwitch",
    };

    /// <summary>The text sizes written in the windows (Medium = these points). Each is a resource
    /// Fs.N (e.g. "Fs.11.5") the XAML reads with DynamicResource, set to N x base/12 by Apply - the
    /// same idea as Skeeve's AddOverlayFontSizes.</summary>
    public static readonly IReadOnlyList<double> FontScaleSizes = new[] { 9.0, 10.0, 10.5, 11.0, 11.5, 12.0, 13.0, 14.0, 15.0, 19.0, 20.0, 21.0 };

    public static string FontKey(double points) => "Fs." + points.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ResourceDictionary? _active;

    /// <summary>Unknown names (a settings file from a build with different themes) fall back to Dark
    /// rather than leaving every brush unresolved - an unresolved DynamicResource renders as nothing.</summary>
    public static string Normalize(string? theme) =>
        Themes.FirstOrDefault(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)) ?? DefaultTheme;

    public static double FontSizeFor(string? fontSize) => fontSize?.ToLowerInvariant() switch
    {
        "small" => 11.0,
        "large" => 14.0,
        _ => 12.0,
    };

    public static void Apply(Application app, string? theme, string? fontSize)
    {
        ResourceDictionary dictionary = Load(Normalize(theme));
        if (_active is not null)
        {
            app.Resources.MergedDictionaries.Remove(_active);
        }

        app.Resources.MergedDictionaries.Add(dictionary);
        _active = dictionary;
        double size = FontSizeFor(fontSize);
        app.Resources["FontSize.Base"] = size;
        AddFontSizes(app.Resources, size);
    }

    /// <summary>Every Fs.N key, scaled by the base size (12 = the written sizes).</summary>
    public static void AddFontSizes(ResourceDictionary resources, double basePoints)
    {
        foreach (double pt in FontScaleSizes)
        {
            resources[FontKey(pt)] = pt * basePoints / 12.0;
        }
    }

    /// <summary>The overlay's background: the theme's two surface colours, lighter at the top, at the
    /// opacity set in Settings.</summary>
    public static System.Windows.Media.Brush OverlayBackground(ResourceDictionary resources, double opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        System.Windows.Media.Color top = resources["Color.SurfaceTop"] is System.Windows.Media.Color t ? t : System.Windows.Media.Color.FromRgb(0x14, 0x1C, 0x24);
        System.Windows.Media.Color bottom = resources["Color.SurfaceBottom"] is System.Windows.Media.Color b ? b : System.Windows.Media.Color.FromRgb(0x0B, 0x10, 0x16);
        var brush = new System.Windows.Media.LinearGradientBrush(
            System.Windows.Media.Color.FromArgb(alpha, top.R, top.G, top.B), System.Windows.Media.Color.FromArgb(alpha, bottom.R, bottom.G, bottom.B), 90);
        brush.Freeze();
        return brush;
    }

    /// <summary>Loads a theme's compiled dictionary. Relative component URI, not a pack URI with the
    /// assembly name: the assembly is called "Aion DPS", and a space inside a pack authority is
    /// one more thing that can go wrong for no gain.</summary>
    public static ResourceDictionary Load(string theme) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Ui/Themes/{Normalize(theme)}.xaml", UriKind.Relative));

    /// <summary>Tokens a theme leaves undefined - empty for a complete theme.</summary>
    public static IReadOnlyList<string> MissingTokens(string theme)
    {
        ResourceDictionary dictionary = Load(theme);
        return Tokens
            .SelectMany(t => new[] { $"Color.{t}", $"Brush.{t}" })
            .Where(key => !dictionary.Contains(key))
            .ToList();
    }
}
