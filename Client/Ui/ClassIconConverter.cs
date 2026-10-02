using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

/// <summary>
/// Class name -> icon, for the places a class name is bound at runtime rather than hardcoded in
/// XAML (the "Your Characters" list and the Players grid's Class column -- static ComboBoxItem
/// lists like ClassFilter/NewCharacterClassBox just reference the icon files directly). User
/// asked for icons instead of text names for classes wherever they appear. Icons are the same
/// assets/classes/icons/*.png files ClassFilter's dropdown items use, copied next to the exe via
/// the csproj's assets/**/*.* CopyToOutputDirectory item.
/// </summary>
public sealed class ClassIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string className || className.Length == 0)
        {
            return null;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", $"{className}.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path, UriKind.Absolute)) : Badge(className);
    }

    private static readonly Dictionary<string, ImageSource> Badges = new(StringComparer.Ordinal);

    /// <summary>
    /// A class without an icon file (Aion 2's Elementalist and Brawler) gets a round badge in its
    /// class colour with its three-letter short form, so no row goes without one.
    /// </summary>
    private static ImageSource Badge(string className)
    {
        lock (Badges)
        {
            if (Badges.TryGetValue(className, out ImageSource? cached))
            {
                return cached;
            }

            string text = AionDPS.Data.ClassCatalog.Abbreviation(className);
            var label = new FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Condensed),
                9, Brushes.White, 1.0);
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                dc.DrawEllipse(ClassColors.For(className), null, new System.Windows.Point(10, 10), 10, 10);
                dc.DrawText(label, new System.Windows.Point(10 - label.Width / 2, 10 - label.Height / 2));
            }

            var image = new DrawingImage(group);
            image.Freeze();
            Badges[className] = image;
            return image;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
