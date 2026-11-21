using System.Globalization;
using System.Windows.Data;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The Monsters skin's coin before each player (Stacy, 2026-11-21: "une pastille de type devant chaque joueur, selon sa classe"): the four types of the monster games shared out among the
/// nine classes - fire for the fighters and the fire summoner, water for the defender and the singer, plant for the archer and the healer, electric for the quick and the arcane.
/// </summary>
public sealed class ClassTypeBadgeConverter : IValueConverter
{
    public static string? TypeOf(string? className) => className switch
    {
        "Gladiator" or "Elementalist" or "Brawler" => "feu",
        "Templar" or "Chanter" => "eau",
        "Ranger" or "Cleric" => "plante",
        "Assassin" or "Sorcerer" => "electrique",
        _ => null,
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        TypeOf(value as string) is string type ? SkinState.Current.BadgeOf(type) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
