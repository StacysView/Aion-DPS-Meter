namespace AionDPS.Schedule;

/// <summary>Where a recurring game event stands: open (<see cref="Remaining"/> until it closes) or
/// not (<see cref="Remaining"/> until it next opens).</summary>
public readonly record struct EventTimer(bool IsOpen, TimeSpan Remaining);

/// <summary>A Shugo Festival reminder: the opening it is about, and whether it says "open" (from the
/// opening until it shuts) or "soon" (the minutes before).</summary>
public readonly record struct ShugoAlert(DateTime Opening, bool Open);

/// <summary>
/// The two recurring events the overlay can count down to, by the clock alone (nothing is read from
/// the game). Times as the French fansite aion2.fr gives them, in the PC's local time (2026-10-05):
/// the Shugo Festival opens on every hour - for 3 minutes, not the 10 first written (Stacy,
/// 2026-10-13); the Rift's entry portal opens every three hours at 02:00, 05:00 ... 23:00 for 10
/// minutes (the Rift itself then runs for an hour).
/// </summary>
public static class GameEventTimers
{
    /// <summary>How long before the Shugo Festival opens its first mid-screen reminder comes (a
    /// setting, off by default); the second comes on the hour.</summary>
    public static readonly TimeSpan ShugoReminderLead = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ShugoOpenFor = TimeSpan.FromMinutes(3);

    public static EventTimer Shugo(DateTime now) => Cycle(now, TimeSpan.FromHours(1), TimeSpan.Zero, ShugoOpenFor);

    public static EventTimer Rift(DateTime now) => Cycle(now, TimeSpan.FromHours(3), TimeSpan.FromHours(2), TimeSpan.FromMinutes(10));

    /// <summary>The Shugo reminder due at <paramref name="now"/> (Stacy, 2026-10-13): "soon" while the
    /// next opening is <see cref="ShugoReminderLead"/> away or less, "open" while it is open, else
    /// null. The opening and the kind tell one reminder from the next: two per opening.</summary>
    public static ShugoAlert? ShugoReminder(DateTime now)
    {
        EventTimer shugo = Shugo(now);
        if (shugo.IsOpen)
        {
            return new ShugoAlert(now + shugo.Remaining - ShugoOpenFor, Open: true);
        }

        return shugo.Remaining <= ShugoReminderLead ? new ShugoAlert(now + shugo.Remaining, Open: false) : null;
    }

    /// <summary>A day holds a whole number of periods, so the time of day is enough.</summary>
    private static EventTimer Cycle(DateTime now, TimeSpan period, TimeSpan firstStart, TimeSpan open)
    {
        long phase = ((now.TimeOfDay - firstStart).Ticks % period.Ticks + period.Ticks) % period.Ticks;
        return phase < open.Ticks
            ? new EventTimer(true, open - TimeSpan.FromTicks(phase))
            : new EventTimer(false, period - TimeSpan.FromTicks(phase));
    }

    /// <summary>"2h20", "20 min", or minutes and seconds under ten minutes ("4:05").</summary>
    public static string Countdown(TimeSpan left) =>
        left.TotalHours >= 1 ? $"{(int)left.TotalHours}h{left.Minutes:D2}"
        : left.TotalMinutes >= 10 ? $"{(int)left.TotalMinutes} min"
        : $"{(int)left.TotalMinutes}:{left.Seconds:D2}";
}
