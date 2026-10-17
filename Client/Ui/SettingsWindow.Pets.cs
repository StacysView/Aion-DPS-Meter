using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The Familiers page (Stacy, 2026-10-17): a big live <see cref="PetMapView"/> of the needed pets (the same model as
/// the map over the game: <see cref="PetTrackerView.Build"/> with the pins and the followed zone), dragged and zoomed
/// from 25 to 400 %, a hovered dot naming its pet; beside it the zone it follows ("Where I am" or a farm zone) and the
/// pets of that map with a check each. The checks are the pins and the zone the farm zone: both are saved at once
/// through <see cref="PetChoices"/> (not by Save), like in the Pets window, which follows them as the panels do. The
/// map follows the collection and the player's place while the window is open (BeginInvoke from the capture thread,
/// at most one rebuild per 250 ms; a move on the same map only moves the arrow). The window widens while the page is
/// open and gets its width back after.
/// </summary>
public partial class SettingsWindow
{
    private const double PetsPageWidth = 1060;
    private static readonly TimeSpan PetsRebuildGap = TimeSpan.FromMilliseconds(250);

    private readonly PetMapView _petsMap = new() { Pannable = true, Interactive = true, MinZoom = 0.25, MaxZoom = 4.0 };
    private Aion2PetCollection? _pets;
    private Aion2PlayerPosition? _position;
    private HashSet<int> _petPins = new();
    private string _petSearch = "";
    private string? _petFarmZone;
    private DispatcherTimer? _petsTimer;
    private DateTime _petsBuiltAt = DateTime.MinValue;
    private bool _petsDirty = true;
    private double? _widthBeforePets;

    /// <summary>What the page's map shows now (read by the smoke harness).</summary>
    public TrackerModel? PetsModel { get; private set; }

    /// <summary>The page's map (read by the smoke harness).</summary>
    public PetMapView PetsMap => _petsMap;

    /// <summary>The pet ids listed with a check box, and the checked ones (read by the smoke harness).</summary>
    public IReadOnlyList<int> PetsListed { get; private set; } = [];

    public IReadOnlyCollection<int> PetsChecked => _petPins;

    /// <summary>Opens the Familiers page (the menus' "Familiers" rows, the smoke harness).</summary>
    public void ShowPetsPage() => NavPets.IsChecked = true;

    /// <summary>Checks or unchecks a pet of the page's list, as a click would (for the smoke harness).</summary>
    public void TogglePetForTest(int petId) => PetChoices.Save(PetChoices.Toggled(_petPins, petId), _petFarmZone);

    /// <summary>Gives the page the capture's pets and the player's place (the meter calls it when its source is the
    /// packet capture; without it the page says the meter is not connected).</summary>
    public void AttachPets(Aion2PetCollection pets, Aion2PlayerPosition position)
    {
        if (_pets is not null)
        {
            return;
        }

        _pets = pets;
        _position = position;
        MeterSettings saved = MeterSettings.Load();
        _petPins = saved.PetPins.ToHashSet();
        _petFarmZone = saved.PetFarmZone;
        _petsMap.SetZoom(0.75);
        PetsMapHost.Child = _petsMap;

        Action later = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, SchedulePetsRefresh);
        Action<Aion2PetState, bool> changed = (_, _) => later();
        Action moved = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, OnPetsPlayerMoved);
        Action<IReadOnlyCollection<int>, string?> choices = (pins, zone) =>
        {
            _petPins = pins.ToHashSet();
            _petFarmZone = zone;
            RefreshPets();
        };
        pets.Changed += changed;
        pets.ListReplaced += later;
        position.Changed += moved;
        PetChoices.Changed += choices;
        _petsMap.ZoomChanged += _ => ShowPetsZoom();
        Closed += (_, _) =>
        {
            _petsTimer?.Stop();
            pets.Changed -= changed;
            pets.ListReplaced -= later;
            position.Changed -= moved;
            PetChoices.Changed -= choices;
        };
        RefreshPets();
    }

    /// <summary>Rebuilds the map, the zones and the check list (only while the page is open; else at its opening).</summary>
    private void RefreshPets()
    {
        if (_pets is null || _position is null)
        {
            return;
        }

        if (NavPets.IsChecked != true)
        {
            _petsDirty = true;
            return;
        }

        _petsDirty = false;
        _petsBuiltAt = DateTime.UtcNow;
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        TrackerModel model = PetTrackerView.Build(_pets, _position, language, _petPins, _petFarmZone);
        PetsModel = model;
        bool otherMap = model.Map is not null && _position.ZoneId > 0 && _position.Map?.Key != model.Map.Key;
        _petsMap.SetModel(model, otherMap);
        PetsMapTitle.Text = model.Map is null ? loc["Pets.Title"] : model.ZoneName;
        ShowPetsZoom();
        BuildPetZones(language, loc);
        BuildPetChecks(model, language, loc);
        ShowPetPhases(loc);
    }

    private void ShowPetsZoom() => PetsZoomText.Text = string.Format(CultureInfo.CurrentCulture, "{0:0} %", _petsMap.Zoom * 100);

    /// <summary>"Where I am" (no farm zone: the player's map, all its pets), then the zones holding pets still below
    /// level 3, as in the Pets window; a click follows the zone, a click on the followed one lets it go.</summary>
    private void BuildPetZones(string language, LocalizationManager loc)
    {
        PetsZonesList.Children.Clear();
        PetsZonesList.Children.Add(ZoneLine(null, loc["Settings.Pets.MyMap"], loc["Settings.Pets.MyMapLine"], _petFarmZone is null));
        foreach (PetZoneRow zone in PetsView.Build(_pets!, language, new HashSet<int>(), null, _petFarmZone, phase: 3).Zones)
        {
            string pets = string.Format(loc[zone.PetsHere == 1 ? "Pets.ZonePet" : "Pets.ZonePets"], zone.PetsHere);
            string souls = string.Format(loc[zone.SoulsLeft == 1 ? "Pets.SoulN" : "Pets.SoulsN"], zone.SoulsLeft);
            bool hasMap = Aion2WorldMaps.Get(zone.Map) is not null;
            PetsZonesList.Children.Add(ZoneLine(zone.Key, zone.Name, pets + " · " + souls + (zone.Chosen ? " · " + loc["Pets.Farming"] : ""), zone.Chosen, hasMap));
        }
    }

    private Border ZoneLine(string? key, string name, string line, bool chosen, bool hasMap = true)
    {
        var text = new StackPanel();
        var title = new TextBlock { Text = name, FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, chosen ? "Brush.Heading" : "Brush.Body");
        var sub = new TextBlock { Text = line, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        sub.SetResourceReference(TextBlock.FontSizeProperty, "Fs.11");
        text.Children.Add(title);
        text.Children.Add(sub);
        var border = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 5, 9, 6),
            Margin = new Thickness(0, 0, 0, 2),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Opacity = hasMap ? 1 : 0.6, // the Abyss has no map: following it keeps the player's map
            Tag = key,
        };
        if (chosen)
        {
            border.SetResourceReference(Border.BackgroundProperty, "Brush.BrandNav");
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Brand");
        }

        border.MouseEnter += (_, _) =>
        {
            if (!chosen)
            {
                border.Background = PetLook.Frozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            }
        };
        border.MouseLeave += (_, _) =>
        {
            if (!chosen)
            {
                border.Background = Brushes.Transparent;
            }
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            string? zone = key is null || key == _petFarmZone ? null : key;
            if (zone != _petFarmZone)
            {
                PetChoices.Save(_petPins, zone);
            }
        };
        return border;
    }

    /// <summary>The pets of the shown map (or zone) still below level 3, each with a check: the checked ones are the
    /// pins (the map then shows only them). A pinned pet that lives elsewhere is listed too, so it can be unchecked.</summary>
    private void BuildPetChecks(TrackerModel shown, string language, LocalizationManager loc)
    {
        PetsCheckList.Children.Clear();
        TrackerModel all = _petPins.Count == 0 ? shown : PetTrackerView.Build(_pets!, _position!, language, new HashSet<int>(), _petFarmZone);
        var pets = all.Pets.Select(p => (p.PetId, p.Name, p.Level, p.Souls)).ToList();
        foreach (PetRow pinned in PetsView.Build(_pets!, language, _petPins, null, null).Rows.Where(r => r.Pinned))
        {
            if (pets.All(p => p.PetId != pinned.PetId))
            {
                pets.Add((pinned.PetId, pinned.Name, pinned.Level, pinned.Souls));
            }
        }

        PetsListed = pets.Select(p => p.PetId).ToList();
        bool anyChecked = shown.Filtered;
        PetsAllLink.Visibility = anyChecked ? Visibility.Visible : Visibility.Collapsed;
        if (pets.Count == 0)
        {
            var empty = new TextBlock { Text = loc["Settings.Pets.ListEmpty"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 4) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
            PetsCheckList.Children.Add(empty);
            return;
        }

        // Grouped by species like the Pets window, in the list's own order inside a group, and filtered by the search.
        var rows = PetsView.Build(_pets!, language, new HashSet<int>(), null, null, phase: 3, pinsFilter: false).Rows.ToDictionary(r => r.PetId);
        var listed = pets.Select(p => rows.TryGetValue(p.PetId, out PetRow? row)
            ? row with { Level = p.Level, Souls = p.Souls }
            : new PetRow(p.PetId, p.Name, null, p.Level, p.Souls, 0, 0, [], null, false)).ToList();
        IReadOnlyList<PetGroup> groups = PetsView.Group(listed, null, _petSearch);
        PetsGroupsShown = groups;
        var shownPets = shown.Pets.ToList();
        if (groups.Count == 0)
        {
            var none = new TextBlock { Text = string.Format(loc["Pets.NoMatch"], _petSearch.Trim()), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 4) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
            PetsCheckList.Children.Add(none);
            return;
        }

        foreach (PetGroup group in groups)
        {
            PetsCheckList.Children.Add(GroupHeader(group, loc, PetsCheckList.Children.Count == 0));
            foreach (PetRow pet in group.Rows)
            {
                bool isChecked = anyChecked && _petPins.Contains(pet.PetId);
                int rank = shownPets.FindIndex(p => p.PetId == pet.PetId);
                PetsCheckList.Children.Add(CheckLine(pet.PetId, pet.Name, pet.Level, pet.Souls, isChecked, rank, pet.Monsters, loc));
            }
        }
    }

    /// <summary>"Obtenir (5) · 160/207   Niveau 2 (25) · 26/207   Niveau 3 (75) · 6/207": the tiers of the whole collection.</summary>
    private void ShowPetPhases(LocalizationManager loc)
    {
        PetsViewModel all = PetsView.Build(_pets!, loc.Language, new HashSet<int>(), null, null);
        PetsPhasesText.Text = string.Join("     ", all.Steps.Select(s => string.Format(loc["Pets.PhaseStep." + s.Level], s.Reached, s.Total)));
        PetsPhasesText.Visibility = all.Steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The species groups of the check list as last built (read by the preview harness).</summary>
    public IReadOnlyList<PetGroup> PetsGroupsShown { get; private set; } = [];

    /// <summary>The check list's search text (set by the preview harness).</summary>
    public string PetsSearch
    {
        get => PetsSearchBox.Text;
        set => PetsSearchBox.Text = value;
    }

    /// <summary>A species header in the check list: its colour dot, its name and how many pets it has here.</summary>
    private static FrameworkElement GroupHeader(PetGroup group, LocalizationManager loc, bool first)
    {
        Brush colour = PetLook.BadgeBrush(group.Species);
        string label = string.IsNullOrEmpty(group.Species) ? "\u2014" : loc["Pets.Species." + group.Species];
        var line = new DockPanel { Margin = new Thickness(6, first ? 2 : 8, 6, 3) };
        line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = colour, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new TextBlock { Text = label, Foreground = colour, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var count = new TextBlock { Text = group.Rows.Count.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        line.Children.Add(count);
        return line;
    }

    private void OnPetsSearchChanged(object sender, TextChangedEventArgs e)
    {
        _petSearch = PetsSearchBox.Text;
        PetsSearchHint.Visibility = _petSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PetsSearchClear.Visibility = _petSearch.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_pets is not null && _position is not null)
        {
            var loc = LocalizationManager.Instance;
            BuildPetChecks(PetsModel ?? PetTrackerView.Build(_pets, _position, loc.Language, _petPins, _petFarmZone), loc.Language, loc);
        }
    }

    private void OnPetsSearchClear(object sender, MouseButtonEventArgs e) => PetsSearchBox.Text = "";

    private Border CheckLine(int petId, string name, int level, int souls, bool isChecked, int rank, IReadOnlyList<string> monsters, LocalizationManager loc)
    {
        var box = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1.2),
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = isChecked ? new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
            } : null,
        };
        box.SetResourceReference(Border.BorderBrushProperty, isChecked ? "Brush.Brand" : "Brush.FrameStrong");
        if (isChecked)
        {
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Brand");
        }

        // The pet's dot colour on the map (none for a pet not drawn there).
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = rank >= 0 ? PetLook.Frozen(PetLook.DotColor(rank)) : Brushes.Transparent,
        };
        var progress = new TextBlock { Text = PetLook.ProgressText(level, souls), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        progress.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        progress.SetResourceReference(TextBlock.FontSizeProperty, "Fs.11");
        var title = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, isChecked ? "Brush.Heading" : "Brush.Body");
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(box, Dock.Left);
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(progress, Dock.Right);
        line.Children.Add(box);
        line.Children.Add(dot);
        line.Children.Add(progress);
        line.Children.Add(title);

        // Under the name: the 5 | 25 | 75 bar, the souls still needed and the monsters that drop them (as the old Pets window showed).
        int needed = level <= 0 ? 5 : level == 1 ? 25 : 75;
        int left = Math.Max(0, needed - souls);
        var bar = new PetTierBar { Level = level, Souls = souls, Width = 64, Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        string mobs = monsters.Count == 0 ? "" : " · " + string.Join(", ", monsters.Take(2)) + (monsters.Count > 2 ? $" +{monsters.Count - 2}" : "");
        var detail = new TextBlock
        {
            Text = string.Format(loc[left == 1 ? "Pets.SoulLeft" : "Pets.SoulsLeft"], left) + mobs,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        detail.SetResourceReference(TextBlock.FontSizeProperty, "Fs.10");
        var second = new DockPanel { Margin = new Thickness(33, 2, 0, 0), LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Left);
        second.Children.Add(bar);
        second.Children.Add(detail);
        var both = new StackPanel();
        both.Children.Add(line);
        both.Children.Add(second);
        var border = new Border
        {
            Child = both,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 5, 9, 5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = LocalizationManager.Instance[isChecked ? "PetPanels.UncheckTip" : "PetPanels.CheckTip"]
                + (monsters.Count > 0 ? "\n" + string.Join(", ", monsters) : ""),
        };
        border.MouseEnter += (_, _) => border.Background = PetLook.Frozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => PetChoices.Save(PetChoices.Toggled(_petPins, petId), _petFarmZone);
        return border;
    }

    private void OnPetsAllClicked(object sender, MouseButtonEventArgs e) => PetChoices.Save([], _petFarmZone);

    /// <summary>The player moved: on the shown map only the arrow moves; anything else rebuilds.</summary>
    private void OnPetsPlayerMoved()
    {
        if (_position is null || PetsModel is null || NavPets.IsChecked != true)
        {
            _petsDirty = true;
            return;
        }

        if (PetsModel.Map is { } map && _position.Map?.Key == map.Key && PetsModel.Player is not null && _position.Last is { } here)
        {
            (double X, double Y) player = map.ToPixel(here.X, here.Y);
            PetsModel = PetsModel with { Player = player };
            _petsMap.SetPlayer(player);
            return;
        }

        SchedulePetsRefresh();
    }

    /// <summary>One rebuild per <see cref="PetsRebuildGap"/> at most; a call inside the gap is merged into one later.</summary>
    private void SchedulePetsRefresh()
    {
        TimeSpan since = DateTime.UtcNow - _petsBuiltAt;
        if (since >= PetsRebuildGap)
        {
            RefreshPets();
            return;
        }

        if (_petsTimer is { IsEnabled: true })
        {
            return;
        }

        _petsTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _petsTimer.Interval = PetsRebuildGap - since;
        _petsTimer.Tick -= OnPetsTimer;
        _petsTimer.Tick += OnPetsTimer;
        _petsTimer.Start();
    }

    private void OnPetsTimer(object? sender, EventArgs e)
    {
        _petsTimer?.Stop();
        RefreshPets();
    }

    /// <summary>The page opened (wider window, a fresh map) or closed (the width it had before).</summary>
    private void OnPetsPageShown(bool shown)
    {
        if (shown)
        {
            if (Width < PetsPageWidth && WindowState == WindowState.Normal)
            {
                Rect work = SystemParameters.WorkArea;
                double width = Math.Min(PetsPageWidth, work.Width);
                if (width > Width)
                {
                    _widthBeforePets = Width;
                    double grow = width - Width;
                    Width = width;
                    Left = Math.Max(work.Left, Math.Min(Left - grow / 2, work.Right - width));
                }
            }

            if (_petsDirty)
            {
                RefreshPets();
            }

            return;
        }

        if (_widthBeforePets is double before && WindowState == WindowState.Normal)
        {
            double shrink = Width - before;
            Width = before;
            Left += Math.Max(0, shrink / 2);
        }

        _widthBeforePets = null;
    }

    /// <summary>Beside the map when the page is wide enough, below it at the window's minimum size.</summary>
    private void OnPetsLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 640;
        if (narrow == (Grid.GetRow(PetsSide) == 1))
        {
            return;
        }

        Grid.SetRow(PetsSide, narrow ? 1 : 0);
        Grid.SetColumn(PetsSide, narrow ? 0 : 1);
        Grid.SetColumnSpan(PetsSide, narrow ? 2 : 1);
        PetsSide.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(14, 0, 0, 0);
        PetsSideColumn.Width = narrow ? new GridLength(0) : new GridLength(270);
        PetsMapHost.Height = narrow ? 360 : 440;
    }
}
