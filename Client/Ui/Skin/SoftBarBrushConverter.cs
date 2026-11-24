using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The Monsters screen's bars (Stacy, 2026-11-24: "les couleurs sont un peu saturées, flashy"): the class gradient with its colours calmed down - a part of the colour is traded for grey and
/// the whole is a little darker - so the bars stay recognisable by class without shouting on the dark screens.
/// </summary>
public sealed class SoftBarBrushConverter : IValueConverter
{
    /// <summary>How much of the colour is traded for grey (0 none, 1 all).</summary>
    public const double Grey = 0.28;

    /// <summary>How much of the brightness is kept.</summary>
    public const double Keep = 0.74;

    private static readonly Dictionary<Brush, Brush> Cache = new();

    public static Color Soften(Color c)
    {
        double lum = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
        byte Channel(byte v) => (byte)Math.Clamp((v + (lum - v) * Grey) * Keep, 0, 255);
        return Color.FromArgb(c.A, Channel(c.R), Channel(c.G), Channel(c.B));
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Brush brush)
        {
            return value;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(brush, out Brush? done))
            {
                return done;
            }

            Brush result = brush;
            if (brush is SolidColorBrush solid)
            {
                result = new SolidColorBrush(Soften(solid.Color));
            }
            else if (brush is LinearGradientBrush gradient)
            {
                var soft = new LinearGradientBrush { StartPoint = gradient.StartPoint, EndPoint = gradient.EndPoint, MappingMode = gradient.MappingMode, Opacity = gradient.Opacity };
                foreach (GradientStop stop in gradient.GradientStops)
                {
                    soft.GradientStops.Add(new GradientStop(Soften(stop.Color), stop.Offset));
                }

                result = soft;
            }

            if (result.CanFreeze)
            {
                result.Freeze();
            }

            Cache[brush] = result;
            return result;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
