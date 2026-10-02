using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>One continuous fight against one target: its hits, first to last.</summary>
public sealed record FightSegment(int TargetId, DateTime Start, DateTime End, IReadOnlyList<DamageEvent> Hits)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Splits a target's hits into separate fights by a silence gap - the same 120 s rule
/// MainWindow's Mob/Boss dropdown ("Name #1".."#N") and the headless upload use, so what the
/// history records as one fight is exactly what those show as one run. Chat.log assigns ids by
/// name, so a boss farmed five times shares one target id; without this the history would hold
/// one fight spanning the whole farm session.
///
/// <para>A source that sees the target's hit points also knows when it was reset to full health
/// (Aion 2: a wipe and retry keeps the boss's entity id, often well within 120 s). Each such moment
/// starts a new fight too, so two attempts are never summed against one health bar.</para>
/// </summary>
public static class FightSegmenter
{
    public const double DefaultGapSeconds = 120;

    public static List<FightSegment> Segment(IEnumerable<DamageEvent> events, int targetId, double gapSeconds = DefaultGapSeconds,
        IReadOnlyList<DateTime>? resets = null)
    {
        var hits = events
            .Where(ev => !ev.IsHeal && ev.TargetObjectId == targetId)
            .OrderBy(ev => ev.Timestamp)
            .ToList();
        var segments = new List<FightSegment>();
        if (hits.Count == 0)
        {
            return segments;
        }

        var current = new List<DamageEvent> { hits[0] };
        foreach (DamageEvent hit in hits.Skip(1))
        {
            DateTime previous = current[^1].Timestamp;
            bool reset = resets?.Any(r => r > previous && r <= hit.Timestamp) == true;
            if (!reset && (hit.Timestamp - previous).TotalSeconds <= gapSeconds)
            {
                current.Add(hit);
                continue;
            }

            segments.Add(new FightSegment(targetId, current[0].Timestamp, current[^1].Timestamp, current));
            current = new List<DamageEvent> { hit };
        }

        segments.Add(new FightSegment(targetId, current[0].Timestamp, current[^1].Timestamp, current));
        return segments;
    }
}
