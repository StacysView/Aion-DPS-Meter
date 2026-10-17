using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>
/// The zone the local player is in and where they stand, from the game's frames (the zone frame, the local
/// player's position frame and their movement frames; see <see cref="Aion2FrameDecoder"/>). Places are world
/// centimetres as the game sends them. Thread-safe: written by the capture thread, read by the panels.
/// <see cref="Changed"/> is raised outside the lock, at most once per 100 ms of capture time (the pet map moves its arrow up to 10 times a second), except for a
/// zone frame (a zone change or a teleport), which always raises it. A position noted within those 100 ms is
/// kept in <see cref="Last"/> and only announced with the next change past them, so readers that want the latest
/// place read <see cref="Last"/> rather than rely on one event per frame.
/// </summary>
public sealed class Aion2PlayerPosition
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private (float X, float Y, DateTime At)? _last;
    private int _zoneId;
    private bool _zoneFromFrame;
    private DateTime? _raisedAt;

    /// <summary>The last known place of the local player; null while unknown (no frame yet, or a zone entered
    /// without a place).</summary>
    public (float X, float Y, DateTime At)? Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    /// <summary>The zone id of the last zone frame; until one arrived, the zone the character was last seen in
    /// (<see cref="RestoreZone"/>, a meter started in game), else 0.</summary>
    public int ZoneId
    {
        get
        {
            lock (_gate)
            {
                return _zoneId;
            }
        }
    }

    /// <summary>True once a zone frame arrived this session (the zone is then the game's, not a remembered one).</summary>
    public bool ZoneFromFrame
    {
        get
        {
            lock (_gate)
            {
                return _zoneFromFrame;
            }
        }
    }

    /// <summary>The world map of the current zone; null for a zone without one (instances, cities of another map).</summary>
    public Aion2WorldMap? Map => Aion2WorldMaps.OfZone(ZoneId);

    public event Action? Changed;

    /// <summary>A plausible place: finite and inside ±500 000 cm on both axes, not the origin.</summary>
    public static bool IsPlausible(float x, float y) =>
        float.IsFinite(x) && float.IsFinite(y) && Math.Abs(x) <= 500_000 && Math.Abs(y) <= 500_000 && (Math.Abs(x) > 1 || Math.Abs(y) > 1);

    /// <summary>A zone frame: the zone, and the place it puts the player at when it holds one. Entering another zone
    /// without a place forgets the old place (it belonged to the old map). Always raises <see cref="Changed"/>.</summary>
    public void NoteZone(int zoneId, float? x, float? y, DateTime at)
    {
        if (zoneId <= 0)
        {
            return;
        }

        lock (_gate)
        {
            bool otherZone = zoneId != _zoneId;
            _zoneId = zoneId;
            _zoneFromFrame = true;
            if (x is float px && y is float py && IsPlausible(px, py))
            {
                _last = (px, py, at);
            }
            else if (otherZone)
            {
                _last = null;
            }

            _raisedAt = at;
        }

        Changed?.Invoke();
    }

    /// <summary>The zone the character was in when last seen (saved beside its pets), used until a zone frame
    /// arrives: after a start in the middle of a session the game sends none before the next teleport. Ignored once
    /// a zone frame came; 0 forgets a remembered zone (another character with none saved). Raises
    /// <see cref="Changed"/> when the zone changes.</summary>
    public void RestoreZone(int zoneId)
    {
        bool changed;
        lock (_gate)
        {
            if (_zoneFromFrame)
            {
                return;
            }

            int zone = Math.Max(0, zoneId);
            changed = zone != _zoneId;
            _zoneId = zone;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The zone remembered in a zone-&lt;name&gt;.json file; 0 when it is missing or broken.</summary>
    public static int LoadZone(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<SavedZone>(File.ReadAllText(path)) is { } saved)
            {
                return Math.Max(0, saved.LastZoneId);
            }
        }
        catch (Exception)
        {
            // Never throw: a damaged file simply means no zone remembered.
        }

        return 0;
    }

    /// <summary>Writes the zone remembered for a character (write-then-replace); a locked file loses only this save.</summary>
    public static void SaveZone(string path, int zoneId)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SavedZone { LastZoneId = zoneId }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class SavedZone
    {
        public int LastZoneId { get; set; }
    }

    /// <summary>A place of the local player from a position or movement frame. Implausible values are ignored.</summary>
    public void NotePosition(float x, float y, DateTime at)
    {
        if (!IsPlausible(x, y))
        {
            return;
        }

        bool raise;
        lock (_gate)
        {
            _last = (x, y, at);
            // A capture time going backwards (a replay restarted) does not hold the event back.
            raise = _raisedAt is not DateTime raised || at - raised >= Throttle || at < raised;
            if (raise)
            {
                _raisedAt = at;
            }
        }

        if (raise)
        {
            Changed?.Invoke();
        }
    }

    private (float Vx, float Vy, DateTime At)? _velocity;

    /// <summary>The local player's flight velocity (cm/s) from the flight frame 0x2A37, about 10 a second in the air.</summary>
    public void NoteVelocity(float vx, float vy, DateTime at)
    {
        if (!float.IsFinite(vx) || !float.IsFinite(vy) || Math.Abs(vx) > 20_000 || Math.Abs(vy) > 20_000)
        {
            return;
        }

        lock (_gate)
        {
            _velocity = (vx, vy, at);
        }
    }

    /// <summary>True while a recent velocity says the player is moving: the map then glides the arrow between two places.</summary>
    public bool IsGliding(DateTime nowUtc)
    {
        lock (_gate)
        {
            return _velocity is { } v && nowUtc - v.At.ToUniversalTime() < TimeSpan.FromSeconds(1) && v.Vx * v.Vx + v.Vy * v.Vy > 50 * 50;
        }
    }

    /// <summary>
    /// The place to draw now: the last place moved on by the flight velocity for the time since it came (at most 1 s,
    /// the game sends a place 2-3 times a second in flight), so the arrow moves smoothly instead of jumping. Without a
    /// recent velocity, the last place as it is.
    /// </summary>
    public (float X, float Y)? Predicted(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_last is not { } last)
            {
                return null;
            }

            if (_velocity is not { } v || nowUtc - v.At.ToUniversalTime() >= TimeSpan.FromSeconds(1))
            {
                return (last.X, last.Y);
            }

            double dt = Math.Clamp((nowUtc - last.At.ToUniversalTime()).TotalSeconds, 0, 1);
            return ((float)(last.X + v.Vx * dt), (float)(last.Y + v.Vy * dt));
        }
    }
}
