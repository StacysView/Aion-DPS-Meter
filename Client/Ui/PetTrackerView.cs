using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One spawn point of a needed pet's monster, in pixels of the whole map.</summary>
public sealed record TrackerDot(int PetId, double PixelX, double PixelY);

/// <summary>One needed pet of the shown zone; <see cref="SpawnCount"/> is its number of dots on the map.</summary>
public sealed record TrackerPet(int PetId, string Name, int Level, int Souls, int Needed, int SpawnCount);

/// <summary>What the map and list panels show. <see cref="Map"/> is null where there is no map (a dungeon, a zone
/// not known yet): no dots, no pets. <see cref="Player"/> is the player's pixel on <see cref="Map"/>, null while the
/// place is unknown or when the player stands on another map than the one shown.</summary>
public sealed record TrackerModel(Aion2WorldMap? Map, string ZoneName, (double X, double Y)? Player, IReadOnlyList<TrackerDot> Dots, IReadOnlyList<TrackerPet> Pets);

/// <summary>
/// Builds what the pet panels over the game show; no WPF, so it can be tested. The needed pets are the rows of
/// <see cref="PetsView.Build"/> with the current pins (pins: those only; else the phase list), in its order (souls
/// left). The zone is the farm zone chosen in the Pets window when it has a map (the Abyss has none), else the
/// player's map with all its needed pets. A dot is drawn only for a spawn point of that map's own spawn table, so a
/// pet that lives on another map is never drawn on the wrong one. Spawn data: SkeeveTV (MIT).
/// </summary>
public static class PetTrackerView
{
    /// <summary>Two spawn points of the same pet closer than this (map pixels) are one dot.</summary>
    private const double DedupePixels = 5;

    private static readonly TrackerModel Nothing = new(null, "", null, [], []);

    public static TrackerModel Build(Aion2PetCollection pets, Aion2PlayerPosition position, string language, ISet<int> pins, string? farmZone)
    {
        IReadOnlyList<PetRow> rows = PetsView.Build(pets, language, pins, null, null).Rows;
        Aion2WorldMap? playerMap = position.Map;

        PetZone? farm = farmZone is null ? null : Aion2PetZones.All.FirstOrDefault(z => z.Key == farmZone);
        Aion2WorldMap? farmMap = farm is null ? null : Aion2WorldMaps.Get(farm.Map);

        Aion2WorldMap map;
        string zoneName;
        IEnumerable<PetRow> candidates;
        bool keepEmpty;
        if (farm is not null && farmMap is not null)
        {
            map = farmMap;
            zoneName = farm.NameIn(language);
            candidates = rows.Where(r => farm.Pets.Contains(r.PetId));
            keepEmpty = true; // the zone's own pets, even one with no point on the map
        }
        else if (playerMap is not null)
        {
            map = playerMap;
            zoneName = MapName(playerMap, language);
            candidates = rows;
            keepEmpty = false; // the whole map: only the pets that live on it
        }
        else
        {
            return Nothing;
        }

        IReadOnlyDictionary<int, float[]> spawns = Aion2PetZones.SpawnsOn(map.SpawnTable);
        var dots = new List<TrackerDot>();
        var shown = new List<TrackerPet>();
        foreach (PetRow row in candidates)
        {
            int before = dots.Count;
            AddDots(row.PetId, map, spawns, dots);
            int count = dots.Count - before;
            if (count > 0 || keepEmpty)
            {
                shown.Add(new TrackerPet(row.PetId, row.Name, row.Level, row.Souls, row.Needed, count));
            }
        }

        (double X, double Y)? player = null;
        if (playerMap?.Key == map.Key && position.Last is { } last)
        {
            player = map.ToPixel(last.X, last.Y);
        }

        return new TrackerModel(map, zoneName, player, dots, shown);
    }

    /// <summary>The name of a whole map: Verteron, else the whole-map zone of <see cref="Aion2PetZones"/> (Eltnen, the starter zone).</summary>
    public static string MapName(Aion2WorldMap map, string language) => map.Key switch
    {
        "World_L_A" => "Verteron",
        "World_L_B" => WholeZoneName("Eltnen", language) ?? "Eltnen",
        "World_L_Starter" => WholeZoneName("Starter", language) ?? map.Key,
        _ => map.Key,
    };

    private static string? WholeZoneName(string key, string language) =>
        Aion2PetZones.All.FirstOrDefault(z => z.Key == key)?.NameIn(language);

    // The spawn points of the pet's monsters on this map, inside the map, one dot per 5 px (a grid of 5 px cells).
    private static void AddDots(int petId, Aion2WorldMap map, IReadOnlyDictionary<int, float[]> spawns, List<TrackerDot> dots)
    {
        var cells = new Dictionary<(int, int), List<(double X, double Y)>>();
        int size = map.MapPixels;
        foreach (int npc in Aion2PetCatalog.MonstersOf(petId))
        {
            if (!spawns.TryGetValue(npc, out float[]? xy))
            {
                continue;
            }

            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                (double px, double py) = map.ToPixel(xy[i], xy[i + 1]);
                if (!(px >= 0 && px < size && py >= 0 && py < size))
                {
                    continue;
                }

                int cx = (int)(px / DedupePixels), cy = (int)(py / DedupePixels);
                if (Near(cells, cx, cy, px, py))
                {
                    continue;
                }

                if (!cells.TryGetValue((cx, cy), out List<(double X, double Y)>? cell))
                {
                    cells[(cx, cy)] = cell = new List<(double X, double Y)>();
                }

                cell.Add((px, py));
                dots.Add(new TrackerDot(petId, px, py));
            }
        }
    }

    private static bool Near(Dictionary<(int, int), List<(double X, double Y)>> cells, int cx, int cy, double px, double py)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!cells.TryGetValue((cx + dx, cy + dy), out List<(double X, double Y)>? cell))
                {
                    continue;
                }

                foreach ((double x, double y) in cell)
                {
                    if ((x - px) * (x - px) + (y - py) * (y - py) < DedupePixels * DedupePixels)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
