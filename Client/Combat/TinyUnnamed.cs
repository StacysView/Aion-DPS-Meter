namespace AionDPS.Combat;

/// <summary>
/// The damage list without the unnamed bit players (Stacy, 2026-11-10: "masque ceux sans nom sous 1 %"): in a crowded place the meter lists a few "Player #12345" - players
/// it has seen cast but never named, summons taken for players - at a hundredth of the damage of the real ones. Hidden while they stay under one percent of what the list shows.
/// </summary>
public static class TinyUnnamed
{
    public const double Percent = 1.0;

    /// <summary>The ids to keep: every named one, and the unnamed ones that reach <see cref="Percent"/> of the total of <paramref name="ids"/>.</summary>
    public static List<int> Keep(IReadOnlyList<int> ids, Func<int, long> damage, Func<int, bool> unnamed)
    {
        long total = ids.Sum(damage);
        return ids.Where(id => !unnamed(id) || total <= 0 || damage(id) * 100.0 >= total * Percent).ToList();
    }
}
