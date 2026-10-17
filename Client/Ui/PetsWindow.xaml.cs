using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The Pets window: phase bar, species chips, one card per pet (pin toggle) and the places worth
/// farming, all drawn from <see cref="PetsView.Build"/>. Follows the collection live (the events come
/// from the capture thread: BeginInvoke, at most one rebuild per 250 ms). Pins and the followed zone
/// are kept in the settings file. Pet data: SkeeveTV (MIT) - see Aion2PetCatalog.
/// </summary>
public partial class PetsWindow : Window
{
    private static readonly TimeSpan RebuildGap = TimeSpan.FromMilliseconds(250);

    /// <summary>The five species, in the order of the chips (the catalog's own names).</summary>
    private static readonly string[] SpeciesOrder = { "Intellect", "Feral", "Nature", "Trans", "Special" };

    private static readonly Color CardRest = Color.FromRgb(0x1A, 0x24, 0x30);
    private static readonly Brush CardFill = Frozen(CardRest);
    private static readonly Brush CardFrame = Frozen(Color.FromRgb(0x2A, 0x38, 0x46));
    private static readonly Brush ChosenFill = Frozen(Color.FromRgb(0x22, 0x1F, 0x3A));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x7B, 0x5C, 0xF0));
    private static readonly Brush ClearBrush = Frozen(Colors.Transparent);
    private static readonly Brush PinnedFill = Frozen(Color.FromRgb(0x4A, 0x3A, 0x9A));
    private static readonly Brush ChipOff = Frozen(Color.FromRgb(0x15, 0x1D, 0x26));
    private static readonly Brush MutedText = Frozen(Color.FromRgb(0x8F, 0xA3, 0xB3));
    private static readonly Brush ChipText = Frozen(Color.FromRgb(0xB8, 0xC6, 0xD2));

    private readonly Aion2PetCollection _pets;
    private readonly HashSet<int> _pins;
    private string? _farmZone;
    private string? _species;
    private DateTime _lastBuild = DateTime.MinValue;
    private bool _rebuildQueued;
    private bool _closed;
    private DispatcherTimer? _timer;
    private readonly PetFlashes _flashes = new();

    /// <summary>What the window shows now (read by the smoke harness).</summary>
    public PetsViewModel? Model { get; private set; }

    public PetsWindow(Aion2PetCollection pets)
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        ThemedChrome.Apply(this);
        _pets = pets;
        MeterSettings saved = MeterSettings.Load();
        _pins = saved.PetPins.ToHashSet();
        _farmZone = saved.PetFarmZone;
        _flashes.Enabled = saved.PetFlash;

        // The collection raises its events on the capture thread: hop to the UI thread, never wait for it.
        Action later = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, ScheduleRefresh);
        // A soul or a level: the pet's card flashes at the next rebuild (a level-up comes with its own Changed).
        Action<Aion2PetState, bool> onChanged = (state, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _flashes.Mark(state.PetId);
            ScheduleRefresh();
        });
        _pets.Changed += onChanged;
        _pets.ListReplaced += later;
        // The pins and the zone are shared with the list panel over the game and the settings page (PetChoices).
        Action<IReadOnlyCollection<int>, string?> onChoices = (pins, farmZone) =>
        {
            _pins.Clear();
            _pins.UnionWith(pins);
            _farmZone = farmZone;
            if (!_closed)
            {
                Refresh();
            }
        };
        PetChoices.Changed += onChoices;
        Closed += (_, _) =>
        {
            _closed = true;
            _timer?.Stop();
            _pets.Changed -= onChanged;
            _pets.ListReplaced -= later;
            PetChoices.Changed -= onChoices;
        };
        Refresh();
    }

    /// <summary>Whether a pet's card flashes when it gains souls or a level (<see cref="MeterSettings.PetFlash"/>).</summary>
    public bool FlashOnGain
    {
        get => _flashes.Enabled;
        set => _flashes.Enabled = value;
    }

    /// <summary>The pet ids whose card flashed at the last rebuild or still flashes (read by the smoke harness).</summary>
    public IReadOnlyList<int> Flashing { get; private set; } = [];

    /// <summary>One pet card as bound by the XAML template: <see cref="Level"/> and <see cref="Souls"/> draw the three
    /// tiers' bar, <see cref="BarText"/> is "Niv. 1 · 12/25", <see cref="Fill"/> the card's background (purple while it
    /// flashes).</summary>
    public sealed record PetCard(int PetId, string Name, string Initial, Brush Badge, string LeftText, string Detail, string Zone, string Tip,
        int Level, int Souls, string BarText, string PinTip, Brush PinBackground, double PinOpacity, Brush Fill, string PinText);

    /// <summary>One zone card as bound by the XAML template.</summary>
    public sealed record ZoneCard(string Key, string Name, string Line, string ButtonText, Brush ButtonFill, Brush Fill, Brush Frame);

    private static Brush BadgeBrush(string? species) => PetLook.BadgeBrush(species);

    private static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static string Loc(string key) => LocalizationManager.Instance[key];

    private static string SpeciesLabel(string? species)
    {
        if (species is null)
        {
            return "";
        }

        string key = "Pets.Species." + species;
        string named = Loc(key);
        return named != key ? named : species;
    }

    /// <summary>One rebuild per <see cref="RebuildGap"/> at most; a call inside the gap is merged into one later.</summary>
    private void ScheduleRefresh()
    {
        if (_closed)
        {
            return;
        }

        TimeSpan since = DateTime.UtcNow - _lastBuild;
        if (since >= RebuildGap)
        {
            Refresh();
            return;
        }

        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        var timer = _timer = new DispatcherTimer { Interval = RebuildGap - since };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _rebuildQueued = false;
            if (!_closed)
            {
                Refresh();
            }
        };
        timer.Start();
    }

    /// <summary>Rebuilds the whole window from the collection and the current filters.</summary>
    public void Refresh()
    {
        _lastBuild = DateTime.UtcNow;
        var loc = LocalizationManager.Instance;
        CultureInfo culture = CultureInfo.CurrentCulture;
        PetsViewModel model = PetsView.Build(_pets, loc.Language, _pins, _species, _farmZone, pinsFilter: false);
        Model = model;
        DateTime? listedAt = _pets.ListedAt;

        // Header: the title, then who and when.
        HeaderText.Text = loc["Pets.Title"];
        var sub = new List<string>();
        if (!string.IsNullOrWhiteSpace(_pets.Character))
        {
            sub.Add(_pets.Character!);
        }

        if (listedAt is { } at)
        {
            sub.Add(string.Format(loc["Pets.ListRead"], at.ToString("d MMM", culture) + " " + at.ToString("t", culture)));
        }

        SubText.Text = string.Join(" · ", sub);
        SubText.Visibility = sub.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        BuildSteps(model);
        BuildChips();

        HintText.Text = listedAt is null ? loc["Pets.EmptyNoList"] : "";
        HintText.Visibility = listedAt is null ? Visibility.Visible : Visibility.Collapsed;
        PinnedNote.Visibility = _pins.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        double petsAt = PetsScroll.VerticalOffset, zonesAt = ZonesScroll.VerticalOffset;
        _flashes.StartPending();
        var cards = model.Rows.Select(r => ToCard(r, loc, _flashes.Brush(r.PetId, CardRest))).ToList();
        Flashing = cards.Where(c => !ReferenceEquals(c.Fill, CardFill)).Select(c => c.PetId).ToList();
        PetsList.ItemsSource = cards;
        EmptyText.Text = model.Rows.Count > 0 ? "" : loc[model.PinnedOnly ? "Pets.NoPinnedInSpecies" : "Pets.AllDone"];
        EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ZonesList.ItemsSource = model.Zones.Select(z => ToZone(z, loc)).ToList();
        ZonesEmpty.Visibility = model.Zones.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // A live rebuild must not throw the player back to the top of the lists.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PetsScroll.ScrollToVerticalOffset(petsAt);
            ZonesScroll.ScrollToVerticalOffset(zonesAt);
        });
    }

    private static PetCard ToCard(PetRow r, LocalizationManager loc, Brush? flash)
    {
        string initial = PetLook.Initial(r.Name);
        string monsters = r.Monsters.Count == 0 ? "—"
            : string.Join(", ", r.Monsters.Take(2)) + (r.Monsters.Count > 2 ? $" +{r.Monsters.Count - 2}" : "");
        string species = SpeciesLabel(r.Species);
        string detail = (species.Length > 0 ? species + " · " : "") + monsters;
        string zone = "📍 " + (r.Zone ?? "—");
        string tip = (species.Length > 0 ? species + "\n" : "") + (r.Monsters.Count == 0 ? "—" : string.Join(", ", r.Monsters)) + "\n" + zone;
        return new PetCard(r.PetId, r.Name, initial, BadgeBrush(r.Species),
            string.Format(loc[r.SoulsLeft == 1 ? "Pets.SoulLeft" : "Pets.SoulsLeft"], r.SoulsLeft), detail, zone, tip,
            r.Level, r.Souls, PetLook.ProgressText(r.Level, r.Souls), loc[r.Pinned ? "Pets.UnpinTip" : "Pets.PinTip"],
            r.Pinned ? PinnedFill : ClearBrush, r.Pinned ? 1.0 : 0.7, flash ?? CardFill, (r.Pinned ? "✓ " : "") + loc["Pets.OnMap"]);
    }

    private static ZoneCard ToZone(PetZoneRow z, LocalizationManager loc)
    {
        string pets = string.Format(loc[z.PetsHere == 1 ? "Pets.ZonePet" : "Pets.ZonePets"], z.PetsHere);
        string souls = string.Format(loc[z.SoulsLeft == 1 ? "Pets.SoulN" : "Pets.SoulsN"], z.SoulsLeft);
        return new ZoneCard(z.Key, z.Name, pets + " · " + souls, loc[z.Chosen ? "Pets.Farming" : "Pets.FarmHere"],
            z.Chosen ? Accent : ClearBrush, z.Chosen ? ChosenFill : CardFill, z.Chosen ? Accent : CardFrame);
    }

    /// <summary>The three phase steps, "Obtenir (5)", "Niveau 2 (25)", "Niveau 3 (75)" with the pets there; the
    /// current phase is lit in purple.</summary>
    private void BuildSteps(PetsViewModel model)
    {
        StepsPanel.Children.Clear();
        foreach (PhaseStep step in model.Steps)
        {
            bool current = step.Level == model.Phase;
            var text = new TextBlock
            {
                Text = string.Format(Loc("Pets.PhaseStep." + step.Level), step.Reached, step.Total),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = current ? Brushes.White : MutedText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            StepsPanel.Children.Add(new Border
            {
                Child = text,
                Height = 30,
                Margin = new Thickness(0, 0, step.Level == 3 ? 0 : 6, 0),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                BorderBrush = current ? Accent : CardFrame,
                Background = current ? Accent : CardFill,
            });
        }
    }

    /// <summary>"All" and the five species as toggle chips.</summary>
    private void BuildChips()
    {
        ChipsPanel.Children.Clear();
        AddChip(Loc("Pets.Filter.All"), null);
        foreach (string species in SpeciesOrder)
        {
            AddChip(SpeciesLabel(species), species);
        }
    }

    private void AddChip(string label, string? species)
    {
        bool on = _species == species;
        var button = new Button
        {
            Tag = species ?? "",
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 4),
            Content = new Border
            {
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                BorderBrush = on ? Accent : CardFrame,
                Background = on ? Accent : ChipOff,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = on ? Brushes.White : ChipText,
                },
            },
        };
        button.Click += OnChipClick;
        ChipsPanel.Children.Add(button);
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        string tag = (string)((Button)sender).Tag;
        _species = tag.Length == 0 ? null : tag;
        Refresh();
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        int id = (int)((Button)sender).Tag;
        if (!_pins.Remove(id))
        {
            _pins.Add(id);
        }

        SaveChoices();
    }

    private void OnZoneClick(object sender, RoutedEventArgs e)
    {
        string key = (string)((Button)sender).Tag;
        _farmZone = _farmZone == key ? null : key;
        SaveChoices();
    }

    /// <summary>Saves the pins and the followed zone; every pet view follows, this window included (<see cref="PetChoices"/>).</summary>
    private void SaveChoices() => PetChoices.Save(_pins.ToList(), _farmZone);
}
