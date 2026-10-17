using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>
/// The zone the local player is in and where they stand, from the game's frames (the zone frame, the local
/// player's position frame and their movement frames; see <see cref="Aion2FrameDecoder"/>). Places are world
/// centimetres as the game sends them. Thread-safe: written by the capture thread, read by the panels.
/// <see cref="Changed"/> is raised outside the lock, at most once per 200 ms of capture time, except for a
/// zone frame (a zone change or a teleport), which always raises it. A position noted within those 200 ms is
/// kept in <see cref="Last"/> and only announced with the next change past them, so readers that want the latest
/// place read <see cref="Last"/> rather than rely on one event per frame.
/// </summary>
public sealed class Aion2PlayerPosition
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(200);

    private readonly object _gate = new();
    private (float X, float Y, DateTime At)? _last;
    private int _zoneId;
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

    /// <summary>The zone id of the last zone frame; 0 until one arrived.</summary>
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
}
