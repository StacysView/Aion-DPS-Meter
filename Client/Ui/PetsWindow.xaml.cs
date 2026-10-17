using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pets still to raise (first version): a header with the phase and the souls left, then one card
/// per pet below the phase's target level, the closest to done first. Follows the collection live.
/// Pet data: SkeeveTV (MIT) - see Aion2PetCatalog.
/// </summary>
public partial class PetsWindow : Window
{
    /// <summary>Souls to give to go from level 0 / 1 / 2 to the next one.</summary>
    private static readonly int[] CostPerLevel = { 5, 25, 75 };

    private readonly Aion2PetCollection _pets;

    public PetsWindow(Aion2PetCollection pets)
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        ThemedChrome.Apply(this);
        _pets = pets;
        // The collection raises its events on the capture thread: hop to the UI thread, never wait for it.
        Action later = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
        Action<Aion2PetState, bool> onChanged = (_, _) => later();
        _pets.Changed += onChanged;
        _pets.ListReplaced += later;
        Closed += (_, _) =>
        {
            _pets.Changed -= onChanged;
            _pets.ListReplaced -= later;
        };
        Refresh();
    }

    /// <summary>One card: the pet, its level, the souls towards the next level.</summary>
    private sealed record PetCard(int PetId, string Name, string Species, string LevelText, string LeftText, int Souls, int Needed,
        string BarText, int Left);

    /// <summary>Souls still to give for a pet to reach <paramref name="phase"/>.</summary>
    private static int SoulsLeft(Aion2PetState pet, int phase)
    {
        int left = -pet.Souls;
        for (int level = pet.Level; level < phase && level < CostPerLevel.Length; level++)
        {
            left += CostPerLevel[level];
        }

        return Math.Max(left, 0);
    }

    private static string SpeciesLabel(int petId)
    {
        if (Aion2PetCatalog.Species(petId) is not { } species)
        {
            return "";
        }

        string key = "Pets.Species." + species;
        string named = LocalizationManager.Instance[key];
        return named != key ? named : species;
    }

    /// <summary>The header, the cards and the empty text again from the collection.</summary>
    public void Refresh()
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        CultureInfo culture = CultureInfo.CurrentCulture;
        int phase = _pets.Phase;
        IReadOnlyList<Aion2PetState> needed = _pets.Needed(phase);
        DateTime? listedAt = _pets.ListedAt;
        int total = Aion2PetCatalog.PetIds.Count;
        var catalog = Aion2PetCatalog.PetIds.ToHashSet();
        // Catalog pets already at the phase's level (a missing one counts as level 0).
        int done = total - needed.Count(p => catalog.Contains(p.PetId));

        List<PetCard> cards = needed
            .Select(p =>
            {
                int left = SoulsLeft(p, phase);
                return new PetCard(p.PetId, Aion2PetCatalog.Name(p.PetId, language), SpeciesLabel(p.PetId),
                    string.Format(loc[p.Level == 0 ? "Pets.NotYet" : "Pets.Level"], p.Level),
                    string.Format(loc[left == 1 ? "Pets.SoulLeft" : "Pets.SoulsLeft"], left),
                    p.Souls, p.Needed, $"{p.Souls}/{p.Needed}", left);
            })
            .OrderBy(c => c.Left).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        int soulsLeft = cards.Sum(c => c.Left);

        if (listedAt is null)
        {
            HeaderText.Text = loc["Pets.Title"];
            SubText.Text = "";
            EmptyText.Text = loc["Pets.EmptyNoList"];
        }
        else
        {
            string progress = string.Format(loc[phase == 1 ? "Pets.Header.Obtained" : "Pets.Header.Level"], phase, done, total);
            string souls = string.Format(loc[soulsLeft == 1 ? "Pets.SoulLeft" : "Pets.SoulsLeft"], soulsLeft);
            HeaderText.Text = cards.Count == 0 ? progress : progress + " · " + souls;
            SubText.Text = listedAt is { } at
                ? string.Format(loc["Pets.ListedAt"], at.ToString("d MMM", culture) + " " + at.ToString("t", culture))
                : "";
            EmptyText.Text = cards.Count == 0 ? loc["Pets.AllDone"] : "";
        }

        PetsList.ItemsSource = cards;
        EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
