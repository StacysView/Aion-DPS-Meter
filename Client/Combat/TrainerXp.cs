using System.IO;
using System.Text.Json;

namespace AionDPS.Combat;

/// <summary>
/// The trainer's experience of the Monsters theme (Stacy, 2026-11-23): every point of damage the local player deals to a boss counts, for good (kept in a small file of the meter's data
/// folder); every <see cref="PerLevel"/> damage is one level, and the first <see cref="Badges"/> levels each have a badge of their own (the last one is the shining one).
/// </summary>
public sealed class TrainerXp
{
    /// <summary>The damage to the bosses that makes one level.</summary>
    public const long PerLevel = 10_000_000;

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

    public int Level => (int)(Total / PerLevel) + 1;

    /// <summary>The damage of the current level, 0 to <see cref="PerLevel"/> - 1.</summary>
    public long InLevel => Total % PerLevel;

    /// <summary>How far to the next level, 0 to 1.</summary>
    public double Progress => InLevel / (double)PerLevel;

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
