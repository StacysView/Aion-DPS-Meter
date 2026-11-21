namespace AionDPS.Aion2;

/// <summary>
/// The end chest of a dungeon being opened (Stacy, 2026-11-21: his own sound "quand on ouvre le cube"): the game sends the chest's rewards in one frame
/// (bytes 15 57, Kinah first), seen only then in 150 recordings. <see cref="Opened"/> is raised on the capture thread.
/// </summary>
public static class Aion2Chest
{
    private static DateTime _last = DateTime.MinValue;

    /// <summary>Raised once per chest (two frames within 3 s are one chest).</summary>
    public static event Action? Opened;

    public static void NoteOpened(DateTime at)
    {
        if (at - _last < TimeSpan.FromSeconds(3))
        {
            return;
        }

        _last = at;
        Opened?.Invoke();
    }
}
