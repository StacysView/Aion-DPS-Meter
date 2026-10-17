using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>
/// The name of an item for the loot page: the player's own name (aion2-item-names.json) first, then the translated name
/// (assets/aion2/items/item_names_i18n.json, built by Tools/loot/build_item_names.py from SkeeveAN's translations, MIT), then the English name
/// of the item table, then "Soul: pet" for a soul, then "Item id".
/// </summary>
public static class Aion2ItemNames
{
    private static readonly object Gate = new();
    private static Dictionary<int, Dictionary<string, string>>? _i18n;
    private static Dictionary<int, int>? _souls;
    private static Dictionary<int, Dictionary<string, string>>? _pets;
    private static readonly Dictionary<int, string> UserNames = new();
    private static bool _persist;
    private static bool _loadedUser;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-item-names.json");

    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            LoadUser();
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            UserNames.Clear();
            _persist = false;
            _loadedUser = false;
        }
    }

    public static string Name(int itemId, string language)
    {
        lock (Gate)
        {
            if (UserNames.TryGetValue(itemId, out string? user))
            {
                return user;
            }

            _i18n ??= LoadI18n();
            if (language != "en" && _i18n.TryGetValue(itemId, out var tr) && tr.TryGetValue(language, out string? translated) && translated.Length > 0)
            {
                return translated;
            }

            if (Aion2ItemCatalog.Find(itemId) is { } info && info.Name.Length > 0)
            {
                return info.Name;
            }

            LoadPets();
            if (_souls!.TryGetValue(itemId, out int pet) && _pets!.TryGetValue(pet, out var petNames))
            {
                string petName = petNames.GetValueOrDefault(language) ?? petNames.GetValueOrDefault("en") ?? petNames.Values.FirstOrDefault() ?? "";
                if (petName.Length > 0)
                {
                    return (language switch { "fr" => "Âme : ", "de" => "Seele: ", _ => "Soul: " }) + petName;
                }
            }

            return language switch { "fr" => "Objet " + itemId, "de" => "Gegenstand " + itemId, _ => "Item " + itemId };
        }
    }

    /// <summary>Gives an item the player's own name (null or empty takes it back).</summary>
    public static void SetUserName(int itemId, string? name)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                UserNames.Remove(itemId);
            }
            else
            {
                UserNames[itemId] = name.Trim();
            }

            if (!_persist)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                string temp = StorePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(UserNames.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)));
                File.Move(temp, StorePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the name stays for this run
            }
        }
    }

    private static void LoadUser()
    {
        if (_loadedUser)
        {
            return;
        }

        _loadedUser = true;
        try
        {
            if (File.Exists(StorePath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath)) is { } saved)
            {
                foreach (var (key, value) in saved)
                {
                    if (int.TryParse(key, out int id) && !string.IsNullOrWhiteSpace(value))
                    {
                        UserNames[id] = value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // no own names
        }
    }

    private static Dictionary<int, Dictionary<string, string>> LoadI18n()
    {
        var table = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "items", "item_names_i18n.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty e in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(e.Name, out int id))
                    {
                        table[id] = e.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // untranslated names
        }

        return table;
    }

    /// <summary>assets/aion2/pets/pets.json (when it exists): <c>{"pets": {"1117": {"names": {"fr": ...}}}, "souls": {"534660086": 1117}}</c>.</summary>
    private static void LoadPets()
    {
        if (_souls is not null)
        {
            return;
        }

        _souls = new Dictionary<int, int>();
        _pets = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "pets.json");
            if (!File.Exists(path))
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("pets", out JsonElement pets))
            {
                foreach (JsonProperty p in pets.EnumerateObject())
                {
                    if (int.TryParse(p.Name, out int id) && p.Value.TryGetProperty("names", out JsonElement names))
                    {
                        _pets[id] = names.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "");
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("souls", out JsonElement souls))
            {
                foreach (JsonProperty s in souls.EnumerateObject())
                {
                    if (int.TryParse(s.Name, out int item) && s.Value.TryGetInt32(out int pet))
                    {
                        _souls[item] = pet;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // no soul names
        }
    }
}
