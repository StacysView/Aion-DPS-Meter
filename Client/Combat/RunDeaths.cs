namespace AionDPS.Combat;

/// <summary>One boss of a dungeon run: who it was and when it went - from the first hit anyone struck it (so the failed tries, the resets and the pauses before the kill are in) to its death.</summary>
public sealed record RunBoss(int NpcId, string Name, DateTime Start, DateTime End);

/// <summary>One player of the recap: their deaths at each boss (null when they did not fight it, or when the game sent no hit points for
/// them), and over the whole run (<see cref="Total"/>, which also counts the deaths between two bosses).</summary>
public sealed record RunDeathRow(int PlayerId, IReadOnlyList<int?> PerBoss, int? Total);

/// <summary>
/// The "Morts du donjon" table (Stacy, 2026-11-09, for the first Ludra run): per player their deaths at each
/// boss of the run and in all, most deaths first. Pure: the caller says how to count the deaths of a player
/// over a span (see <see cref="Deaths"/>). Abyssal Forge: Ludra's own bosses are told apart by
/// <see cref="IsLudraBoss"/>; the last one, Eternal Ludra, closes the run (<see cref="IsFinalBoss"/>).
/// </summary>
public static class RunDeaths
{
    private const string LudraInstance = "Abyssal Forge: Ludra";
    private const string FinalBossName = "Eternal Ludra";

    /// <summary>A boss of Abyssal Forge: Ludra, Normal or Hard.</summary>
    public static bool IsLudraBoss(int npcId) =>
        Aion2.Protocol.Aion2BossCatalog.Find(npcId) is { } boss && boss.Instance.StartsWith(LudraInstance, StringComparison.Ordinal);

    /// <summary>The last boss of Ludra, Eternal Ludra: its death ends the run.</summary>
    public static bool IsFinalBoss(int npcId) =>
        Aion2.Protocol.Aion2BossCatalog.Find(npcId) is { } boss && boss.Instance.StartsWith(LudraInstance, StringComparison.Ordinal) && boss.Name == FinalBossName;

    /// <param name="bosses">The run's bosses, in the order they died.</param>
    /// <param name="players">Who to list.</param>
    /// <param name="deathsIn">A player's deaths between two moments; null when nothing is known of them there.</param>
    /// <param name="tookPart">Whether the player fought that boss: a death is only listed under a boss they fought (two groups can fight two bosses at once).</param>
    /// <param name="nameOf">Breaks ties: the same number of deaths, A to Z.</param>
    public static List<RunDeathRow> Build(IReadOnlyList<RunBoss> bosses, IEnumerable<int> players, Func<int, DateTime, DateTime, int?> deathsIn, Func<int, RunBoss, bool> tookPart, Func<int, string> nameOf)
    {
        var rows = new List<RunDeathRow>();
        if (bosses.Count == 0)
        {
            return rows;
        }

        DateTime from = bosses.Min(b => b.Start), to = bosses.Max(b => b.End);
        foreach (int player in players.Distinct())
        {
            var perBoss = bosses.Select(b => tookPart(player, b) ? deathsIn(player, b.Start, b.End) : null).ToList();
            rows.Add(new RunDeathRow(player, perBoss, deathsIn(player, from, to)));
        }

        // Players with unknown deaths go last: a "-" is not a clean run.
        return rows.OrderBy(r => r.Total is null).ThenByDescending(r => r.Total ?? 0).ThenBy(r => nameOf(r.PlayerId), StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>How many of a player's hits really landed: a parried hit does about 0.4 of its damage (see <see cref="HitKind"/>),
/// which is what Précision is there to avoid (Stacy, 2026-11-09). The server sends no miss for a monster, so a hit that did
/// nothing is not seen: this is the share of hits that were not parried.</summary>
public static class HitRate
{
    /// <summary>Hits counted (a damage-over-time tick is not a hit) and how many were parried.</summary>
    public readonly record struct Result(int Hits, int Parried)
    {
        public int Landed => Hits - Parried;

        /// <summary>Percent of the hits that were not parried; null with no hit.</summary>
        public double? LandedPercent => Hits > 0 ? 100.0 * Landed / Hits : null;
    }

    public static Result Of(IEnumerable<DamageEvent> events)
    {
        int hits = 0, parried = 0;
        foreach (DamageEvent e in events)
        {
            if (e.IsHeal || e.IsTick)
            {
                continue;
            }

            hits++;
            if ((e.Kind & HitKind.Parry) != 0)
            {
                parried++;
            }
        }

        return new Result(hits, parried);
    }
}
