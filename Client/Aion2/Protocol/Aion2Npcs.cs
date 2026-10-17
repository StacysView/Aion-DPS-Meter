using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Every monster's name by the NPC id the game sends when it appears, read from the client's NpcData table and its texts
/// (assets/aion2/npcs/npc_names.json: {"2300709": {"en": "Talisra of the Void", "fr": "...", ...}}), as SkeeveTV's
/// Aion-DPS-Meter ships it (MIT). The boss cast bar names its casters with it in the game language; the bosses are also
/// in <see cref="Aion2BossCatalog"/> (English only). The table is big (3 MB): it is read once, on first use or by
/// <see cref="Warm"/> on a worker thread.
/// </summary>
public static class Aion2Npcs
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "npcs", "npc_names.json");
    private static readonly Lazy<Dictionary<int, Dictionary<string, string>>> Table = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The monster's name in a language, else English; null when the NPC id is unknown.</summary>
    public static string? NameOf(int npcId, string language) =>
        Table.Value.TryGetValue(npcId, out var names) ? names.GetValueOrDefault(language) ?? names.GetValueOrDefault("en") : null;

    /// <summary>Reads the table now, off the UI thread, so the first boss cast does not wait for it.</summary>
    public static void Warm() => Task.Run(() => _ = Table.Value);

    private static Dictionary<int, Dictionary<string, string>> Load()
    {
        var table = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            if (File.Exists(FilePath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(FilePath)) ?? new();
                foreach ((string key, var names) in raw)
                {
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = names;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // No table: monsters keep the boss catalog's English name, or none.
        }

        return table;
    }
}
