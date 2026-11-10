namespace AionDPS.Aion2;

/// <summary>One reading of a monster's stagger bar (<paramref name="Broke"/>: the bar broke; then the numbers are 0).</summary>
public readonly record struct StaggerReading(DateTime At, int Entity, int Current, int Max, bool Broke);

/// <summary>
/// The stagger bars of the monsters, as the server sends them (decoded after SkeeveAN, MIT: opcode 0x05E0, "03 01 | max u32 | current u32", or "00 03" on a break;
/// verified by him on two recordings, the bar being 1,200 and falling with the hits taken). Who made it fall is worked out from the hits around each drop.
/// </summary>
public sealed class Aion2Stagger
{
    private const int MaxReadings = 200_000;
    private readonly object _gate = new();
    private readonly List<StaggerReading> _readings = new();

    public void Note(StaggerReading reading)
    {
        lock (_gate)
        {
            if (_readings.Count >= MaxReadings)
            {
                _readings.RemoveRange(0, MaxReadings / 4);
            }

            _readings.Add(reading);
        }
    }

    public IReadOnlyList<StaggerReading> Readings()
    {
        lock (_gate)
        {
            return _readings.ToList();
        }
    }
}
