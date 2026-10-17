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

    /// <summary>A bright colour per rank of the pet in the shown list (golden-angle hues): the pets of one model get
    /// distinct hues, the same on the map and in the list. The purples around the player's arrow (#7B5CF0) are left
    /// out, so a dot is never taken for the player.</summary>
    public static Color DotColor(int rank)
    {
        double hue = (Math.Max(rank, 0) * 137.508) % 310.0;
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

    /// <summary>The souls of the three tiers: obtain (5), level 2 (25), level 3 (75).</summary>
    public static readonly int[] TierSouls = [5, 25, 75];

    /// <summary>How full tier <paramref name="tier"/> (0 = obtain, 1 = level 2, 2 = level 3) is for a pet at
    /// <paramref name="level"/> with <paramref name="souls"/> given since it: the tiers below the level are full, the
    /// current one in part, the later ones empty.</summary>
    public static double TierFill(int level, int souls, int tier)
    {
        if (tier < level)
        {
            return 1;
        }

        if (tier > level || tier >= TierSouls.Length)
        {
            return 0;
        }

        return Math.Clamp((double)souls / TierSouls[tier], 0, 1);
    }

    /// <summary>"Niv. 1 · 12/25", "Niv. 3 · max".</summary>
    public static string ProgressText(int level, int souls)
    {
        var loc = LocalizationManager.Instance;
        return level >= TierSouls.Length
            ? string.Format(loc["Pets.ProgressMax"], level)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["Pets.Progress"], level, souls, TierSouls[Math.Max(level, 0)]);
    }

    /// <summary>The colour a pet's line flashes with when it gains souls or a level.</summary>
    public static readonly Color FlashColor = Color.FromRgb(0x7B, 0x5C, 0xF0);

    /// <summary>A brush that goes from the flash's purple back to <paramref name="rest"/> over what is left of
    /// <see cref="PetFlashes.Length"/> after <paramref name="elapsed"/>: a line rebuilt in the middle of its flash goes
    /// on where it was.</summary>
    public static Brush FlashBrush(Color rest, TimeSpan elapsed)
    {
        double done = Math.Clamp(elapsed.TotalMilliseconds / PetFlashes.Length.TotalMilliseconds, 0, 1);
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        Color flash = Color.FromArgb(0xE0, FlashColor.R, FlashColor.G, FlashColor.B);
        Color from = Color.FromArgb(Mix(flash.A, rest.A, done), Mix(flash.R, rest.R, done), Mix(flash.G, rest.G, done), Mix(flash.B, rest.B, done));
        var brush = new SolidColorBrush(from);
        TimeSpan left = PetFlashes.Length - elapsed;
        if (left > TimeSpan.Zero)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(from, rest, new System.Windows.Duration(left)));
        }

        return brush;
    }

    /// <summary>The thin dark scroll bar of the pet panels (the list with a height of its own).</summary>
    public static System.Windows.Style ThinScrollBar => _thinScrollBar ??= (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar">
            <Setter Property="Width" Value="6"/>
            <Setter Property="MinWidth" Value="6"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" IsDirectionReversed="True" Margin="1,2">
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border Background="#3A4A5A" CornerRadius="2.5"/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        """);

    private static System.Windows.Style? _thinScrollBar;

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

/// <summary>
/// Which pets flash now (their line in the pet list, their card in the Pets window): a pet gaining souls or a level is
/// marked (UI thread), its flash starts at the next rebuild of the view (so the throttled rebuild does not eat the start
/// of it) and lasts <see cref="Length"/>; a rebuild during it goes on from where it was.
/// </summary>
public sealed class PetFlashes
{
    public static readonly TimeSpan Length = TimeSpan.FromMilliseconds(1200);

    private readonly HashSet<int> _pending = new();
    private readonly Dictionary<int, DateTime> _started = new();

    /// <summary>Off: nothing is marked any more (the setting "flash the line of a pet").</summary>
    public bool Enabled { get; set; } = true;

    public void Mark(int petId)
    {
        if (Enabled)
        {
            _pending.Add(petId);
        }
    }

    /// <summary>Starts the marked flashes now; the pet ids started.</summary>
    public IReadOnlyCollection<int> StartPending()
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        DateTime now = DateTime.UtcNow;
        var started = _pending.ToList();
        foreach (int id in started)
        {
            _started[id] = now;
        }

        _pending.Clear();
        return started;
    }

    /// <summary>How long the pet's flash has been running; null when it is not flashing.</summary>
    public TimeSpan? Elapsed(int petId)
    {
        if (!_started.TryGetValue(petId, out DateTime at))
        {
            return null;
        }

        TimeSpan elapsed = DateTime.UtcNow - at;
        if (elapsed >= Length)
        {
            _started.Remove(petId);
            return null;
        }

        return elapsed;
    }

    /// <summary>The background of a flashing pet's line, fading to <paramref name="rest"/>; null when it is not flashing.</summary>
    public Brush? Brush(int petId, Color rest) => Elapsed(petId) is { } elapsed ? PetLook.FlashBrush(rest, elapsed) : null;
}

/// <summary>
/// A pet's progress over its three tiers, 5 | 25 | 75 souls (obtain, level 2, level 3): three segments of the same
/// width, the tiers done full, the current one in part, the later ones empty.
/// </summary>
public sealed class PetTierBar : System.Windows.FrameworkElement
{
    public static readonly System.Windows.DependencyProperty LevelProperty = System.Windows.DependencyProperty.Register(
        nameof(Level), typeof(int), typeof(PetTierBar), new System.Windows.FrameworkPropertyMetadata(0, System.Windows.FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly System.Windows.DependencyProperty SoulsProperty = System.Windows.DependencyProperty.Register(
        nameof(Souls), typeof(int), typeof(PetTierBar), new System.Windows.FrameworkPropertyMetadata(0, System.Windows.FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly System.Windows.DependencyProperty TrackProperty = System.Windows.DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(PetTierBar), new System.Windows.FrameworkPropertyMetadata(PetLook.Frozen(Color.FromRgb(0x2A, 0x38, 0x46)), System.Windows.FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Fill = PetLook.Frozen(PetLook.FlashColor);
    private static readonly Brush DoneFill = PetLook.Frozen(Color.FromRgb(0x5E, 0x47, 0xC0));

    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public int Souls
    {
        get => (int)GetValue(SoulsProperty);
        set => SetValue(SoulsProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size available) => new(0, double.IsNaN(Height) ? 5 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        const double Gap = 3;
        double w = ActualWidth, h = ActualHeight;
        int tiers = PetLook.TierSouls.Length;
        double segment = (w - Gap * (tiers - 1)) / tiers;
        if (!(segment > 0) || !(h > 0))
        {
            return;
        }

        double radius = Math.Min(h / 2, 3);
        for (int tier = 0; tier < tiers; tier++)
        {
            double x = tier * (segment + Gap);
            dc.DrawRoundedRectangle(Track, null, new System.Windows.Rect(x, 0, segment, h), radius, radius);
            double fill = PetLook.TierFill(Level, Souls, tier);
            if (fill > 0)
            {
                // The tiers done are a deeper purple, the one being filled the bright one.
                dc.DrawRoundedRectangle(fill >= 1 && tier < Level ? DoneFill : Fill, null, new System.Windows.Rect(x, 0, Math.Max(segment * fill, Math.Min(segment, h)), h), radius, radius);
            }
        }
    }
}
