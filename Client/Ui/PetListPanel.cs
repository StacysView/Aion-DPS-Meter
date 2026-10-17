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
/// of the Pets window (<see cref="PetChoices"/>). Rebuilt on every soul (see <see cref="PetPanelSet"/>). Pet data:
/// SkeeveTV (MIT).
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
    private readonly Dictionary<int, Border> _rowFrames = new();
    private (string? Map, string Zone, bool Unknown, string Language, bool AllRows, bool Filtered)? _lastShown;
    private List<TrackerPet>? _lastPets;
    private TrackerModel? _lastModel;

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
        _body = new DockPanel { Width = ListWidth, LastChildFill = true };
        _body.Children.Add(header);
        _body.Children.Add(scroll);
        Body = _body;
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
        _body.Width = ListWidth;
        _body.Height = ListHeight ?? double.NaN;
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

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.PetListLeft = left;
        s.PetListTop = top;
    });

    protected override Point DefaultPlace(Rect workArea) => new(workArea.Right - MapSizeForDefault - 40, workArea.Top + 120 + MapSizeForDefault + 70);

    /// <summary>The first drag of the grip gives the list a height of its own (from the one it had).</summary>
    protected override void ResizeBy(double dx, double dy)
    {
        double height = ListHeight ?? _body.ActualHeight;
        bool hadHeight = ListHeight is not null;
        SetSize(ListWidth + dx, height + dy);
        if (!hadHeight && _lastModel is { } model)
        {
            _lastShown = null; // every pet is listed now, with a scroll
            ShowModel(model);
        }
    }

    protected override void SaveSize() => SaveSettings(s =>
    {
        s.PetListWidth = ListWidth;
        s.PetListHeight = ListHeight;
    });

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
