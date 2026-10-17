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

/// <summary>Builds what the Pets window shows from the collection; no WPF, so it can be tested.</summary>
public static class PetsView
{
    private static readonly int[] SoulsPerLevel = [5, 25, 75];

    public static PetsViewModel Build(Aion2PetCollection pets, string language, ISet<int> pins, string? species, string? farmZone)
    {
        IReadOnlyDictionary<int, Aion2PetState> known = pets.Pets;
        int phase = pets.Phase;

        var pinned = pins
            .Select(id => known.TryGetValue(id, out Aion2PetState? s) ? s : new Aion2PetState(id, 0, 0))
            .Where(s => !s.IsTop)
            .ToList();
        bool pinnedOnly = pinned.Count > 0;

        IEnumerable<PetRow> built = pinnedOnly
            ? pinned.Select(s => Row(s, language, s.Needed - s.Souls, pinned: true))
            : pets.Needed(phase).Select(s => Row(s, language, ToPhase(s, phase), pinned: pins.Contains(s.PetId)));

        var rows = built
            .Where(r => species is null || r.Species == species)
            .OrderBy(r => r.SoulsLeft)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();

        var zones = new List<PetZoneRow>();
        foreach (PetZone zone in Aion2PetZones.All)
        {
            var here = rows.Where(r => zone.Pets.Contains(r.PetId)).ToList();
            if (here.Count > 0)
            {
                zones.Add(new PetZoneRow(zone.Key, zone.NameIn(language), zone.Map, here.Count, here.Sum(r => r.SoulsLeft), zone.Key == farmZone));
            }
        }

        zones = zones.OrderBy(z => z.SoulsLeft).ThenBy(z => z.Name, StringComparer.CurrentCulture).ToList();

        int total = Aion2PetCatalog.PetIds.Count;
        var steps = new List<PhaseStep>();
        for (int level = 1; level <= 3; level++)
        {
            int reached = Aion2PetCatalog.PetIds.Count(id => known.TryGetValue(id, out Aion2PetState? s) && s.Level >= level);
            steps.Add(new PhaseStep(level, reached, total));
        }

        return new PetsViewModel(phase, steps, rows, zones, pinnedOnly, rows.Sum(r => r.SoulsLeft));
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
