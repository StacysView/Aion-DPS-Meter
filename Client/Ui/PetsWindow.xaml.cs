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

    /// <summary>A muted badge colour per species, picked to sit with the purple theme.</summary>
    private static readonly Dictionary<string, Color> BadgeColors = new()
    {
        ["Intellect"] = Color.FromRgb(0x5B, 0x7F, 0xD6),
        ["Feral"] = Color.FromRgb(0xC2, 0x6A, 0x5A),
        ["Nature"] = Color.FromRgb(0x4F, 0x9A, 0x74),
        ["Trans"] = Color.FromRgb(0xB5, 0x6F, 0xC9),
        ["Special"] = Color.FromRgb(0xC9, 0x9A, 0x45),
    };

    private static readonly Brush CardFill = Frozen(Color.FromRgb(0x1A, 0x24, 0x30));
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

        // The collection raises its events on the capture thread: hop to the UI thread, never wait for it.
        Action later = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, ScheduleRefresh);
        Action<Aion2PetState, bool> onChanged = (_, _) => later();
        Action<Aion2PetState, int> onLevel = (_, _) => later();
        _pets.Changed += onChanged;
        _pets.LevelRaised += onLevel;
        _pets.ListReplaced += later;
        Closed += (_, _) =>
        {
            _pets.Changed -= onChanged;
            _pets.LevelRaised -= onLevel;
            _pets.ListReplaced -= later;
        };
        Refresh();
    }

    /// <summary>One pet card as bound by the XAML template.</summary>
    public sealed record PetCard(int PetId, string Name, string Initial, Brush Badge, string LevelText, string LeftText, string Detail,
        int Souls, int Needed, string BarText, string PinTip, Brush PinBackground, double PinOpacity);

    /// <summary>One zone card as bound by the XAML template.</summary>
    public sealed record ZoneCard(string Key, string Name, string Line, string ButtonText, Brush ButtonFill, Brush Fill, Brush Frame);

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
        var timer = new DispatcherTimer { Interval = RebuildGap - since };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _rebuildQueued = false;
            Refresh();
        };
        timer.Start();
    }

    /// <summary>Rebuilds the whole window from the collection and the current filters.</summary>
    public void Refresh()
    {
        _lastBuild = DateTime.UtcNow;
        var loc = LocalizationManager.Instance;
        CultureInfo culture = CultureInfo.CurrentCulture;
        PetsViewModel model = PetsView.Build(_pets, loc.Language, _pins, _species, _farmZone);
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
        PinnedNote.Visibility = model.PinnedOnly ? Visibility.Visible : Visibility.Collapsed;

        double petsAt = PetsScroll.VerticalOffset, zonesAt = ZonesScroll.VerticalOffset;
        PetsList.ItemsSource = model.Rows.Select(r => ToCard(r, loc)).ToList();
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

    private static PetCard ToCard(PetRow r, LocalizationManager loc)
    {
        string trimmed = r.Name.TrimStart('#', ' ');
        string initial = trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
        Color color = r.Species is not null && BadgeColors.TryGetValue(r.Species, out Color c) ? c : Color.FromRgb(0x6B, 0x7A, 0x8A);
        string monsters = r.Monsters.Count == 0 ? "—"
            : string.Join(", ", r.Monsters.Take(2)) + (r.Monsters.Count > 2 ? $" +{r.Monsters.Count - 2}" : "");
        string species = SpeciesLabel(r.Species);
        string detail = (species.Length > 0 ? species + " · " : "") + monsters + " · " + (r.Zone ?? "—");
        return new PetCard(r.PetId, r.Name, initial, Frozen(color), string.Format(loc["Pets.LevelShort"], r.Level),
            string.Format(loc[r.SoulsLeft == 1 ? "Pets.SoulLeft" : "Pets.SoulsLeft"], r.SoulsLeft), detail,
            r.Souls, r.Needed, $"{r.Souls}/{r.Needed}", loc[r.Pinned ? "Pets.UnpinTip" : "Pets.PinTip"],
            r.Pinned ? PinnedFill : ClearBrush, r.Pinned ? 1.0 : 0.65);
    }

    private static ZoneCard ToZone(PetZoneRow z, LocalizationManager loc)
    {
        string pets = string.Format(loc[z.PetsHere == 1 ? "Pets.ZonePet" : "Pets.ZonePets"], z.PetsHere);
        string souls = string.Format(loc[z.SoulsLeft == 1 ? "Pets.SoulN" : "Pets.SoulsN"], z.SoulsLeft);
        return new ZoneCard(z.Key, z.Name, pets + " · " + souls, loc[z.Chosen ? "Pets.Farming" : "Pets.FarmHere"],
            z.Chosen ? Accent : ClearBrush, z.Chosen ? ChosenFill : CardFill, z.Chosen ? Accent : CardFrame);
    }

    /// <summary>The three phase steps; the current phase is lit in purple.</summary>
    private void BuildSteps(PetsViewModel model)
    {
        StepsPanel.Children.Clear();
        foreach (PhaseStep step in model.Steps)
        {
            bool current = step.Level == model.Phase;
            var text = new TextBlock
            {
                Text = string.Format(Loc("Pets.PhaseStep"), step.Level, step.Reached, step.Total),
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
        Refresh();
    }

    private void OnZoneClick(object sender, RoutedEventArgs e)
    {
        string key = (string)((Button)sender).Tag;
        _farmZone = _farmZone == key ? null : key;
        SaveChoices();
        Refresh();
    }

    /// <summary>Writes the pins and the followed zone into the settings file, keeping every other setting as it is on disk.</summary>
    private void SaveChoices()
    {
        try
        {
            MeterSettings onDisk = MeterSettings.Load();
            onDisk.PetPins = _pins.OrderBy(i => i).ToList();
            onDisk.PetFarmZone = _farmZone;
            onDisk.Save();
        }
        catch (IOException)
        {
            // A locked settings file must not break the window; the choice stays for this session.
        }
    }
}
