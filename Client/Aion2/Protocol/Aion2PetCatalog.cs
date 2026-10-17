using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 pets, from assets/aion2/pets/pets.json (data collected by SkeeveTV, MIT - see the README next to
/// the file). Gives a pet's name per language, its species, the soul item that feeds it, the monsters
/// that drop its soul and the pet shop's items. Loaded on first use; a missing or broken file simply yields empty results.
/// </summary>
public static class Aion2PetCatalog
{
    private sealed record Pet(string Species, IReadOnlyDictionary<string, string> Names);

    private sealed record Data(
        IReadOnlyDictionary<int, Pet> Pets,
        IReadOnlyDictionary<int, int> Souls,
        IReadOnlyDictionary<int, List<int>> MonstersByPet,
        IReadOnlyDictionary<int, int> ShopItems);

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "pets.json");
    private static Data? _cache;

    public static IReadOnlyList<int> PetIds => _petIds ??= Load().Pets.Keys.Order().ToList();
    private static IReadOnlyList<int>? _petIds;

    /// <summary>The pet's name in <paramref name="language"/>, else English, else "#id".</summary>
    public static string Name(int petId, string language)
    {
        if (Load().Pets.TryGetValue(petId, out Pet? pet))
        {
            if (pet.Names.TryGetValue(language, out string? name) && name.Length > 0)
            {
                return name;
            }

            if (pet.Names.TryGetValue("en", out string? english) && english.Length > 0)
            {
                return english;
            }
        }

        return "#" + petId;
    }

    /// <summary>"Intellect", "Feral", "Nature", "Trans" or "Special" (the file's category); null if unknown.</summary>
    public static string? Species(int petId) => Load().Pets.TryGetValue(petId, out Pet? pet) ? pet.Species : null;

    /// <summary>The pet fed by a soul item, or null.</summary>
    public static int? PetOfSoulItem(int itemId) => Load().Souls.TryGetValue(itemId, out int pet) ? pet : null;

    /// <summary>The pet an item of the in-game pet shop stands for (item id from the shop frame 0x9156), or null for any
    /// other item. The file's "shop" table is SkeeveTV's (MIT).</summary>
    public static int? PetOfShopItem(int itemId) => Load().ShopItems.TryGetValue(itemId, out int pet) ? pet : null;

    /// <summary>Whether the Black Cloud shop's table lists this pet.</summary>
    public static bool InShop(int petId) => Load().ShopItems.Values.Contains(petId);

    /// <summary>The monster (npc) ids that drop this pet's soul.</summary>
    public static IReadOnlyList<int> MonstersOf(int petId) =>
        Load().MonstersByPet.TryGetValue(petId, out List<int>? list) ? list : [];

    private static Data Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var pets = new Dictionary<int, Pet>();
        var souls = new Dictionary<int, int>();
        var monsters = new Dictionary<int, List<int>>();
        var shop = new Dictionary<int, int>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("pets", out JsonElement petsNode))
                {
                    foreach (JsonProperty entry in petsNode.EnumerateObject())
                    {
                        if (!int.TryParse(entry.Name, out int id))
                        {
                            continue;
                        }

                        var names = new Dictionary<string, string>();
                        if (entry.Value.TryGetProperty("names", out JsonElement namesNode))
                        {
                            foreach (JsonProperty n in namesNode.EnumerateObject())
                            {
                                names[n.Name] = n.Value.GetString() ?? "";
                            }
                        }

                        string species = entry.Value.TryGetProperty("category", out JsonElement c) ? c.GetString() ?? "" : "";
                        pets[id] = new Pet(species, names);
                    }
                }

                if (root.TryGetProperty("souls", out JsonElement soulsNode))
                {
                    foreach (JsonProperty entry in soulsNode.EnumerateObject())
                    {
                        if (int.TryParse(entry.Name, out int item) && entry.Value.TryGetInt32(out int pet))
                        {
                            souls[item] = pet;
                        }
                    }
                }

                if (root.TryGetProperty("shop", out JsonElement shopNode))
                {
                    foreach (JsonProperty entry in shopNode.EnumerateObject())
                    {
                        if (int.TryParse(entry.Name, out int item) && entry.Value.TryGetInt32(out int pet))
                        {
                            shop[item] = pet;
                        }
                    }
                }

                if (root.TryGetProperty("monsters", out JsonElement monstersNode))
                {
                    foreach (JsonProperty entry in monstersNode.EnumerateObject())
                    {
                        if (!int.TryParse(entry.Name, out int npc))
                        {
                            continue;
                        }

                        // A monster maps to one pet id or to a list of them.
                        IEnumerable<JsonElement> values = entry.Value.ValueKind == JsonValueKind.Array
                            ? entry.Value.EnumerateArray()
                            : [entry.Value];
                        foreach (JsonElement v in values)
                        {
                            if (v.TryGetInt32(out int pet))
                            {
                                if (!monsters.TryGetValue(pet, out List<int>? list))
                                {
                                    monsters[pet] = list = [];
                                }

                                list.Add(npc);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Missing or broken file: no pet data, the meter keeps working.
            pets.Clear();
            souls.Clear();
            monsters.Clear();
            shop.Clear();
        }

        return _cache = new Data(pets, souls, monsters, shop);
    }
}
