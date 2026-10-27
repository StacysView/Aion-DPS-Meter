namespace AionDPS.Aion2;

/// <summary>
/// The Hidden Cubes the game has announced around the player (env object spawn 0x3436, gone with
/// 0x3536): the game only announces one when the player comes within some 100-120 m (2026-10-17 19:03:09 recording: three cubes
/// announced 105-110 m away, one removed at 118 m and announced again at 120 m), so a live cube is
/// a cube nearby. The cube ids are the env objects the client names "Hidden Cube", from SkeeveTV's
/// Tools/aion2-dat/build_hidden_cubes.py (MIT). Checked on two real recordings (2026-10-08 and
/// 2026-10-10: env 1200039 announced, then removed 20 to 45 s later). Thread-safe: written on the
/// capture thread, read by the overlay's timer.
/// </summary>
public sealed class Aion2HiddenCubes
{
    /// <summary>A cube never removed (left the zone without the game saying so) is dropped after this.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<int, (float X, float Y, DateTime At)> _live = new();

    /// <summary>The env object ids of the Hidden Cubes: Verteron 1200036-41, 1200667, 1200707, 1201502-05, Altgard 1300081-83,
    /// 1300754, 1301199, 1301200, 1301242-45, and the Abyss ones (1100056, 1100094).</summary>
    public static bool IsHiddenCube(long envId) =>
        envId is >= 1200036 and <= 1200041 or 1200667 or 1200707 or >= 1201502 and <= 1201505 or >= 1300081 and <= 1300083 or 1300754
            or 1301199 or 1301200 or >= 1301242 and <= 1301245 or 1100056 or 1100094;

    /// <summary>Whether the cube detector (chime, beeps, distance, map) is on: when the bag holds at least one Hidden Cube key, or whatever the bag holds when
    /// <paramref name="always"/> is set, or while the bag is unknown (<paramref name="keys"/> null: a recording started after the login), because a cube
    /// missed for a count the meter could not make is worse than a cube announced without a key (Stacy, 2026-10-27).</summary>
    public static bool DetectorActive(long? keys, bool always) => always || keys is not long count || count > 0;

    /// <summary>How many cubes have appeared so far (a new entity id each): the overlay plays its arrival chime when it grows,
    /// even before the player's own place is known for the radar beeps (Stacy, 2026-10-17).</summary>
    public int Arrivals { get; private set; }

    public void NoteSpawn(int entityId, long envId, float x, float y, DateTime at)
    {
        if (!IsHiddenCube(envId) || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) >= 1_000_000 || Math.Abs(y) >= 1_000_000)
        {
            return;
        }

        lock (_gate)
        {
            if (!_live.ContainsKey(entityId))
            {
                Arrivals++;
            }

            _live[entityId] = (x, y, at);
        }
    }

    public void NoteRemoved(int entityId)
    {
        lock (_gate)
        {
            _live.Remove(entityId);
        }
    }

    /// <summary>Every cube announced and not yet gone, as of <paramref name="now"/> (capture time).</summary>
    public IReadOnlyList<(float X, float Y)> Live(DateTime now)
    {
        lock (_gate)
        {
            return _live.Values.Where(c => now - c.At < MaxAge).Select(c => (c.X, c.Y)).ToList();
        }
    }

    /// <summary>The game announces a cube within some 100-120 m and removes it beyond: one further than this from the player's own
    /// fresh place is one the game forgot to remove, dropped (<see cref="DropFar"/>). It was 70 m in 0.9.92-93, which dropped real
    /// cubes the game would not announce again while the player stayed in range: one under a player was missing (2026-10-17).</summary>
    public const double HearMetres = 160;

    /// <summary>Forgets the cubes further than <see cref="HearMetres"/> from the player's fresh place (x, y).</summary>
    public void DropFar(float x, float y)
    {
        lock (_gate)
        {
            foreach (int far in _live.Where(c => Math.Sqrt((c.Value.X - x) * (double)(c.Value.X - x) + (c.Value.Y - y) * (double)(c.Value.Y - y)) / 100.0 > HearMetres)
                .Select(c => c.Key).ToList())
            {
                _live.Remove(far);
            }
        }
    }

    /// <summary>Metres from (x, y) to the nearest live cube (world units are centimetres), null without one.</summary>
    public double? NearestMeters(float x, float y, DateTime now)
    {
        var cubes = Live(now);
        return cubes.Count == 0 ? null : cubes.Min(c => Math.Sqrt((c.X - x) * (double)(c.X - x) + (c.Y - y) * (double)(c.Y - y))) / 100.0;
    }

    /// <summary>The nearest live cube to (x, y) (world centimetres), null without one.</summary>
    public (float X, float Y)? Nearest(float x, float y, DateTime now)
    {
        var cubes = Live(now);
        return cubes.Count == 0 ? null : cubes.MinBy(c => (c.X - x) * (double)(c.X - x) + (c.Y - y) * (double)(c.Y - y));
    }

    private static readonly string[] Arrows = ["↑", "↗", "→", "↘", "↓", "↙", "←", "↖"];

    /// <summary>The way from one place to another as one of 8 arrows, relative to the map's north as the pet map draws
    /// it (screen up = map up: the world's y grows downwards on the map, x to the right).</summary>
    public static string Direction(float fromX, float fromY, float toX, float toY)
    {
        double dx = toX - (double)fromX, dy = toY - (double)fromY;
        double angle = Math.Atan2(dx, -dy) * 180 / Math.PI; // clockwise from the map's north
        int sector = (int)Math.Round((angle + 360) % 360 / 45) % 8;
        return Arrows[sector];
    }

    /// <summary>The beep's period, like a parking sensor: every 2 s from 60 m away down to 0.15 s within 3 m (SkeeveTV's curve).</summary>
    public static double BeepInterval(double meters) => Math.Clamp(0.15 + (meters - 3) / 57.0 * 1.85, 0.15, 2.0);

    public void Clear()
    {
        lock (_gate)
        {
            _live.Clear();
        }
    }
}
