using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>Where a pet's souls can be farmed (<see cref="Aion2PetZones.SourceOf"/>).</summary>
public enum PetSource
{
    /// <summary>Monsters drop it on the Elyos side (Verteron, the Abyss), or on a map the game's list does not cover.</summary>
    Farm,

    /// <summary>Only Altgard's monsters drop it: the Asmodian side, through a rift (the game does not show it to an Elyos).</summary>
    AltgardOnly,

    /// <summary>Only monsters of dungeons (instances, sealed dungeons) drop it.</summary>
    Dungeon,

    /// <summary>Only monsters of field events (the Defence events...) drop it.</summary>
    FieldEvent,

    /// <summary>No monster drops it and the shop sells it.</summary>
    ShopOnly,

    /// <summary>No monster drops it and the shop does not sell it (the Black Dragon: an event or a reward).</summary>
    Other,
}

/// <summary>
/// A place where pet monsters live. <see cref="Map"/> is "World_L_A" (Verteron), "World_L_B" (Eltnen),
/// "World_L_Starter", "Abyss_Reshanta_A", "_C" or "_D" (the lower, middle and upper Abyss).
/// </summary>
public sealed record PetZone(string Key, string Map, IReadOnlyDictionary<string, string> Names, IReadOnlyList<int> Pets)
{
    /// <summary>The zone's name in <paramref name="language"/>, else English, else the key.</summary>
    public string NameIn(string language)
    {
        if (Names.TryGetValue(language, out string? name) && name.Length > 0)
        {
            return name;
        }

        return Names.TryGetValue("en", out string? english) && english.Length > 0 ? english : Key;
    }
}

/// <summary>
/// Where each pet's monsters live (Elyos side: Verteron sub-zones, the whole of Eltnen, the starter zone and the
/// Abyss), from assets/aion2/pets/{regions,spawns,monster_names}.json (data collected by SkeeveTV, MIT - see the
/// README next to the files). Loaded on first use; a missing or broken file simply yields empty results.
/// </summary>
public static class Aion2PetZones
{
    private const string TableA = "World/World_L/World_L_A";
    private const string TableB = "World/World_L/World_L_B";
    private const string TableStarter = "World/World_L/World_L_Starter";
    private const string TableAltgard = "World/World_D/World_D_A";
    private const string TableMorheim = "World/World_D/World_D_B";
    private const string TableAbyss = "Intersever/Abyss/Abyss_Reshanta_A";
    private const string TableAbyssMiddle = "Intersever/Abyss/Abyss_Reshanta_C";
    private const string TableAbyssUpper = "Intersever/Abyss/Abyss_Reshanta_D";

    private sealed record Data(
        IReadOnlyList<PetZone> Zones,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, float[]>> Spawns,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> MonsterNames);

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets");
    private static Data? _cache;

    public static IReadOnlyList<PetZone> All => Load().Zones;

    /// <summary>The zones where this pet's monsters live (empty for an unknown pet or one with no spawn point).</summary>
    public static IReadOnlyList<PetZone> ZonesOf(int petId) => Load().Zones.Where(z => z.Pets.Contains(petId)).ToList();

    /// <summary>The names, in <paramref name="language"/> (else English), of this pet's monsters that have a name; distinct, sorted.</summary>
    public static IReadOnlyList<string> MonsterNames(int petId, string language)
    {
        Data data = Load();
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (int npc in Aion2PetCatalog.MonstersOf(petId))
        {
            if (!data.MonsterNames.TryGetValue(npc, out IReadOnlyDictionary<string, string>? byLanguage))
            {
                continue;
            }

            if ((byLanguage.TryGetValue(language, out string? name) && name.Length > 0)
                || (byLanguage.TryGetValue("en", out name) && name.Length > 0))
            {
                names.Add(name);
            }
        }

        return names.ToList();
    }

    private static Dictionary<int, HashSet<int>>? _soulMaps;

    /// <summary>Where a pet's souls drop, from the game's own "Obtention" list (soul_places.json, SkeeveTV, 2026-10-18): Verteron or
    /// the Abyss = farmed; only Altgard = <see cref="PetSource.AltgardOnly"/>. A pet the list does not name may live on a map it does
    /// not cover (Eltnen, Morheim, the middle and upper Abyss): farmed when one of its monsters spawns there; else shop only or other.</summary>
    public static PetSource SourceOf(int petId)
    {
        if (SoulMaps().TryGetValue(petId, out HashSet<int>? maps))
        {
            return maps.Contains(1010) || maps.Contains(20) || !maps.Contains(1110) ? PetSource.Farm : PetSource.AltgardOnly;
        }

        IReadOnlyList<int> monsters = Aion2PetCatalog.MonstersOf(petId);
        foreach (string table in new[] { TableB, TableStarter, TableMorheim, TableAbyssMiddle, TableAbyssUpper })
        {
            if (Load().Spawns.TryGetValue(table, out IReadOnlyDictionary<int, float[]>? onMap) && monsters.Any(onMap.ContainsKey))
            {
                return PetSource.Farm;
            }
        }

        // The game's list names open-world places only: a pet dropped in a dungeon or a field event is not "no drop"
        // (Monstre du marais: the Defence events; Murute de fumée noire: the Fire Temple; Stacy, 2026-10-18).
        var tables = SpawnTablesOf(monsters);
        if (tables.Any(t => t.Contains("FieldEvent", StringComparison.Ordinal) || t.Contains("_FE_", StringComparison.Ordinal) || t.Contains("Event", StringComparison.Ordinal)))
        {
            return PetSource.FieldEvent;
        }

        if (tables.Count > 0)
        {
            return PetSource.Dungeon;
        }

        return Aion2PetCatalog.InShop(petId) ? PetSource.ShopOnly : PetSource.Other;
    }

    private static Dictionary<int, List<string>>? _npcTables;

    /// <summary>Every spawn table of spawns.json (dungeons and events included) one of these monsters spawns on.</summary>
    private static List<string> SpawnTablesOf(IReadOnlyList<int> monsters)
    {
        if (_npcTables is null)
        {
            var byNpc = new Dictionary<int, List<string>>();
            try
            {
                using JsonDocument? doc = Open("spawns.json");
                if (doc is not null && doc.RootElement.TryGetProperty("maps", out JsonElement maps))
                {
                    foreach (JsonProperty table in maps.EnumerateObject())
                    {
                        foreach (JsonProperty npc in table.Value.EnumerateObject())
                        {
                            if (int.TryParse(npc.Name, out int id))
                            {
                                (byNpc.TryGetValue(id, out List<string>? list) ? list : byNpc[id] = new()).Add(table.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                byNpc.Clear();
            }

            _npcTables = byNpc;
        }

        return monsters.SelectMany(m => _npcTables.TryGetValue(m, out List<string>? t) ? t : []).Distinct().ToList();
    }

    private static Dictionary<int, HashSet<int>> SoulMaps()
    {
        if (_soulMaps is { } known)
        {
            return known;
        }

        var maps = new Dictionary<int, HashSet<int>>();
        try
        {
            using JsonDocument? doc = Open("soul_places.json");
            if (doc is not null)
            {
                foreach (JsonProperty pet in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(pet.Name, out int id))
                    {
                        maps[id] = pet.Value.EnumerateArray().Where(p => p.GetArrayLength() > 0).Select(p => p[0].GetInt32()).ToHashSet();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            maps.Clear(); // no list: every pet reads as farmed, as before
        }

        return _soulMaps = maps;
    }

    /// <summary>The spawn points (x, y, x, y... in world centimetres) of each npc id on a spawn table; empty if unknown.</summary>
    public static IReadOnlyDictionary<int, float[]> SpawnsOn(string spawnTable) =>
        Load().Spawns.TryGetValue(spawnTable, out IReadOnlyDictionary<int, float[]>? spawns) ? spawns : new Dictionary<int, float[]>();

    /// <summary>The spawn table key of a zone's <see cref="PetZone.Map"/>; the map itself if unknown.</summary>
    public static string SpawnTableOf(string map) => map switch
    {
        "World_L_A" => TableA,
        "World_L_B" => TableB,
        "World_L_Starter" => TableStarter,
        "World_D_A" => TableAltgard,
        "World_D_B" => TableMorheim,
        "Abyss_Reshanta_A" => TableAbyss,
        "Abyss_Reshanta_C" => TableAbyssMiddle,
        "Abyss_Reshanta_D" => TableAbyssUpper,
        _ => map,
    };

    private static Data Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var zones = new List<PetZone>();
        var spawns = new Dictionary<string, IReadOnlyDictionary<int, float[]>>();
        var monsterNames = new Dictionary<int, IReadOnlyDictionary<string, string>>();
        try
        {
            LoadMonsterNames(monsterNames);
            LoadSpawns(spawns);
            LoadRegions(zones);
            AddWholeMapZone(zones, "Eltnen", "World_L_B", new Dictionary<string, string> { ["en"] = "Eltnen", ["fr"] = "Eltnen", ["de"] = "Eltnen" }, spawns);
            AddWholeMapZone(zones, "Starter", "World_L_Starter", new Dictionary<string, string> { ["en"] = "Starter zone", ["fr"] = "Zone de départ", ["de"] = "Startgebiet" }, spawns);
            AddWholeMapZone(zones, "Morheim", "World_D_B", new Dictionary<string, string> { ["en"] = "Morheim", ["fr"] = "Morheim", ["de"] = "Morheim" }, spawns);
            AddWholeMapZone(zones, "AbyssMiddle", "Abyss_Reshanta_C", new Dictionary<string, string> { ["en"] = "Middle Abyss", ["fr"] = "Abysse intermédiaire", ["de"] = "Mittlerer Abyss" }, spawns);
            AddWholeMapZone(zones, "AbyssUpper", "Abyss_Reshanta_D", new Dictionary<string, string> { ["en"] = "Upper Abyss", ["fr"] = "Abysse supérieur", ["de"] = "Oberer Abyss" }, spawns);
        }
        catch (Exception)
        {
            // Missing or broken file: no zone data, the meter keeps working.
            zones.Clear();
            spawns.Clear();
            monsterNames.Clear();
        }

        return _cache = new Data(zones, spawns, monsterNames);
    }

    private static JsonDocument? Open(string name)
    {
        string path = Path.Combine(Folder, name);
        return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
    }

    private static void LoadMonsterNames(Dictionary<int, IReadOnlyDictionary<string, string>> target)
    {
        using JsonDocument? doc = Open("monster_names.json");
        if (doc is null)
        {
            return;
        }

        foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out int npc) || entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var names = new Dictionary<string, string>();
            foreach (JsonProperty n in entry.Value.EnumerateObject())
            {
                names[n.Name] = n.Value.GetString() ?? "";
            }

            target[npc] = names;
        }
    }

    private static void LoadSpawns(Dictionary<string, IReadOnlyDictionary<int, float[]>> target)
    {
        using JsonDocument? doc = Open("spawns.json");
        if (doc is null || !doc.RootElement.TryGetProperty("maps", out JsonElement maps))
        {
            return;
        }

        foreach (string table in new[] { TableA, TableB, TableStarter, TableAltgard, TableMorheim, TableAbyss, TableAbyssMiddle, TableAbyssUpper })
        {
            if (!maps.TryGetProperty(table, out JsonElement npcs))
            {
                continue;
            }

            var byNpc = new Dictionary<int, float[]>();
            foreach (JsonProperty entry in npcs.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out int npc))
                {
                    continue;
                }

                var xy = new List<float>();
                foreach (JsonElement point in entry.Value.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2
                        || point[0].ValueKind != JsonValueKind.Number || point[1].ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    xy.Add(point[0].GetSingle());
                    xy.Add(point[1].GetSingle());
                }

                byNpc[npc] = xy.ToArray();
            }

            target[table] = byNpc;
        }
    }

    private static void LoadRegions(List<PetZone> target)
    {
        using JsonDocument? doc = Open("regions.json");
        if (doc is null || !doc.RootElement.TryGetProperty("groups", out JsonElement groups))
        {
            return;
        }

        foreach (JsonElement group in groups.EnumerateArray())
        {
            string? map = group.GetProperty("id").GetString() switch
            {
                "verteron" => "World_L_A",
                "altgard" => "World_D_A", // the Asmodian side, reached through the rifts
                "abyss" => "Abyss_Reshanta_A",
                _ => null,
            };
            if (map is null)
            {
                continue;
            }

            foreach (JsonElement region in group.GetProperty("regions").EnumerateArray())
            {
                var names = new Dictionary<string, string>();
                foreach (JsonProperty n in region.GetProperty("names").EnumerateObject())
                {
                    names[n.Name] = n.Value.GetString() ?? "";
                }

                var pets = region.GetProperty("pets").EnumerateArray().Select(p => p.GetInt32()).ToList();
                target.Add(new PetZone(region.GetProperty("key").GetString() ?? "", map, names, pets));
            }
        }
    }

    // One zone for a whole map: the pets of every monster that has a spawn point on that spawn table.
    private static void AddWholeMapZone(List<PetZone> zones, string key, string map, Dictionary<string, string> names,
        Dictionary<string, IReadOnlyDictionary<int, float[]>> spawns)
    {
        if (!spawns.TryGetValue(SpawnTableOf(map), out IReadOnlyDictionary<int, float[]>? onMap))
        {
            return;
        }

        var pets = Aion2PetCatalog.PetIds.Where(p => Aion2PetCatalog.MonstersOf(p).Any(onMap.ContainsKey)).ToList();
        zones.Add(new PetZone(key, map, names, pets));
    }
}
