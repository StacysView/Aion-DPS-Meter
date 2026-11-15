using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>A Kibelisk (teleport artifact): its number in the game's own list (map window, "Kibelisques" tab), its name and where it stands (cm).</summary>
public sealed record Aion2Teleport(int Number, string Name, double X, double Y);

/// <summary>
/// The teleports of the map windows, numbered as the game lists them (Stacy, 2026-11-15: "Kaaira dans 17 min - TP 56"). Data in assets/aion2/maps/kibelisk_numbers.json:
/// the numbers come from the game's list, the places from SkeeveAN's Kibelisk data (MIT). A map without a numbered list gives no teleport.
/// </summary>
public static class Aion2Teleports
{
    private static Dictionary<string, List<Aion2Teleport>>? _byMap;

    /// <summary>The teleports of a map (by map key, e.g. World_L_A); empty when the numbers of that map are not known.</summary>
    public static IReadOnlyList<Aion2Teleport> Of(string mapKey) =>
        (_byMap ??= Load()).TryGetValue(mapKey, out List<Aion2Teleport>? list) ? list : [];

    /// <summary>The teleport nearest to a place of the map, or null when the map has none.</summary>
    public static Aion2Teleport? Nearest(string mapKey, double x, double y)
    {
        Aion2Teleport? best = null;
        double bestDistance = double.MaxValue;
        foreach (Aion2Teleport t in Of(mapKey))
        {
            double d = (t.X - x) * (t.X - x) + (t.Y - y) * (t.Y - y);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = t;
            }
        }

        return best;
    }

    private static Dictionary<string, List<Aion2Teleport>> Load()
    {
        var result = new Dictionary<string, List<Aion2Teleport>>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "kibelisk_numbers.json");
            if (!File.Exists(path))
            {
                return result;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonProperty map in doc.RootElement.GetProperty("maps").EnumerateObject())
            {
                var list = new List<Aion2Teleport>();
                foreach (JsonElement k in map.Value.EnumerateArray())
                {
                    JsonElement names = k.GetProperty("names");
                    list.Add(new Aion2Teleport(k.GetProperty("n").GetInt32(), names.TryGetProperty("fr", out JsonElement fr) ? fr.GetString() ?? "" : "", k.GetProperty("x").GetDouble(), k.GetProperty("y").GetDouble()));
                }

                result[map.Name] = list;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no numbered list: the boss lines simply carry no teleport
        }

        return result;
    }
}
