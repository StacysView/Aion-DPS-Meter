using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// One colour per Aion 2 class, for the share bars and the stand-in icons: players are told apart
/// at a glance, as on the in-game party frames. Picked to stay distinct from each other on the
/// dark themes and the overlay's translucent black.
/// </summary>
public static class ClassColors
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal)
    {
        ["Gladiator"] = Make(0xE8, 0xA1, 0x3A),
        ["Templar"] = Make(0x5D, 0xAD, 0xE2),
        ["Assassin"] = Make(0xE0, 0x5A, 0x5A),
        ["Ranger"] = Make(0x58, 0xD6, 0x8D),
        ["Sorcerer"] = Make(0xAF, 0x7A, 0xC5),
        ["Elementalist"] = Make(0x48, 0xC9, 0xB0),
        ["Cleric"] = Make(0xF4, 0xD0, 0x3F),
        ["Chanter"] = Make(0xF0, 0x8C, 0xC4),
        ["Brawler"] = Make(0x9A, 0xA8, 0xBC),
    };

    private static readonly SolidColorBrush Unknown = Make(0x8A, 0x93, 0x9B);

    public static SolidColorBrush For(string? className) =>
        className is not null && Brushes.TryGetValue(className, out SolidColorBrush? brush) ? brush : Unknown;

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
