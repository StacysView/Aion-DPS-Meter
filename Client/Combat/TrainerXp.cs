using System.IO;
using System.Text.Json;

namespace AionDPS.Combat;

/// <summary>
/// The trainer's experience of the Monsters theme (Stacy, 2026-11-23): every point of damage the local player deals to a boss counts, for good (kept in a small file of the meter's data
/// folder); each level costs more damage than the one before (<see cref="CostOf"/>), and the first <see cref="Badges"/> levels each have a badge of their own (the last one is the shining one).
/// </summary>
public sealed class TrainerXp
{
    /// <summary>The damage to the bosses that each of the first levels costs (Stacy, 2026-11-23: "10 millions pour le premier, 50 pour le deuxième, 200 pour le troisième, et ça augmente
    /// drastiquement"): from level 1 to 2, 2 to 3, and so on. After the tenth the cost keeps growing by 40 % a level, so more levels can follow.</summary>
    private static readonly long[] Costs = [10_000_000, 50_000_000, 200_000_000, 500_000_000, 1_000_000_000, 2_000_000_000, 3_500_000_000, 5_000_000_000, 7_500_000_000, 10_000_000_000];

    /// <summary>The damage that takes <paramref name="level"/> to the next one.</summary>
    public static long CostOf(int level)
    {
        if (level < 1)
        {
            level = 1;
        }

        if (level <= Costs.Length)
        {
            return Costs[level - 1];
        }

        double cost = Costs[^1] * Math.Pow(1.4, level - Costs.Length);
        return (long)(Math.Round(cost / 100_000_000) * 100_000_000);
    }

    /// <summary>The number of badges (one per level up to the tenth; the level after the tenth keeps the tenth badge).</summary>
    public const int Badges = 10;

    private readonly string? _file;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _dirty;

    public TrainerXp(string? file = null, long total = 0)
    {
        _file = file;
        Total = Math.Max(0, total);
    }

    /// <summary>All the damage to the bosses so far.</summary>
    public long Total { get; private set; }

    public int Level => LevelAt(Total, out _);

    /// <summary>The damage of the current level, 0 to <see cref="CostOfLevel"/> - 1.</summary>
    public long InLevel
    {
        get
        {
            LevelAt(Total, out long inLevel);
            return inLevel;
        }
    }

    /// <summary>What the current level costs to leave.</summary>
    public long CostOfLevel => CostOf(Level);

    /// <summary>How far to the next level, 0 to 1.</summary>
    public double Progress => InLevel / (double)CostOfLevel;

    private static int LevelAt(long total, out long inLevel)
    {
        int level = 1;
        long left = Math.Max(0, total);
        while (left >= CostOf(level) && level < 1000)
        {
            left -= CostOf(level);
            level++;
        }

        inLevel = left;
        return level;
    }

    /// <summary>The badge of a level, 1 to <see cref="Badges"/>.</summary>
    public static int BadgeOf(int level) => Math.Clamp(level, 1, Badges);

    public int Badge => BadgeOf(Level);

    /// <summary>Adds damage; true when it brought a new level.</summary>
    public bool Add(long damage)
    {
        if (damage <= 0)
        {
            return false;
        }

        int before = Level;
        Total += damage;
        _dirty = true;
        return Level > before;
    }

    /// <summary>The file read, or a new trainer when it is missing or broken.</summary>
    public static TrainerXp Load(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("totalDamage", out JsonElement total) && total.TryGetInt64(out long value))
                {
                    return new TrainerXp(file, value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a broken file: a trainer from the start
        }

        return new TrainerXp(file);
    }

    /// <summary>Writes the file when something changed and the last write is older than <paramref name="every"/> (or <paramref name="force"/>).</summary>
    public void Save(TimeSpan every, bool force = false)
    {
        if (_file is null || !_dirty || (!force && DateTime.UtcNow - _lastSave < every))
        {
            return;
        }

        try
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(new { totalDamage = Total }));
            _lastSave = DateTime.UtcNow;
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the next try
        }
    }
}
