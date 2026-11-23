using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The last boss of each dungeon (assets/aion2/bosses/dungeon_finals.json, written by Tools/make_dungeon_finals.py from the boss table): the Monsters theme's battle music plays for these.
/// Ludra's last boss has its own music and is not listed.
/// </summary>
public static class Aion2DungeonFinals
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "bosses", "dungeon_finals.json");
    private static Dictionary<string, HashSet<string>>? _cache;

    /// <summary>Whether this boss (an NPC id of the boss table) is the last boss of its dungeon.</summary>
    public static bool IsFinal(int npcId) =>
        Aion2BossCatalog.Find(npcId) is { } boss && Load().TryGetValue(boss.Instance, out HashSet<string>? names) && names.Contains(boss.Name);

    internal static IReadOnlyDictionary<string, HashSet<string>> Table => Load();

    private static Dictionary<string, HashSet<string>> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in doc.RootElement.GetProperty("finals").EnumerateObject())
                {
                    table[entry.Name] = entry.Value.EnumerateArray().Select(n => n.GetString() ?? "").ToHashSet(StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // a missing or broken table: no dungeon has its battle music, the meter keeps working
        }

        _cache = table;
        return table;
    }
}
