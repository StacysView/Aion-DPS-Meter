namespace AionDPS.Combat;

/// <summary>
/// At a boss's death, the players who finished its fight under a DPS threshold - for the teasing
/// banner (Ui/TeaseBanner). DPS is the overlay's own number: damage over the boss fight, from the
/// first to the last hit on the boss (see <see cref="BossFight.Dps"/>). Kept apart from the window
/// so the rules can be checked on their own (see SelfCheck).
/// </summary>
public static class LowDpsTease
{
    /// <summary>A shorter fight says nothing about anyone's DPS (a few seconds on a weak boss).</summary>
    public static readonly TimeSpan MinimumFight = TimeSpan.FromSeconds(20);

    /// <param name="fight">The boss fight's damage (the boss and its adds, see <see cref="BossFight.ShownHits"/>).</param>
    /// <param name="present">The players there: who dealt damage or healed during the fight.</param>
    /// <param name="concerned">Who may be teased: the group, or the local player alone.</param>
    /// <returns>The players under the threshold, lowest DPS first.</returns>
    public static IReadOnlyList<(int PlayerId, double Dps)> Pick(IReadOnlyList<DamageEvent> fight, int bossId,
        IEnumerable<int> present, Func<int, bool> concerned, double threshold)
    {
        var damage = fight.Where(ev => !ev.IsHeal).ToList();
        var onBoss = damage.Where(ev => ev.TargetObjectId == bossId).ToList();
        if (onBoss.Count < 2 || onBoss.Max(ev => ev.Timestamp) - onBoss.Min(ev => ev.Timestamp) < MinimumFight)
        {
            return Array.Empty<(int, double)>();
        }

        return present.Distinct()
            .Where(concerned)
            .Select(id => (PlayerId: id, Dps: BossFight.Dps(damage, bossId, id) ?? 0))
            .Where(p => p.Dps < threshold)
            .OrderBy(p => p.Dps)
            .ToList();
    }
}

/// <summary>
/// Phrases drawn like cards from a shuffled deck: none comes back before every other one was used,
/// and a new deck never starts with the phrase that ended the last one.
/// </summary>
public sealed class PhraseDeck
{
    private readonly int _count;
    private readonly Random _random;
    private readonly Queue<int> _left = new();
    private int _last = -1;

    public PhraseDeck(int count, Random? random = null)
    {
        _count = count;
        _random = random ?? new Random();
    }

    /// <summary>The index of the next phrase, 0 to count - 1.</summary>
    public int Draw()
    {
        if (_left.Count == 0)
        {
            int[] order = Enumerable.Range(0, _count).ToArray();
            _random.Shuffle(order);
            if (_count > 1 && order[0] == _last)
            {
                (order[0], order[^1]) = (order[^1], order[0]);
            }

            foreach (int index in order)
            {
                _left.Enqueue(index);
            }
        }

        _last = _left.Dequeue();
        return _last;
    }
}
