namespace AionDPS.Combat;

/// <summary>
/// When a boss fight starts and whether it is still going, for the meter's own sounds (Stacy, 2026-11-21: a sound at the start of a boss fight, a
/// music during Eternal Ludra only). A boss's fight starts at the first hit on it after <see cref="Quiet"/> without any (a new boss, or a new pull
/// after a wipe); it goes on while hits keep landing. Keyed by the boss's NPC id, so its parts and a reset boss (a new entity) are the same fight.
/// Pure: the caller hands in the hits and the time.
/// </summary>
public sealed class BossMoments
{
    /// <summary>Without a hit on a boss for this long, its fight is over: the next hit starts a new one.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    private readonly Dictionary<int, DateTime> _lastHit = new();

    /// <summary>Notes the bosses hit at <paramref name="now"/>; returns those whose fight starts with these hits.</summary>
    public List<int> Note(IEnumerable<int> bossesHit, DateTime now)
    {
        var started = new List<int>();
        foreach (int npc in bossesHit.Distinct())
        {
            if (!_lastHit.TryGetValue(npc, out DateTime last) || now - last >= Quiet)
            {
                started.Add(npc);
            }

            _lastHit[npc] = now;
        }

        return started;
    }

    /// <summary>Whether the boss was hit within <paramref name="quiet"/> before <paramref name="now"/>.</summary>
    public bool HitWithin(int npc, DateTime now, TimeSpan quiet) => _lastHit.TryGetValue(npc, out DateTime last) && now - last < quiet;

    /// <summary>Forgets a boss (it died): its next hit is a new fight.</summary>
    public void End(int npc) => _lastHit.Remove(npc);
}
