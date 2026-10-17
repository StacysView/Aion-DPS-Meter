using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The colours the Pets window and the pet panels share: the species badges, and a colour per pet for the
/// dots of the map (the same in the list, so a dot is found in it at a glance).</summary>
internal static class PetLook
{
    /// <summary>A muted badge colour per species, picked to sit with the purple theme.</summary>
    private static readonly Dictionary<string, Brush> Badges = new()
    {
        ["Intellect"] = Frozen(Color.FromRgb(0x5B, 0x7F, 0xD6)),
        ["Feral"] = Frozen(Color.FromRgb(0xC2, 0x6A, 0x5A)),
        ["Nature"] = Frozen(Color.FromRgb(0x4F, 0x9A, 0x74)),
        ["Trans"] = Frozen(Color.FromRgb(0xB5, 0x6F, 0xC9)),
        ["Special"] = Frozen(Color.FromRgb(0xC9, 0x9A, 0x45)),
    };

    private static readonly Brush UnknownBadge = Frozen(Color.FromRgb(0x6B, 0x7A, 0x8A));

    public static Brush BadgeBrush(string? species) =>
        species is not null && Badges.TryGetValue(species, out Brush? b) ? b : UnknownBadge;

    /// <summary>The first letter of a pet's name, for its badge.</summary>
    public static string Initial(string name)
    {
        string trimmed = name.TrimStart('#', ' ');
        return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
    }

    /// <summary>A bright colour of its own per pet id, the same at every start (golden-angle hues). The purples
    /// around the player's arrow (#7B5CF0) are left out, so a dot is never taken for the player.</summary>
    public static Color DotColor(int petId)
    {
        double hue = (petId * 137.508) % 310.0;
        if (hue >= 235)
        {
            hue += 50; // skip 235-285°, the arrow's purple
        }

        return FromHsv(hue, 0.62, 0.98);
    }

    public static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (double r, double g, double b) = (hue % 360) switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
