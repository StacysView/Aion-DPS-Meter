using System.Globalization;
using System.Text;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One pet card: <see cref="SoulsLeft"/> is what is still needed to reach the phase's target level
/// (a pinned pet aims at its next level instead). <see cref="Zone"/> is the first zone's name, null when
/// none of the pet's monsters has a known spawn.</summary>
public sealed record PetRow(int PetId, string Name, string? Species, int Level, int Souls, int Needed, int SoulsLeft,
    IReadOnlyList<string> Monsters, string? Zone, bool Pinned);

/// <summary>A place worth farming: how many listed pets live there and the souls they still need.</summary>
public sealed record PetZoneRow(string Key, string Name, string Map, int PetsHere, int SoulsLeft, bool Chosen);

/// <summary>Progress towards a level: catalog pets at <see cref="Level"/> or more, out of <see cref="Total"/>.</summary>
public sealed record PhaseStep(int Level, int Reached, int Total);

public sealed record PetsViewModel(int Phase, IReadOnlyList<PhaseStep> Steps, IReadOnlyList<PetRow> Rows,
    IReadOnlyList<PetZoneRow> Zones, bool PinnedOnly, int SoulsLeftTotal);

/// <summary>The pet cards of one species in the Pets window, in the list's own order (souls left, then name).</summary>
public sealed record PetGroup(string Species, IReadOnlyList<PetRow> Rows);

/// <summary>Builds what the Pets window shows from the collection; no WPF, so it can be tested.</summary>
public static class PetsView
{
    private static readonly int[] SoulsPerLevel = [5, 25, 75];

    /// <summary>The five species in the order of the chips and of the groups (the catalog's own names).</summary>
    public static readonly IReadOnlyList<string> SpeciesOrder = ["Intellect", "Feral", "Nature", "Trans", "Special"];

    /// <summary>Groups the rows by species (<see cref="SpeciesOrder"/>, a species the catalog does not know after them),
    /// keeping their order inside a group. <paramref name="species"/> keeps one species only; <paramref name="search"/>
    /// keeps the rows whose name, monsters or zone hold every word of it, case and accents ignored (empty = all).
    /// Empty groups are left out.</summary>
    public static IReadOnlyList<PetGroup> Group(IEnumerable<PetRow> rows, string? species, string? search)
    {
        string[] words = Fold(search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = rows
            .Where(r => species is null || r.Species == species)
            .Where(r => words.Length == 0 || Matches(r, words))
            .ToList();

        var order = SpeciesOrder.ToList();
        foreach (string? other in kept.Select(r => r.Species).Distinct())
        {
            if (!order.Contains(other ?? ""))
            {
                order.Add(other ?? "");
            }
        }

        return order
            .Select(s => new PetGroup(s, kept.Where(r => (r.Species ?? "") == s).ToList()))
            .Where(g => g.Rows.Count > 0)
            .ToList();
    }

    /// <summary>True when every word is in the pet's name, one of its monsters' names or its zone.</summary>
    private static bool Matches(PetRow row, string[] words)
    {
        string haystack = Fold(string.Join(" | ", row.Monsters.Prepend(row.Name).Append(row.Zone ?? "")));
        return words.All(w => haystack.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>Lower case, accents dropped ("Araignée" -> "araignee"), runs of blanks made one space.</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool blank = false;
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                blank = true;
                continue;
            }

            if (blank && sb.Length > 0)
            {
                sb.Append(' ');
            }

            blank = false;
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <param name="phase">The level the rows lead to; null = the collection's own phase. The panels over the game pass 3:
    /// a pet's spawns stay on the map until it reaches the top level (Stacy, 2026-10-17), not only while it is in the phase.</param>
    /// <param name="pinsFilter">False for the Pets window: the pins only say which pets the map shows ("Sur la carte"), the
    /// list itself stays the whole phase (Stacy, 2026-10-17).</param>
    public static PetsViewModel Build(Aion2PetCollection pets, string language, ISet<int> pins, string? species, string? farmZone, int? phase = null,
        bool pinsFilter = true)
    {
        IReadOnlyDictionary<int, Aion2PetState> known = pets.Pets;
        int ownPhase = pets.Phase;
        int target = phase ?? ownPhase;

        var pinned = pins
            .Select(id => known.TryGetValue(id, out Aion2PetState? s) ? s : new Aion2PetState(id, 0, 0))
            .Where(s => !s.IsTop)
            .ToList();
        bool pinnedOnly = pinsFilter && pinned.Count > 0;

        IEnumerable<PetRow> built = pinnedOnly
            ? pinned.Select(s => Row(s, language, s.Needed - s.Souls, pinned: true))
            : pets.Needed(target).Select(s => Row(s, language, ToPhase(s, target), pinned: pins.Contains(s.PetId)));

        var rows = built
            .Where(r => species is null || r.Species == species)
            .OrderBy(r => r.SoulsLeft)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();

        IReadOnlyList<PetZoneRow> zones = ZonesFor(rows, language, farmZone);

        int total = Aion2PetCatalog.PetIds.Count;
        var steps = new List<PhaseStep>();
        for (int level = 1; level <= 3; level++)
        {
            int reached = Aion2PetCatalog.PetIds.Count(id => known.TryGetValue(id, out Aion2PetState? s) && s.Level >= level);
            steps.Add(new PhaseStep(level, reached, total));
        }

        return new PetsViewModel(ownPhase, steps, rows, zones, pinnedOnly, rows.Sum(r => r.SoulsLeft));
    }

    /// <summary>The places worth farming for these rows: the zones where at least one of them lives, with how many
    /// and the souls they still need, most pets first. The Pets window passes the rows left by the search
    /// (<see cref="Group"/>), so the column follows what is typed.</summary>
    public static IReadOnlyList<PetZoneRow> ZonesFor(IEnumerable<PetRow> rows, string language, string? farmZone)
    {
        var list = rows.ToList();
        var zones = new List<PetZoneRow>();
        foreach (PetZone zone in Aion2PetZones.All)
        {
            var here = list.Where(r => zone.Pets.Contains(r.PetId)).ToList();
            if (here.Count > 0)
            {
                zones.Add(new PetZoneRow(zone.Key, zone.NameIn(language), zone.Map, here.Count, here.Sum(r => r.SoulsLeft), zone.Key == farmZone));
            }
        }

        return zones.OrderByDescending(z => z.PetsHere).ThenBy(z => z.SoulsLeft).ThenBy(z => z.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Souls still to give to reach level <paramref name="phase"/>.</summary>
    private static int ToPhase(Aion2PetState state, int phase)
    {
        int left = -state.Souls;
        for (int level = state.Level; level < phase && level < SoulsPerLevel.Length; level++)
        {
            left += SoulsPerLevel[level];
        }

        return Math.Max(0, left);
    }

    private static PetRow Row(Aion2PetState state, string language, int soulsLeft, bool pinned)
    {
        PetZone? zone = Aion2PetZones.ZonesOf(state.PetId).FirstOrDefault();
        return new PetRow(state.PetId, Aion2PetCatalog.Name(state.PetId, language), Aion2PetCatalog.Species(state.PetId),
            state.Level, state.Souls, state.Needed, Math.Max(0, soulsLeft),
            Aion2PetZones.MonsterNames(state.PetId, language), zone?.NameIn(language), pinned);
    }
}
