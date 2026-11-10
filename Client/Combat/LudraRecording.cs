namespace AionDPS.Combat;

/// <summary>What the automatic Ludra recording does on this tick.</summary>
public enum LudraRecordingStep
{
    None,
    Start,
    Stop,
}

/// <summary>
/// The capture recorded by itself in Ludra (Stacy, 2026-11-10: a whole run on file, with the words said into the microphone as
/// markers, to find each mechanic afterwards). It starts once a boss of Ludra is there in this zone and stops at the next zone
/// change (leaving the dungeon, or a new instance). A recording started by hand is never touched; one stopped by hand is not
/// started again in the same zone.
/// </summary>
public static class LudraRecording
{
    /// <param name="enabled">The setting.</param>
    /// <param name="recording">A recording runs (by hand or automatic).</param>
    /// <param name="autoZone">The zone the automatic recording running now started in, null when none runs.</param>
    /// <param name="zone">The zone now (when the game last sent the character record: it does at every zone change).</param>
    /// <param name="declinedZone">The zone where an automatic recording was stopped by hand.</param>
    /// <param name="ludraBossHere">A boss of Ludra was seen in this zone.</param>
    public static LudraRecordingStep Next(bool enabled, bool recording, DateTime? autoZone, DateTime zone, DateTime? declinedZone, bool ludraBossHere)
    {
        if (autoZone is DateTime started)
        {
            return recording && (zone != started || !enabled) ? LudraRecordingStep.Stop : LudraRecordingStep.None;
        }

        return enabled && !recording && ludraBossHere && declinedZone != zone ? LudraRecordingStep.Start : LudraRecordingStep.None;
    }

    /// <summary>A boss of Ludra whose hit points were read since <paramref name="zone"/> and who is alive.</summary>
    public static bool LudraBossHere(Aion2.Aion2EntityDirectory directory, DateTime zone) =>
        directory.KnownBosses().Any(b => RunDeaths.IsLudraBoss(b.NpcId) && directory.HitPoints.Latest(b.EntityId) is { } hp && hp.At >= zone && hp.Hp > 0);
}
