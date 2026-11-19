using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pet list over the game: the shown zone's name, then each needed pet of it (fewest souls left first) with its
/// species badge, its name and map colour, its number of spawns on the map and its progress over the three tiers
/// (5 | 25 | 75 souls). A pet gaining souls or a level lights its line up in purple for a moment. Unlocked, its corner
/// grip sets its width and height (then the rows scroll); without a height of its own it shows the 3 pets nearest to the player
/// (their nearest spawn's distance in metres). A click on a line checks its pet (Stacy, 2026-10-17): then the list
/// and the map show only the checked pets, each line with a purple check, and "All" in the header unchecks them all;
/// so the list takes its clicks even when locked (it is small; the map stays click-through). The checks are the pins
/// of the Pets window (<see cref="PetChoices"/>). While the in-game pet shop is open it lists the shop's pets instead
/// (<see cref="ShowShop"/>). Rebuilt on every soul (see <see cref="PetPanelSet"/>). Pet data: SkeeveTV (MIT).
/// </summary>
public sealed class PetListPanel : PetPanelWindow
{
    public const double MinListWidth = 200, MaxListWidth = 700, MinListHeight = 100, MaxListHeight = 1200;
    /// <summary>Without a height of its own the list shows only the pets nearest to the player (Stacy, 2026-10-17: "les 3 plus proches").</summary>
    private const int MaxRows = 3;

    private static readonly Brush BarTrack = PetLook.Frozen(Color.FromRgb(0x2A, 0x38, 0x46));
    private static readonly Brush RowText = PetLook.Frozen(Color.FromRgb(0xE6, 0xEC, 0xF1));
    private static readonly Color RowRest = Color.FromArgb(0, 0x7B, 0x5C, 0xF0);

    private readonly TextBlock _title = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock _count = new() { FontSize = 11, Foreground = MutedText, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    // "All": shown while some pets are checked, unchecks them all.
    private readonly TextBlock _all = new()
    {
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = PetLook.Frozen(Color.FromRgb(0xB8, 0xA6, 0xFF)),
        TextDecorations = TextDecorations.Underline,
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Cursor = System.Windows.Input.Cursors.Hand,
        Visibility = Visibility.Collapsed,
    };
    private readonly StackPanel _rows = new();
    private readonly DockPanel _body;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Dictionary<int, Border> _rowFrames = new();
    private (string? Map, string Zone, bool Unknown, string Language, bool AllRows, bool Filtered)? _lastShown;
    private List<TrackerPet>? _lastPets;
    private TrackerModel? _lastModel;
    private List<ShopRow>? _lastShop;
    private List<Aion2.Aion2ShopSkin> _lastSkins = [];
    private string? _lastShopLanguage;

    public PetListPanel() : base("Aion DPS - pet list")
    {
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 6), LastChildFill = true };
        DockPanel.SetDock(_all, Dock.Right);
        header.Children.Add(_all);
        DockPanel.SetDock(_count, Dock.Right);
        header.Children.Add(_count);
        _all.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // not a drag of the unlocked panel
            ShowAllClicked?.Invoke();
        };
        header.Children.Add(_title);
        DockPanel.SetDock(header, Dock.Top);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _rows,
        };
        scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), PetLook.ThinScrollBar);
        _body = new DockPanel { Width = ListWidth, LastChildFill = true, LayoutTransform = _scale };
        _body.Children.Add(header);
        _body.Children.Add(scroll);
        Body = _body;
    }

    public const double MinScale = 0.7, MaxScale = 2.0;

    /// <summary>How big the whole list is drawn (1 = its usual size).</summary>
    public double Scale => _scale.ScaleX;

    /// <summary>Draws the whole list <paramref name="scale"/> times its usual size, 0.7 to 2.</summary>
    public void SetScale(double scale)
    {
        scale = Math.Round(Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale), 2);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        ApplyBodySize();
    }


    /// <summary>A line was clicked: its pet is checked or unchecked (<see cref="PetPanelSet"/> saves it).</summary>
    public event Action<int>? PetClicked;

    /// <summary>"All" was clicked: every pet is unchecked.</summary>
    public event Action? ShowAllClicked;

    /// <summary>Clicks a line, as the mouse would (for the smoke harness).</summary>
    public void ClickPetForTest(int petId) => PetClicked?.Invoke(petId);

    /// <summary>Clicks "All", as the mouse would (for the smoke harness).</summary>
    public void ClickAllForTest() => ShowAllClicked?.Invoke();

    /// <summary>Whether "All" is shown (some pets are checked; read by the smoke harness).</summary>
    public bool ShowsAll => _all.Visibility == Visibility.Visible;

    protected override bool ClickThroughWhenLocked => false;

    /// <summary>The pet ids of the rows shown (read by the smoke harness).</summary>
    public IReadOnlyList<int> Shown { get; private set; } = [];

    /// <summary>The pet ids whose line is lit up now (read by the smoke harness).</summary>
    public IReadOnlyList<int> Flashing => _rowFrames.Where(p => p.Value.Background is SolidColorBrush { Color.A: > 0 }).Select(p => p.Key).ToList();

    /// <summary>Set by <see cref="PetPanelSet"/>: the map's side, the list's default place being below it.</summary>
    public double MapSizeForDefault { get; set; } = 260;

    /// <summary>The content's width, as set by the grip (or the settings).</summary>
    public double ListWidth { get; private set; } = 210;

    /// <summary>The content's height as set by the grip; null = as tall as its rows.</summary>
    public double? ListHeight { get; private set; }

    public void PlaceAt(double? left, double? top)
    {
        Point place = left is double l && top is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
        Left = place.X;
        Top = place.Y;
    }

    /// <summary>The content's size: a width of 200 to 700, a height of 100 to 1200 or none (as tall as the rows).</summary>
    public void SetSize(double width, double? height)
    {
        ListWidth = Math.Clamp(double.IsFinite(width) ? width : 250, MinListWidth, MaxListWidth);
        ListHeight = height is double h && double.IsFinite(h) ? Math.Clamp(h, MinListHeight, MaxListHeight) : null;
        ApplyBodySize();
    }

    /// <summary>The shop list's width: wide enough for its cards whatever the zone list's own width (Stacy, 2026-11-17: "étire la fenêtre").</summary>
    public const double ShopWidth = 300;

    /// <summary>The zone list takes its own width and height; the shop list is at least <see cref="ShopWidth"/> wide and as tall as its cards, up to
    /// most of the screen (then it scrolls) - not the zone list's short height (Stacy, 2026-11-17: "dans la boutique allonge d'office").</summary>
    private void ApplyBodySize()
    {
        bool shop = _lastShop is not null;
        _body.Width = shop ? Math.Max(ListWidth, ShopWidth) : ListWidth;
        _body.Height = shop ? double.NaN : ListHeight ?? double.NaN;
        _body.MaxHeight = shop ? SystemParameters.WorkArea.Height * 0.85 / Math.Max(Scale, 0.1) : double.PositiveInfinity;
    }

    /// <summary>How many times the rows were rebuilt (read by the smoke harness: a move alone rebuilds nothing).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Shows a model. The rows are rebuilt only when what they show changed (the zone, a pet's level, souls or
    /// spawn points, the language); the pets in <paramref name="started"/> start flashing. It may throw on something
    /// unforeseen: <see cref="PetPanelSet.Refresh"/> catches it.</summary>
    public void ShowModel(TrackerModel model, PetFlashes? flashes = null, IReadOnlyCollection<int>? started = null)
    {
        var loc = LocalizationManager.Instance;
        _lastModel = model;
        if (_lastShop is not null)
        {
            _lastShop = null; // back from the shop: the next shop list is built again
            ApplyBodySize();
        }
        // With a height of its own the list scrolls: every pet is listed, not only the first 10.
        var shownNow = (model.Map?.Key, model.ZoneName, model.ZoneUnknown, loc.Language, ListHeight is not null, model.Filtered);
        if (_lastShown is { } last && last.Equals(shownNow) && _lastPets is { } pets && pets.SequenceEqual(model.Pets))
        {
            foreach (int id in started ?? Array.Empty<int>())
            {
                if (_rowFrames.TryGetValue(id, out Border? frame) && flashes?.Brush(id, RowRest) is { } brush)
                {
                    frame.Background = brush;
                }
            }

            return;
        }

        _lastShown = shownNow;
        _lastPets = model.Pets.ToList();
        Rebuilds++;
        _rows.Children.Clear();
        _rowFrames.Clear();
        _all.Text = loc["PetPanels.All"];
        _all.ToolTip = loc["PetPanels.AllTip"];
        _all.Visibility = model.Filtered ? Visibility.Visible : Visibility.Collapsed;
        if (model.Map is null)
        {
            _title.Text = loc["Pets.Title"];
            _count.Text = "";
            AddNote(loc[model.ZoneUnknown ? "PetPanels.ZoneUnknown" : "PetPanels.NoMap"], RowText);
            AddNote(loc[model.ZoneUnknown ? "PetPanels.ZoneUnknownHint" : "PetPanels.NoMapHint"]);
            Shown = [];
            return;
        }

        _title.Text = model.ZoneName;
        _count.Text = string.Format(loc[model.Pets.Count == 1 ? "Pets.ZonePet" : "Pets.ZonePets"], model.Pets.Count);
        if (model.Pets.Count == 0)
        {
            AddNote(loc["PetPanels.NothingHere"]);
            Shown = [];
            return;
        }

        // Nearest first: the metres from the player to each pet's nearest spawn point on the shown map (unknown last,
        // in the phase order).
        var metres = new Dictionary<int, double>();
        if (model.Player is { } me && model.Map is { } map)
        {
            foreach (var dots in model.Dots.GroupBy(d => d.PetId))
            {
                metres[dots.Key] = dots.Min(d => Math.Sqrt((d.PixelX - me.X) * (d.PixelX - me.X) + (d.PixelY - me.Y) * (d.PixelY - me.Y))) * map.MetresPerPixel;
            }
        }

        int maxRows = ListHeight is null ? MaxRows : int.MaxValue;
        var shown = model.Pets.OrderBy(p => metres.TryGetValue(p.PetId, out double m) ? m : double.MaxValue).Take(maxRows).ToList();
        foreach (TrackerPet pet in shown)
        {
            int rank = IndexOf(model.Pets, pet); // the rank is the one the map's dot has
            Border frame = Row(pet, rank, loc, metres.TryGetValue(pet.PetId, out double m) ? m : null, model.Filtered);
            frame.Background = flashes?.Brush(pet.PetId, RowRest) ?? Brushes.Transparent;
            int id = pet.PetId;
            frame.Cursor = System.Windows.Input.Cursors.Hand;
            frame.ToolTip = loc[model.Filtered ? "PetPanels.UncheckTip" : "PetPanels.CheckTip"];
            frame.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true; // a click on a line checks its pet, it does not drag the unlocked panel
                PetClicked?.Invoke(id);
            };
            _rowFrames[pet.PetId] = frame;
            _rows.Children.Add(frame);
        }

        if (model.Pets.Count > shown.Count)
        {
            AddNote(string.Format(loc["PetPanels.More"], model.Pets.Count - shown.Count));
        }

        Shown = shown.Select(p => p.PetId).ToList();
    }

    /// <summary>Whether the list shows the open pet shop's pets now (read by the smoke harness).</summary>
    public bool ShowsShop => _lastShop is not null;

    /// <summary>
    /// The open pet shop (Stacy, 2026-10-17, after SkeeveTV's 1.1.2, MIT): "Pet shop", then each pet the shop offers with
    /// its badge, name and price (the tab in the price's tooltip), and what the player has of it (the three tiers' bar and "Lv. 1 · 12/25", "not owned"
    /// in red, the top level in green). Every offer is listed (the rows scroll with a height of its own); a line checks
    /// nothing. Rebuilt only when the rows or the language changed; the next <see cref="ShowModel"/> rebuilds the pets of
    /// the zone.
    /// </summary>
    public void ShowShop(IReadOnlyList<ShopRow> rows, IReadOnlyList<Aion2.Aion2ShopSkin>? skins = null)
    {
        var loc = LocalizationManager.Instance;
        skins ??= [];
        if (_lastShop is { } last && last.SequenceEqual(rows) && _lastSkins.SequenceEqual(skins) && _lastShopLanguage == loc.Language)
        {
            return;
        }

        _lastShop = rows.ToList();
        _lastSkins = skins.ToList();
        _lastShopLanguage = loc.Language;
        ApplyBodySize();
        _lastShown = null; // the zone's pets are built again after the shop
        _lastPets = null;
        Rebuilds++;
        _rows.Children.Clear();
        _rowFrames.Clear();
        _all.Visibility = Visibility.Collapsed;
        _title.Text = loc["PetShop.Title"];
        // The pets already at the top level are hidden, as Questlog does: nothing left to buy (Stacy, 2026-11-17).
        var open = rows.Where(r => r.Level < 3).ToList();
        int recommended = open.Count(r => r.Advice.Recommended) + skins.Count;
        _count.Text = recommended > 0
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["PetShop.RecommendedCount"], recommended)
            : string.Format(loc[open.Count == 1 ? "Pets.ZonePet" : "Pets.ZonePets"], open.Count);
        _count.Foreground = recommended > 0 ? RecommendText : MutedText;
        // The skins of the player's own weapon first (Stacy, 2026-11-19): power when he does not have them yet; the game says "En possession".
        foreach (Aion2.Aion2ShopSkin skin in skins)
        {
            _rows.Children.Add(SkinLine(skin, loc));
        }

        foreach (ShopRow row in open)
        {
            _rows.Children.Add(ShopLine(row, loc));
        }

        int maxed = rows.Count - open.Count;
        if (maxed > 0)
        {
            AddNote(string.Format(System.Globalization.CultureInfo.CurrentCulture, loc[maxed == 1 ? "PetShop.MaxedHidden1" : "PetShop.MaxedHidden"], maxed));
        }
        else if (open.Count == 0)
        {
            AddNote(loc["PetShop.Empty"]);
        }

        Shown = open.Select(r => r.PetId).ToList();
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.PetListLeft = left;
        s.PetListTop = top;
    });

    protected override Point DefaultPlace(Rect workArea) => new(workArea.Right - MapSizeForDefault - 40, workArea.Top + 120 + MapSizeForDefault + 70);

    /// <summary>The first drag of the grip gives the list a height of its own (from the one it had).</summary>
    /// <summary>The corner grip grows or shrinks the whole list, text included, as the meter's own grip does (Stacy, 2026-11-17: "comme sur le meter en
    /// bas à droite"); its width and height in the settings stay what they were.</summary>
    protected override void ResizeBy(double dx, double dy)
    {
        double drawn = ListWidth * Scale;
        SetScale((drawn + Math.Max(dx, dy)) / ListWidth);
    }

    protected override void SaveSize()
    {
        double saved = Scale;
        SaveSettings(s => s.PetListScale = saved);
    }

    private void AddNote(string text, Brush? brush = null) => _rows.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11.5,
        Foreground = brush ?? MutedText,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 2, 2, 0),
    });

    /// <summary>Badge | name and map colour | spawns, then the three tiers' bar and "Niv. 1 · 12/25", in a frame that
    /// flashes.</summary>
    private static int IndexOf(IReadOnlyList<TrackerPet> pets, TrackerPet pet)
    {
        for (int i = 0; i < pets.Count; i++)
        {
            if (pets[i].PetId == pet.PetId)
            {
                return i;
            }
        }

        return 0;
    }

    private static readonly Brush MissingText = PetLook.Frozen(Color.FromRgb(0xFF, 0x5A, 0x4F));
    private static readonly Brush NotFarmableText = PetLook.Frozen(Color.FromRgb(0xE7, 0xC4, 0x6A));
    private static readonly Brush TopText = PetLook.Frozen(Color.FromRgb(0x4C, 0xC8, 0x78));

    private static readonly Brush RecommendText = PetLook.Frozen(Color.FromRgb(0xF2, 0xC9, 0x4C));
    private static readonly Brush RecommendPill = PetLook.Frozen(Color.FromArgb(0x33, 0xF2, 0xC9, 0x4C));
    private static readonly Brush RecommendEdge = PetLook.Frozen(Color.FromArgb(0x99, 0xF2, 0xC9, 0x4C));
    private static readonly Brush RecommendBack = PetLook.Frozen(Color.FromArgb(0x1A, 0xF2, 0xC9, 0x4C));
    private static readonly Brush CardBack = PetLook.Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// One pet of the shop as a small card, after Questlog's black market (Stacy, 2026-11-17): the species badge | the name and the price, then
    /// "● Species  Lv. 1 · 21/25" and the tiers' bar, then why to buy it - a gold "★ RECOMMANDÉ" and the reason (one boss only, few spawn points,
    /// shop only), the card framed in gold - or a discreet note of where else it drops (Altgard, a dungeon, an event).
    /// </summary>
    private static Border ShopLine(ShopRow row, LocalizationManager loc)
    {
        ShopAdvice advice = row.Advice;
        string? species = Aion2PetCatalog.Species(row.PetId);
        Brush speciesBrush = PetLook.BadgeBrush(species);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = speciesBrush,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0),
            Child = new TextBlock
            {
                Text = PetLook.Initial(row.Name),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRowSpan(badge, 3);
        grid.Children.Add(badge);

        // Line 1: the name and the price (the tab in its tooltip: the name keeps the room).
        var top = new DockPanel { LastChildFill = true };
        string tab = loc[row.Tab == Aion2.Aion2PetShop.TabSpecial ? "PetShop.TabSpecial" : "PetShop.TabPet"];
        var offer = new TextBlock
        {
            Text = row.Price.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            ToolTip = tab,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = RowText,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(offer, Dock.Right);
        top.Children.Add(offer);
        top.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(top, 1);
        grid.Children.Add(top);

        // Line 2: "● Species", what the player has (not owned in red, "Lv. 1 · 12/25") and the three tiers.
        bool missing = row.Level == 0 && row.Souls == 0;
        var middle = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 0) };
        var kind = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        kind.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = speciesBrush });
        if (species is not null && loc["Pets.Species." + species] is { } speciesName && speciesName != "Pets.Species." + species)
        {
            kind.Inlines.Add(new System.Windows.Documents.Run(speciesName + "  ") { Foreground = speciesBrush, FontWeight = FontWeights.SemiBold });
        }

        kind.Inlines.Add(new System.Windows.Documents.Run(missing ? loc["PetShop.Missing"] : PetLook.ProgressText(row.Level, row.Souls))
        {
            Foreground = missing ? MissingText : MutedText,
            FontWeight = missing ? FontWeights.SemiBold : FontWeights.Normal,
        });
        DockPanel.SetDock(kind, Dock.Left);
        middle.Children.Add(kind);
        middle.Children.Add(new PetTierBar { Level = row.Level, Souls = row.Souls, Height = 3, Track = BarTrack, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 64, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(middle, 1);
        Grid.SetRow(middle, 1);
        grid.Children.Add(middle);

        // Line 3: why to buy it (gold), or where else it drops (a discreet note).
        if (advice.ReasonKey is string reasonKey)
        {
            var why = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            string reason = string.Format(System.Globalization.CultureInfo.CurrentCulture, loc[reasonKey], advice.Points);
            if (advice.Recommended)
            {
                why.Children.Add(new Border
                {
                    Background = RecommendPill,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 0, 5, 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "★ " + loc["PetShop.Recommended"], FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = RecommendText },
                });
            }

            why.Children.Add(new TextBlock
            {
                Text = reason,
                FontSize = 10,
                FontStyle = advice.Recommended ? FontStyles.Normal : FontStyles.Italic,
                FontWeight = advice.Recommended ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = advice.Recommended ? RecommendText : NotFarmableText,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(why, 1);
            Grid.SetRow(why, 2);
            grid.Children.Add(why);
        }

        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 5, 7, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Background = advice.Recommended ? RecommendBack : CardBack,
            BorderBrush = advice.Recommended ? RecommendEdge : null,
            BorderThickness = new Thickness(advice.Recommended ? 1 : 0),
        };
    }

    /// <summary>A weapon skin of the player's own weapon, as a gold card: a sword badge | its name and price, then "★ RECOMMANDÉ" and why
    /// (power, unless the game shows it "En possession": the meter cannot tell).</summary>
    private static Border SkinLine(Aion2.Aion2ShopSkin skin, LocalizationManager loc)
    {
        string full = Aion2.Protocol.Aion2ItemTable.NameOf(skin.ItemId, loc.Language) ?? skin.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int open = full.IndexOf(" (", StringComparison.Ordinal);
        string name = open > 0 ? full[..open] : full;
        string weapon = skin.Weapon;
        if (open > 0 && full.IndexOf(')', open) is int close and > 0)
        {
            string inside = full[(open + 2)..close];
            int colon = inside.LastIndexOf(':');
            weapon = (colon >= 0 ? inside[(colon + 1)..] : inside).Trim();
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = RecommendPill,
            BorderBrush = RecommendEdge,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0),
            Child = new TextBlock { Text = "⚔", FontSize = 13, Foreground = RecommendText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Grid.SetRowSpan(badge, 2);
        grid.Children.Add(badge);

        var top = new DockPanel { LastChildFill = true };
        var price = new TextBlock
        {
            Text = skin.Price.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = RowText,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(price, Dock.Right);
        top.Children.Add(price);
        top.Children.Add(new TextBlock { Text = name, ToolTip = full, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(top, 1);
        grid.Children.Add(top);

        var why = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        why.Children.Add(new Border
        {
            Background = RecommendPill,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "★ " + loc["PetShop.Recommended"], FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = RecommendText },
        });
        why.Children.Add(new TextBlock
        {
            Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, loc["PetShop.Why.Skin"], weapon),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = RecommendText,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(why, 1);
        Grid.SetRow(why, 1);
        grid.Children.Add(why);

        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 5, 7, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Background = RecommendBack,
            BorderBrush = RecommendEdge,
            BorderThickness = new Thickness(1),
        };
    }

    private static Border Row(TrackerPet pet, int rank, LocalizationManager loc, double? metres, bool isChecked)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(21) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var badge = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = PetLook.BadgeBrush(Aion2PetCatalog.Species(pet.PetId)),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = PetLook.Initial(pet.Name),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRowSpan(badge, 2);
        grid.Children.Add(badge);

        // Line 1: the map colour, the name, the spawns.
        var top = new DockPanel { LastChildFill = true };
        string spawns = metres is double away ? $"{Math.Round(away):0} m"
            : pet.SpawnCount == 0 ? loc["PetPanels.NoPoint"]
            : string.Format(loc[pet.SpawnCount == 1 ? "PetPanels.Point" : "PetPanels.Points"], pet.SpawnCount);
        var spawnsText = new TextBlock { Text = spawns, FontSize = 11, Foreground = MutedText, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(spawnsText, Dock.Right);
        top.Children.Add(spawnsText);
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = PetLook.Frozen(PetLook.DotColor(rank)),
            BorderBrush = PetLook.Frozen(Color.FromArgb(0xC0, 0x05, 0x08, 0x0C)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(dot, Dock.Left);
        top.Children.Add(dot);
        if (isChecked)
        {
            var check = new TextBlock
            {
                Text = "✓",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = PetLook.Frozen(Color.FromRgb(0xB8, 0xA6, 0xFF)),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(check, Dock.Left);
            top.Children.Add(check);
        }

        top.Children.Add(new TextBlock
        {
            Text = pet.Name,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = RowText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(top, 1);
        grid.Children.Add(top);

        // Line 2: the three tiers (5 | 25 | 75 souls) and "Niv. 1 · 12/25".
        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 0) };
        var progress = new TextBlock
        {
            Text = PetLook.ProgressText(pet.Level, pet.Souls),
            FontSize = 9.5,
            Foreground = MutedText,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(progress, Dock.Right);
        bottom.Children.Add(progress);
        bottom.Children.Add(new PetTierBar { Level = pet.Level, Souls = pet.Souls, Height = 3, Track = BarTrack, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 1);
        grid.Children.Add(bottom);
        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3, 2, 3, 3),
            Margin = new Thickness(0, 0, 0, 1),
        };
    }
}
