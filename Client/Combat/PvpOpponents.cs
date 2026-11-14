namespace AionDPS.Combat;

/// <summary>
/// Which opponents the PvP tab lists (Stacy, 2026-11-14: twenty of them in the Abyss was unreadable): of the players outside the group who hit a member of it,
/// only the <see cref="Max"/> whose last such hit is the most recent. A new attacker takes the place of the one who has been quiet the longest.
/// An arena is left whole (its opponents are a fixed team, ten at most).
/// </summary>
public static class PvpOpponents
{
    public const int Max = 5;

    /// <summary>The ids kept, from the hits on the group as (attacker, when).</summary>
    public static HashSet<int> Latest(IEnumerable<(int Attacker, DateTime At)> hitsOnGroup, int max = Max)
    {
        var last = new Dictionary<int, DateTime>();
        foreach ((int attacker, DateTime at) in hitsOnGroup)
        {
            if (!last.TryGetValue(attacker, out DateTime seen) || at > seen)
            {
                last[attacker] = at;
            }
        }

        // The id settles two hits of the same instant, so the list does not flicker between two refreshes.
        return last.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(Math.Max(0, max)).Select(kv => kv.Key).ToHashSet();
    }
}
