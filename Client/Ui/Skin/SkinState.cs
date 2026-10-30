using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The images of the skin in use that the overlay's rows show (Stacy, 2026-10-30): the local player's bar, the snowflakes in it, the glow and the sparkle of the DPS number, the gold line
/// under the total. A single object the row template binds to; <see cref="Active"/> is false when no skin is on, and the rows are drawn as usual.
/// </summary>
public sealed class SkinState : INotifyPropertyChanged
{
    public static SkinState Current { get; } = new();

    private bool _active;
    private Brush? _selfBar;
    private ImageSource? _glow, _sparkle, _underline, _flake1, _flake2, _flake3;
    private double _flake1W, _flake1H, _flake2W, _flake2H, _flake3W, _flake3H, _glowW, _glowH, _sparkleSize, _underlineH;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A skin is on: its images replace the local player's row look.</summary>
    public bool Active { get => _active; private set => Set(ref _active, value); }

    /// <summary>The skin has a bar for the local player.</summary>
    public bool SelfBarOn => _selfBar is not null;

    /// <summary>The local player's bar as a brush (the skin's ligne_joueur), or null.</summary>
    public Brush? SelfBar { get => _selfBar; private set { if (Set(ref _selfBar, value)) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelfBarOn))); } } }

    /// <summary>The tab backgrounds and the mode button's, as brushes (the skin's onglet_ouvert, onglet_ferme, bouton_degats), or null.</summary>
    public Brush? TabOpen { get; private set; }

    public Brush? TabClosed { get; private set; }

    public Brush? ModeButton { get; private set; }

    public ImageSource? Glow { get => _glow; private set => Set(ref _glow, value); }

    public ImageSource? Sparkle { get => _sparkle; private set => Set(ref _sparkle, value); }

    public ImageSource? Underline { get => _underline; private set => Set(ref _underline, value); }

    public ImageSource? Flake1 { get => _flake1; private set => Set(ref _flake1, value); }

    public ImageSource? Flake2 { get => _flake2; private set => Set(ref _flake2, value); }

    public ImageSource? Flake3 { get => _flake3; private set => Set(ref _flake3, value); }

    public double Flake1W { get => _flake1W; private set => Set(ref _flake1W, value); }

    public double Flake1H { get => _flake1H; private set => Set(ref _flake1H, value); }

    public double Flake2W { get => _flake2W; private set => Set(ref _flake2W, value); }

    public double Flake2H { get => _flake2H; private set => Set(ref _flake2H, value); }

    public double Flake3W { get => _flake3W; private set => Set(ref _flake3W, value); }

    public double Flake3H { get => _flake3H; private set => Set(ref _flake3H, value); }

    public double GlowW { get => _glowW; private set => Set(ref _glowW, value); }

    public double GlowH { get => _glowH; private set => Set(ref _glowH, value); }

    public double SparkleSize { get => _sparkleSize; private set => Set(ref _sparkleSize, value); }

    public double UnderlineH { get => _underlineH; private set => Set(ref _underlineH, value); }

    /// <summary>Puts a skin's row images in place (null: none, the rows go back to their usual look).</summary>
    public void Use(SkinPack? skin)
    {
        BitmapSourceOrNull(skin, "ligne_joueur", out ImageSource? bar, out _, out _);
        if (bar is System.Windows.Media.Imaging.BitmapSource barBitmap)
        {
            var brush = new ImageBrush(barBitmap) { Stretch = Stretch.Fill };
            brush.Freeze();
            SelfBar = brush;
        }
        else
        {
            SelfBar = null;
        }

        TabOpen = BrushOf(skin, "onglet_ouvert");
        TabClosed = BrushOf(skin, "onglet_ferme");
        ModeButton = BrushOf(skin, "bouton_degats");
        BitmapSourceOrNull(skin, "lueur_dps", out ImageSource? glow, out double gw, out double gh);
        Glow = glow;
        GlowW = gw;
        GlowH = gh;
        BitmapSourceOrNull(skin, "etincelle", out ImageSource? sparkle, out double sw, out _);
        Sparkle = sparkle;
        SparkleSize = sw;
        BitmapSourceOrNull(skin, "filet_or", out ImageSource? underline, out _, out double uh);
        Underline = underline;
        UnderlineH = uh;
        ImageSource? f1 = null, f2 = null, f3 = null;
        double w1 = 0, h1 = 0, w2 = 0, h2 = 0, w3 = 0, h3 = 0;
        if (skin is not null)
        {
            SkinImage[] flakes = skin.Flakes.ToArray();
            ImageSource? Bitmap(int i) => i < flakes.Length ? skin.Bitmap(flakes[i].File) : null;
            f1 = Bitmap(0); f2 = Bitmap(1); f3 = Bitmap(2);
            if (flakes.Length > 0) { w1 = flakes[0].W; h1 = flakes[0].H; }
            if (flakes.Length > 1) { w2 = flakes[1].W; h2 = flakes[1].H; }
            if (flakes.Length > 2) { w3 = flakes[2].W; h3 = flakes[2].H; }
        }

        Flake1 = f1; Flake1W = w1; Flake1H = h1;
        Flake2 = f2; Flake2W = w2; Flake2H = h2;
        Flake3 = f3; Flake3W = w3; Flake3H = h3;
        Active = skin is not null && (SelfBar is not null || Glow is not null || Sparkle is not null || Underline is not null || Flake1 is not null);
    }

    private static Brush? BrushOf(SkinPack? skin, string name)
    {
        if (skin?.Get(name) is not (SkinImage, System.Windows.Media.Imaging.BitmapSource bitmap))
        {
            return null;
        }

        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private static void BitmapSourceOrNull(SkinPack? skin, string name, out ImageSource? source, out double w, out double h)
    {
        source = null;
        w = h = 0;
        if (skin?.Get(name) is (SkinImage info, System.Windows.Media.Imaging.BitmapSource bitmap))
        {
            source = bitmap;
            w = info.W;
            h = info.H;
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
