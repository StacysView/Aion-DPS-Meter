namespace AionDPS.Schedule;

/// <summary>Where a recurring game event stands: open (<see cref="Remaining"/> until it closes) or
/// not (<see cref="Remaining"/> until it next opens).</summary>
public readonly record struct EventTimer(bool IsOpen, TimeSpan Remaining);

/// <summary>
/// The two recurring events the overlay can count down to, by the clock alone (nothing is read from
/// the game). Times as the French fansite aion2.fr gives them, in the PC's local time (2026-10-05):
/// the Shugo Festival opens on every hour for 10 minutes; the Rift's entry portal opens every three
/// hours at 02:00, 05:00 ... 23:00 for 10 minutes (the Rift itself then runs for an hour).
/// </summary>
public static class GameEventTimers
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static EventTimer Shugo(DateTime now) => Cycle(now, TimeSpan.FromHours(1), TimeSpan.Zero);

    public static EventTimer Rift(DateTime now) => Cycle(now, TimeSpan.FromHours(3), TimeSpan.FromHours(2));

    /// <summary>A day holds a whole number of periods, so the time of day is enough.</summary>
    private static EventTimer Cycle(DateTime now, TimeSpan period, TimeSpan firstStart)
    {
        long phase = ((now.TimeOfDay - firstStart).Ticks % period.Ticks + period.Ticks) % period.Ticks;
        return phase < Window.Ticks
            ? new EventTimer(true, Window - TimeSpan.FromTicks(phase))
            : new EventTimer(false, period - TimeSpan.FromTicks(phase));
    }

    /// <summary>"2h20", "20 min", or minutes and seconds under ten minutes ("4:05").</summary>
    public static string Countdown(TimeSpan left) =>
        left.TotalHours >= 1 ? $"{(int)left.TotalHours}h{left.Minutes:D2}"
        : left.TotalMinutes >= 10 ? $"{(int)left.TotalMinutes} min"
        : $"{(int)left.TotalMinutes}:{left.Seconds:D2}";
}
