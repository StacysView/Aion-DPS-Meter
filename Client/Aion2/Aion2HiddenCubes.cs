namespace AionDPS.Aion2;

/// <summary>
/// The Hidden Cubes the game has announced around the player (env object spawn 0x3436, gone with
/// 0x3536): the game only announces one when the player comes within some 40-60 m, so a live cube is
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

    public void NoteSpawn(int entityId, long envId, float x, float y, DateTime at)
    {
        if (!IsHiddenCube(envId) || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) >= 1_000_000 || Math.Abs(y) >= 1_000_000)
        {
            return;
        }

        lock (_gate)
        {
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

    /// <summary>Metres from (x, y) to the nearest live cube (world units are centimetres), null without one.</summary>
    public double? NearestMeters(float x, float y, DateTime now)
    {
        var cubes = Live(now);
        return cubes.Count == 0 ? null : cubes.Min(c => Math.Sqrt((c.X - x) * (double)(c.X - x) + (c.Y - y) * (double)(c.Y - y))) / 100.0;
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
