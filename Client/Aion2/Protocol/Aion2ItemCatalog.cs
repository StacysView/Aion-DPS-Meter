using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>What the meter knows about one Aion 2 item from its id alone.</summary>
public sealed record Aion2ItemInfo(int Id, string Name, string Slot, int Grade, int Tier, int ItemLevel);

/// <summary>
/// Aion 2 item id -> name/slot/grade/tier/item level, from assets/aion2/items/item_info.json
/// (see its README for the source). The packet stream only carries ids; this is what turns
/// 110150026 into "Wind Breeze Greatsword, item level 32". Instance data - enchant level,
/// socketed stones, rolled sub-stats - is not in this table because it is not in the id.
/// </summary>
public static class Aion2ItemCatalog
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "items", "item_info.json");
    private static IReadOnlyDictionary<int, Aion2ItemInfo>? _cache;

    public static IReadOnlyDictionary<int, Aion2ItemInfo> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, Aion2ItemInfo>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int id) || entry.Value.GetArrayLength() < 5)
                    {
                        continue;
                    }

                    JsonElement v = entry.Value;
                    table[id] = new Aion2ItemInfo(id, v[0].GetString() ?? id.ToString(), v[1].GetString() ?? "", v[2].GetInt32(), v[3].GetInt32(), v[4].GetInt32());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // Missing or broken table: items degrade to their bare ids, nothing else is affected.
        }

        _cache = table;
        return table;
    }

    public static Aion2ItemInfo? Find(int itemId) => Load().GetValueOrDefault(itemId);

    private static IReadOnlyDictionary<int, string?>? _kindsByPrefix;

    /// <summary>
    /// The kind of item an id is (MainHand, SubHand, Helmet, Ring...): the table's, else - for an item
    /// newer than the table - the kind of every listed item sharing its first four digits (2104... are
    /// shoulders, 115... sub-hands). Null when neither says. A character window on 2026-10-05 wore a
    /// sub-hand, 115030040, missing from the 2026-10-01 table.
    /// </summary>
    public static string? SlotOf(int itemId)
    {
        if (Find(itemId) is { } known)
        {
            return known.Slot;
        }

        _kindsByPrefix ??= Load().Values
            .GroupBy(item => item.Id / 100_000)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Slot).Distinct().Count() == 1 ? group.First().Slot : null);
        return itemId is >= 100_000_000 and <= 999_999_999 ? _kindsByPrefix.GetValueOrDefault(itemId / 100_000) : null;
    }
}
