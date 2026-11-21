using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The Monsters skin's bars (Stacy, 2026-11-21: "les couleurs sont absentes"): a player's bar is green while they bring at least half of the leader's damage, yellow from a fifth
/// to a half, red below - the thresholds of the hit-point bars of the monster games. The value is the share of the leader (0 to 100).
/// </summary>
public sealed class HpBarBrushConverter : IValueConverter
{
    private static readonly Brush Green = Make(0x7B, 0xE8, 0x92, 0x3A, 0xBF, 0x5F);
    private static readonly Brush Yellow = Make(0xFF, 0xE9, 0x7A, 0xF2, 0xC1, 0x1B);
    private static readonly Brush Red = Make(0xFF, 0x8A, 0x7A, 0xE3, 0x4E, 0x3C);

    private static Brush Make(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var brush = new LinearGradientBrush(Color.FromRgb(r1, g1, b1), Color.FromRgb(r2, g2, b2), 90);
        brush.Freeze();
        return brush;
    }

    public static Brush For(double share) => share >= 50 ? Green : share >= 20 ? Yellow : Red;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => For(value is double d ? d : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
