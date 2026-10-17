using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pet list over the game: the shown zone's name, then each needed pet of it (fewest souls left first) with its
/// species badge, its name and map colour, a bar of its souls towards the next level and its number of spawn
/// points on the map. Rebuilt on every soul (see <see cref="PetPanelSet"/>). Pet data: SkeeveTV (MIT).
/// </summary>
public sealed class PetListPanel : PetPanelWindow
{
    private const double PanelWidth = 250;
    private const int MaxRows = 10;

    private static readonly Brush BarTrack = PetLook.Frozen(Color.FromRgb(0x2A, 0x38, 0x46));
    private static readonly Brush RowText = PetLook.Frozen(Color.FromRgb(0xE6, 0xEC, 0xF1));

    private readonly TextBlock _title = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock _count = new() { FontSize = 11, Foreground = MutedText, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _rows = new();

    public PetListPanel() : base("Aion DPS - pet list")
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 6), LastChildFill = true };
        DockPanel.SetDock(_count, Dock.Right);
        header.Children.Add(_count);
        header.Children.Add(_title);
        var stack = new StackPanel { Width = PanelWidth };
        stack.Children.Add(header);
        stack.Children.Add(_rows);
        Body = stack;
    }

    /// <summary>The pet ids of the rows shown (read by the smoke harness).</summary>
    public IReadOnlyList<int> Shown { get; private set; } = [];

    /// <summary>Set by <see cref="PetPanelSet"/>: the map's side, the list's default place being below it.</summary>
    public double MapSizeForDefault { get; set; } = 260;

    public void PlaceAt(double? left, double? top)
    {
        Point place = left is double l && top is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
        Left = place.X;
        Top = place.Y;
    }

    public void ShowModel(TrackerModel model)
    {
        var loc = LocalizationManager.Instance;
        _rows.Children.Clear();
        if (model.Map is null)
        {
            _title.Text = loc["Pets.Title"];
            _count.Text = "";
            AddNote(loc["PetPanels.NoMap"], RowText);
            AddNote(loc["PetPanels.NoMapHint"]);
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

        var shown = model.Pets.Take(MaxRows).ToList();
        foreach (TrackerPet pet in shown)
        {
            _rows.Children.Add(Row(pet, shown.IndexOf(pet), loc)); // the rank is the one the map's dot has
        }

        if (model.Pets.Count > MaxRows)
        {
            AddNote(string.Format(loc["PetPanels.More"], model.Pets.Count - MaxRows));
        }

        Shown = shown.Select(p => p.PetId).ToList();
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.PetListLeft = left;
        s.PetListTop = top;
    });

    protected override Point DefaultPlace(Rect workArea) => new(workArea.Right - MapSizeForDefault - 40, workArea.Top + 120 + MapSizeForDefault + 70);

    private void AddNote(string text, Brush? brush = null) => _rows.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11.5,
        Foreground = brush ?? MutedText,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 2, 2, 0),
    });

    /// <summary>Badge | name and map colour | spawn points, then the bar and souls/needed.</summary>
    private static FrameworkElement Row(TrackerPet pet, int rank, LocalizationManager loc)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = PetLook.BadgeBrush(Aion2PetCatalog.Species(pet.PetId)),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = PetLook.Initial(pet.Name),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRowSpan(badge, 2);
        grid.Children.Add(badge);

        // Line 1: the map colour, the name, the spawn points.
        var top = new DockPanel { LastChildFill = true };
        string points = pet.SpawnCount == 0 ? loc["PetPanels.NoPoint"]
            : string.Format(loc[pet.SpawnCount == 1 ? "PetPanels.Point" : "PetPanels.Points"], pet.SpawnCount);
        var pointsText = new TextBlock { Text = points, FontSize = 11, Foreground = MutedText, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(pointsText, Dock.Right);
        top.Children.Add(pointsText);
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
        top.Children.Add(new TextBlock
        {
            Text = pet.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = RowText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(top, 1);
        grid.Children.Add(top);

        // Line 2: the bar of souls towards the next level, and souls/needed.
        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 3, 0, 0) };
        var souls = new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, "{0}/{1}", pet.Souls, pet.Needed),
            FontSize = 10.5,
            Foreground = MutedText,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(souls, Dock.Right);
        bottom.Children.Add(souls);
        double share = pet.Needed > 0 ? Math.Clamp((double)pet.Souls / pet.Needed, 0, 1) : 0;
        var bar = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        var track = new Border { Background = BarTrack, CornerRadius = new CornerRadius(2.5) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        if (share > 0)
        {
            bar.Children.Add(new Border { Background = Accent, CornerRadius = new CornerRadius(2.5) });
        }

        bottom.Children.Add(bar);
        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 1);
        grid.Children.Add(bottom);
        return grid;
    }
}
