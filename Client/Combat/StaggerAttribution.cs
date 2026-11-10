using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>One player's stagger over a fight: <paramref name="Player"/> the total, <paramref name="Team"/> the total of everyone counted, <paramref name="PerSkill"/> by skill name.</summary>
public sealed record StaggerSummary(long Player, long Team, IReadOnlyDictionary<string, long> PerSkill);

/// <summary>
/// Who took a monster's stagger bar down (Stacy, 2026-11-10: "qui a infligé le stagger"). The server sends the bar of a monster (<see cref="StaggerReading"/>) but not who
/// hit it; measured on a Ludra recording (Ketu, bar 4,500): every skill takes a fixed amount off, whatever its damage or crit (Dark Crush 5 - 65 times of 65,
/// Judgment 5 - 63 of 64, Combustion 3 - 57 of 57, Elemental Fusion 10, Spinning Strike 15, Punishment - Max 35).
/// <para>The amounts are learned from the fight itself: from the drops where one hit only landed, then - round after round - from the drops where
/// every skill but one is already known. A hit is credited its skill's amount; what a drop holds beyond the known amounts goes to the hits of unknown skills.</para>
/// </summary>
public static class StaggerAttribution
{
    private const int MaxRounds = 4;

    /// <summary>The amount each skill takes off the bar, learned from <paramref name="readings"/> and the hits around them.</summary>
    public static Dictionary<string, int> Learn(IReadOnlyList<StaggerReading> readings, IReadOnlyList<DamageEvent> events)
    {
        List<Interval> intervals = Intervals(readings, events);
        var values = new Dictionary<string, int>();
        for (int round = 0; round < MaxRounds; round++)
        {
            var votes = new Dictionary<string, Dictionary<int, int>>();
            foreach (Interval interval in intervals)
            {
                var unknown = interval.Skills.Where(s => !values.ContainsKey(s)).Distinct().ToList();
                if (unknown.Count != 1)
                {
                    continue;
                }

                int known = interval.Skills.Where(values.ContainsKey).Sum(s => values[s]);
                int count = interval.Skills.Count(s => s == unknown[0]);
                int rest = interval.Drop - known;
                if (rest <= 0 || rest % count != 0)
                {
                    continue;
                }

                if (!votes.TryGetValue(unknown[0], out var byValue))
                {
                    votes[unknown[0]] = byValue = new Dictionary<int, int>();
                }

                byValue[rest / count] = byValue.GetValueOrDefault(rest / count) + 1;
            }

            bool learned = false;
            foreach ((string skill, Dictionary<int, int> byValue) in votes)
            {
                (int value, int seen) = byValue.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).First();
                int total = byValue.Values.Sum();
                // Twice at least, and most of what was seen: one odd interval (a hit that straddles two readings) must not make a value.
                if (seen >= 2 && seen * 10 >= total * 7)
                {
                    values[skill] = value;
                    learned = true;
                }
            }

            if (!learned)
            {
                break;
            }
        }

        return values;
    }

    /// <summary>
    /// The stagger each source took off the bars, by skill. <paramref name="span"/> keeps the readings inside a fight's span; null, all of them. A hit is credited
    /// its skill's learned amount (scaled down when the known amounts of a drop add up to more than the drop); the rest of a drop goes to the hits whose skill
    /// is not learned yet, shared out evenly.
    /// </summary>
    public static Dictionary<int, Dictionary<string, long>> Attribute(IReadOnlyList<StaggerReading> readings, IReadOnlyList<DamageEvent> events, (DateTime Start, DateTime End)? span = null)
    {
        Dictionary<string, int> values = Learn(readings, events);
        var credit = new Dictionary<int, Dictionary<string, long>>();
        void Add(int source, string skill, double amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (!credit.TryGetValue(source, out var bySkill))
            {
                credit[source] = bySkill = new Dictionary<string, long>();
            }

            bySkill[skill] = bySkill.GetValueOrDefault(skill) + (long)Math.Round(amount);
        }

        foreach (Interval interval in Intervals(readings, events))
        {
            if (span is (DateTime from, DateTime to) && (interval.At < from || interval.At > to))
            {
                continue;
            }

            int known = interval.Hits.Where(h => values.ContainsKey(h.Skill)).Sum(h => values[h.Skill]);
            double scale = known > interval.Drop ? (double)interval.Drop / known : 1;
            var unknownHits = interval.Hits.Where(h => !values.ContainsKey(h.Skill)).ToList();
            foreach ((int source, string skill) in interval.Hits)
            {
                if (values.TryGetValue(skill, out int value))
                {
                    Add(source, skill, value * scale);
                }
            }

            if (unknownHits.Count > 0 && interval.Drop > known)
            {
                double each = (double)(interval.Drop - known) / unknownHits.Count;
                foreach ((int source, string skill) in unknownHits)
                {
                    Add(source, skill, each);
                }
            }
        }

        return credit;
    }

    /// <summary>The summary of one player: their total, and the team's - <paramref name="counted"/> says whose stagger makes the team.</summary>
    public static StaggerSummary? SummaryOf(Dictionary<int, Dictionary<string, long>> credit, int player, Func<int, bool> counted)
    {
        if (credit.Count == 0)
        {
            return null;
        }

        long team = credit.Where(kv => counted(kv.Key)).Sum(kv => kv.Value.Values.Sum());
        Dictionary<string, long> mine = credit.GetValueOrDefault(player) ?? new Dictionary<string, long>();
        return new StaggerSummary(mine.Values.Sum(), team, mine);
    }

    private readonly record struct Interval(DateTime At, int Drop, List<(int Source, string Skill)> Hits)
    {
        public IEnumerable<string> Skills => Hits.Select(h => h.Skill);
    }

    /// <summary>Between two readings of the same bar that fell: the drop, and the hits on that monster that landed in between.</summary>
    private static List<Interval> Intervals(IReadOnlyList<StaggerReading> readings, IReadOnlyList<DamageEvent> events)
    {
        var result = new List<Interval>();
        var hitsOf = new Dictionary<int, List<DamageEvent>>();
        foreach (DamageEvent ev in events)
        {
            if (ev.IsHeal || ev.SourceObjectId == ev.TargetObjectId || ev.Skill is null)
            {
                continue;
            }

            if (!hitsOf.TryGetValue(ev.TargetObjectId, out var list))
            {
                hitsOf[ev.TargetObjectId] = list = new List<DamageEvent>();
            }

            list.Add(ev);
        }

        foreach (var bar in readings.GroupBy(r => r.Entity))
        {
            if (!hitsOf.TryGetValue(bar.Key, out var hits))
            {
                continue;
            }

            hits.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            var ordered = bar.OrderBy(r => r.At).ToList();
            int first = 0;
            for (int i = 1; i < ordered.Count; i++)
            {
                StaggerReading a = ordered[i - 1], b = ordered[i];
                if (a.Broke || b.Broke || b.Current >= a.Current)
                {
                    continue;
                }

                while (first < hits.Count && hits[first].Timestamp <= a.At)
                {
                    first++;
                }

                var inside = new List<(int, string)>();
                for (int h = first; h < hits.Count && hits[h].Timestamp <= b.At; h++)
                {
                    inside.Add((hits[h].SourceObjectId, hits[h].Skill!));
                }

                result.Add(new Interval(b.At, a.Current - b.Current, inside));
            }
        }

        return result;
    }
}
