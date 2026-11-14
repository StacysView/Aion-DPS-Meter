using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>How a duel stands against one player: how often he killed the local player and how often the local player killed him.</summary>
public sealed record DuelRecord(int KilledMe, int IKilled, DateTime Last)
{
    /// <summary>-1 = he is ahead (red), 1 = the local player is ahead (green), 0 = level.</summary>
    public int Side => KilledMe > IKilled ? -1 : KilledMe < IKilled ? 1 : 0;
}

/// <summary>
/// The score of the deaths between the local player and each other player ("Jeane killed you 5 times, you killed her twice"; Stacy, 2026-11-14).
/// The Abyss opponents change every two weeks (the servers are matched anew), so a record is forgotten after <see cref="Keep"/>: nobody is seen again
/// before then, and an old score would only mislead. Kept in a small file beside the settings; a damaged file starts again empty.
/// </summary>
public sealed class Aion2Duels
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(14);

    private readonly object _gate = new();
    private readonly Dictionary<string, DuelRecord> _records = new(StringComparer.Ordinal);
    private readonly string? _path;

    public Aion2Duels(string? path = null)
    {
        _path = path;
        Load();
    }

    /// <summary>One death between the local player and <paramref name="name"/>: <paramref name="killedMe"/> true when he killed the local player.</summary>
    public void Note(string name, bool killedMe, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (_gate)
        {
            Prune(at);
            DuelRecord current = _records.GetValueOrDefault(name) ?? new DuelRecord(0, 0, at);
            _records[name] = killedMe ? current with { KilledMe = current.KilledMe + 1, Last = at } : current with { IKilled = current.IKilled + 1, Last = at };
        }

        Save();
    }

    /// <summary>The score against a player; null when the two never killed each other (or the record is older than two weeks).</summary>
    public DuelRecord? Of(string name, DateTime now)
    {
        lock (_gate)
        {
            return _records.TryGetValue(name, out DuelRecord? record) && now - record.Last <= Keep ? record : null;
        }
    }

    /// <summary>The total over every opponent still remembered: how often the local player died to another player, and how often he killed one.</summary>
    public (int Deaths, int Kills) Totals(DateTime now)
    {
        lock (_gate)
        {
            var live = _records.Values.Where(r => now - r.Last <= Keep).ToList();
            return (live.Sum(r => r.KilledMe), live.Sum(r => r.IKilled));
        }
    }

    private void Prune(DateTime now)
    {
        foreach (string gone in _records.Where(kv => now - kv.Value.Last > Keep).Select(kv => kv.Key).ToList())
        {
            _records.Remove(gone);
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, DuelRecord>>(File.ReadAllText(_path));
            if (saved is not null)
            {
                foreach ((string name, DuelRecord record) in saved)
                {
                    _records[name] = record;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _records.Clear();
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_records);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
