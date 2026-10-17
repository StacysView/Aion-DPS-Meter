using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

public sealed record LootItem(int ItemId, long Count, DateTime At);

public sealed record LootMap(int Zone, List<LootItem> Items);

public sealed record LootSession(DateTime Start, DateTime End, List<LootMap> Maps);

/// <summary>
/// What the player picked up, by session and map. Three packets tell it (see the loot design): 0x1E56 is a drop on the ground (its loot id at 35, item at 39,
/// count at 43), 0x1B56 is an item landing in the inventory (item at 13), 0x1F56 with a last byte of 0 is a pickup of the drop with that loot id (at 11).
/// A pickup counts only when the same item landed in the inventory within 1.5 s before, so another player's pickups and vanished drops are left out.
/// 0x2136 carries the map. Local only: nothing is sent anywhere. The store is %AppData%\Aion DPS Meter\aion2-loot.json.
/// </summary>
public static class Aion2Loot
{
    private const int MaxDrops = 500;
    private const int MaxSessions = 60;
    private static readonly TimeSpan InventoryWindow = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    private static readonly object Gate = new();
    private static readonly List<LootSession> Stored = new();
    private static readonly Dictionary<uint, (int Item, long Count)> Drops = new();
    private static readonly Queue<uint> DropOrder = new();
    private static readonly List<(int Item, DateTime At)> Inventory = new();
    private static int _zone;
    private static DateTime? _lastPickup;
    private static bool _persist;
    private static bool _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when a pickup was counted.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-loot.json");

    /// <summary>The sessions, newest first (copies).</summary>
    public static IReadOnlyList<LootSession> Sessions
    {
        get
        {
            lock (Gate)
            {
                return Stored.Select(s => new LootSession(s.Start, s.End, s.Maps.Select(m => new LootMap(m.Zone, m.Items.ToList())).ToList())).ToList();
            }
        }
    }

    /// <summary>The map the player is on now (0 = not known yet).</summary>
    public static int CurrentZone
    {
        get
        {
            lock (Gate)
            {
                return _zone;
            }
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Stored.Clear();
            Drops.Clear();
            DropOrder.Clear();
            Inventory.Clear();
            _zone = 0;
            _lastPickup = null;
            _persist = false;
            _dirty = false;
            _savedAt = DateTime.MinValue;
        }

        Aion2ItemNames.ResetForTests();
    }

    /// <summary>The meter (not the replay tools) keeps the loot in a file between two starts; the player's own item names too.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            _lastPickup = null;
            try
            {
                if (File.Exists(StorePath))
                {
                    LoadFromText(File.ReadAllText(StorePath));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NullReferenceException)
            {
                // nothing saved
            }
        }

        Aion2ItemNames.EnablePersistence();
    }

    /// <summary>Replaces the sessions with what a file said; anything unreadable gives none.</summary>
    internal static void LoadFromText(string json)
    {
        var loaded = new List<LootSession>();
        try
        {
            var saved = JsonSerializer.Deserialize<List<SavedSession?>>(json);
            foreach (SavedSession? s in saved ?? new List<SavedSession?>())
            {
                if (s is null)
                {
                    continue;
                }

                var maps = (s.Maps ?? new()).Select(m => new LootMap(m.Zone, (m.Items ?? new()).Select(i => new LootItem(i.Id, i.Count, i.At)).ToList())).ToList();
                loaded.Add(new LootSession(s.Start, s.End, maps));
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException or InvalidOperationException or NullReferenceException)
        {
            loaded.Clear();
        }

        lock (Gate)
        {
            Stored.Clear();
            Stored.AddRange(loaded.OrderByDescending(s => s.Start).Take(MaxSessions));
            _dirty = false;
        }
    }

    /// <summary>Writes what is still waiting for the next save (called when the meter closes).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            _savedAt = DateTime.MinValue;
            Save();
        }
    }

    private sealed class SavedItem
    {
        public int Id { get; set; }

        public long Count { get; set; }

        public DateTime At { get; set; }
    }

    private sealed class SavedMap
    {
        public int Zone { get; set; }

        public List<SavedItem>? Items { get; set; }
    }

    private sealed class SavedSession
    {
        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public List<SavedMap>? Maps { get; set; }
    }

    private static void Save()
    {
        if (!_persist || !_dirty || (DateTime.UtcNow - _savedAt).TotalSeconds < 10)
        {
            return;
        }

        try
        {
            var saved = Stored.Select(s => new SavedSession
            {
                Start = s.Start,
                End = s.End,
                Maps = s.Maps.Select(m => new SavedMap { Zone = m.Zone, Items = m.Items.Select(i => new SavedItem { Id = i.ItemId, Count = i.Count, At = i.At }).ToList() }).ToList(),
            }).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            string temp = StorePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, StorePath, overwrite: true);
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // try again with the next pickup
        }
    }

    /// <summary>Looks at one frame (never consumes it). Frames that are too short for what they need are ignored.</summary>
    public static void Observe(int opcode, ReadOnlySpan<byte> frame, DateTime at)
    {
        if (opcode is not (0x2136 or 0x1E56 or 0x1B56 or 0x1F56))
        {
            return;
        }

        bool changed = false;
        lock (Gate)
        {
            switch (opcode)
            {
                case 0x2136:
                    if (frame.Length >= 10 && BinaryPrimitives.ReadUInt32LittleEndian(frame[6..]) is uint zone and not 0 and < int.MaxValue)
                    {
                        _zone = (int)zone;
                    }

                    break;
                case 0x1E56:
                    if (frame.Length >= 47)
                    {
                        uint key = BinaryPrimitives.ReadUInt32LittleEndian(frame[35..]);
                        int item = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame[39..]);
                        long count = BinaryPrimitives.ReadUInt32LittleEndian(frame[43..]);
                        if (!Drops.ContainsKey(key))
                        {
                            DropOrder.Enqueue(key);
                        }

                        Drops[key] = (item, count);
                        while (DropOrder.Count > MaxDrops)
                        {
                            Drops.Remove(DropOrder.Dequeue());
                        }
                    }

                    break;
                case 0x1B56:
                    if (frame.Length >= 25 && frame[3] <= 1)
                    {
                        Inventory.RemoveAll(e => at - e.At > TimeSpan.FromSeconds(10));
                        Inventory.Add(((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[13..]), at));
                    }

                    break;
                case 0x1F56:
                    if (frame.Length >= 19 && frame[^1] == 0)
                    {
                        uint key = BinaryPrimitives.ReadUInt32LittleEndian(frame[11..]);
                        if (Drops.TryGetValue(key, out var drop))
                        {
                            Drops.Remove(key);
                            int idx = Inventory.FindIndex(e => e.Item == drop.Item && e.At <= at && at - e.At <= InventoryWindow);
                            if (idx >= 0)
                            {
                                Inventory.RemoveAt(idx);
                                AddPickup(drop.Item, drop.Count, at);
                                changed = true;
                                _dirty = true;
                                Save();
                            }
                        }
                    }

                    break;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static void AddPickup(int item, long count, DateTime at)
    {
        if (_lastPickup is not { } last || at - last > SessionGap || Stored.Count == 0)
        {
            Stored.Insert(0, new LootSession(at, at, new List<LootMap>()));
            if (Stored.Count > MaxSessions)
            {
                Stored.RemoveRange(MaxSessions, Stored.Count - MaxSessions);
            }
        }

        _lastPickup = at;
        LootSession session = Stored[0];
        if (at > session.End)
        {
            session = session with { End = at };
            Stored[0] = session;
        }

        LootMap? map = session.Maps.FirstOrDefault(m => m.Zone == _zone);
        if (map is null)
        {
            map = new LootMap(_zone, new List<LootItem>());
            session.Maps.Add(map);
        }

        int i = map.Items.FindIndex(x => x.ItemId == item);
        if (i < 0)
        {
            map.Items.Add(new LootItem(item, count, at));
        }
        else
        {
            map.Items[i] = map.Items[i] with { Count = map.Items[i].Count + count, At = at };
        }
    }

    private static readonly Dictionary<int, string[]> Extra = new()
    {
        [600002] = ["Grotte de Krao", "Krao Cave", "Kraohöhle"],
        [600011] = ["Caverne de Draupnir", "Draupnir Cave", "Draupnirhöhle"],
    };

    public static string ZoneName(int zone, string language)
    {
        if (zone == 0)
        {
            return language == "fr" ? "Carte inconnue" : "Unknown map";
        }

        if (Extra.TryGetValue(zone, out string[]? names))
        {
            return language switch { "fr" => names[0], "de" => names[2], _ => names[1] };
        }

        FieldBossMap? map = Aion2FieldBosses.Maps.FirstOrDefault(m => m.Id == zone);
        return map is not null ? map.NameIn(language) : "Zone " + zone;
    }
}
