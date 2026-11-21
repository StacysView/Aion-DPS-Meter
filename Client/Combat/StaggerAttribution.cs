using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>One player's stagger over a fight: <paramref name="Player"/> the total, <paramref name="Team"/> the total of everyone counted, <paramref name="PerSkill"/> by skill name.</summary>
public sealed record StaggerSummary(long Player, long Team, IReadOnlyDictionary<string, long> PerSkill);

/// <summary>
/// Who took a monster's stagger bar down (Stacy, 2026-11-10: "qui a infligé le stagger"). The server sends the bar of a monster (<see cref="StaggerReading"/>) but not who
/// hit it; measured on a Ludra recording (Ketu, bar 4,500): every skill takes a fixed amount off, whatever its damage or crit (Dark Crush 5 - 65 times of 65,
/// Judgment 5 - 63 of 64, Combustion 3 - 57 of 57, Elemental Fusion 10, Spinning Strike 15, Punishment - Max 35).
/// <para>The amounts come from the game's own table first (<see cref="Aion2StaggerTable"/>, 3,329 skills of every class, SkeeveTV, MIT; Stacy, 2026-11-21:
/// "le stagger des autres sorts précisément"). A skill the table lacks is learned from the fight itself: from the drops where one hit only landed, then -
/// round after round - from the drops where every skill but one is already known. Each drop is matched to the hits that landed in it: a hit is credited
/// its skill's amount while the drop has room for it (a hit that did not count - the bar recharging - gets nothing), a buffed hit half as much again when
/// that is what the drop holds; what is left goes to the hits of unknown skills, evenly.</para>
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
                var unknown = interval.Hits.Where(h => ValueOf(h, values) is null).Select(h => h.Skill).Distinct().ToList();
                if (unknown.Count != 1)
                {
                    continue;
                }

                int known = interval.Hits.Sum(h => ValueOf(h, values) ?? 0);
                int count = interval.Hits.Count(h => ValueOf(h, values) is null && h.Skill == unknown[0]);
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

            // Each fall of the packet, in order, to the next hit whose drop it equals - the game sends the readings in the order of the hits
            // (SkeeveTV's matching, MIT); then what no single hit explained goes to the hits left, the biggest first, buffed ones last.
            var used = new bool[interval.Hits.Count];
            int next = 0, remaining = 0;
            foreach (int drop in interval.Drops)
            {
                bool matched = false;
                for (int h = next; h < interval.Hits.Count; h++)
                {
                    if (!used[h] && ValueOf(interval.Hits[h], values) is int v && v > 0 && drop == v)
                    {
                        used[h] = true;
                        next = h + 1;
                        Add(interval.Hits[h].Source, interval.Hits[h].Skill, drop);
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    remaining += drop;
                }
            }

            var unknownHits = new List<Hit>();
            var left = new List<(Hit Hit, int Value)>();
            for (int h = 0; h < interval.Hits.Count; h++)
            {
                if (used[h])
                {
                    continue;
                }

                if (ValueOf(interval.Hits[h], values) is int v)
                {
                    if (v > 0)
                    {
                        left.Add((interval.Hits[h], v));
                    }
                }
                else
                {
                    unknownHits.Add(interval.Hits[h]);
                }
            }

            var taken = new List<(Hit Hit, int Value)>();
            foreach ((Hit hit, int v) in left.OrderByDescending(x => x.Value))
            {
                if (v <= remaining)
                {
                    taken.Add((hit, v));
                    remaining -= v;
                }
            }

            // A buff of the attacker raises a hit's drop by half (rounded down): what is still left goes to the hits it fits as buffed ones.
            for (int i = 0; i < taken.Count && remaining > 0; i++)
            {
                int extra = taken[i].Value * 3 / 2 - taken[i].Value;
                if (extra > 0 && extra <= remaining)
                {
                    taken[i] = (taken[i].Hit, taken[i].Value + extra);
                    remaining -= extra;
                }
            }

            foreach ((Hit hit, int v) in taken)
            {
                Add(hit.Source, hit.Skill, v);
            }

            if (unknownHits.Count > 0 && remaining > 0)
            {
                double each = (double)remaining / unknownHits.Count;
                foreach (Hit hit in unknownHits)
                {
                    Add(hit.Source, hit.Skill, each);
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

    private readonly record struct Hit(int Source, string Skill, int SkillId);

    /// <summary>One packet of a bar's readings that fell: its falls in order, their sum, and the hits on that monster since the packet before.</summary>
    private readonly record struct Interval(DateTime At, List<int> Drops, List<Hit> Hits)
    {
        public int Drop => Drops.Sum();
    }

    /// <summary>Readings and hits of one packet carry (nearly) the same time (SkeeveTV: 12 ms).</summary>
    private static readonly TimeSpan Packet = TimeSpan.FromMilliseconds(12);

    /// <summary>A hit's drop: the game's table for its skill id, else what the fight taught for its skill name; null when neither knows it.</summary>
    private static int? ValueOf(Hit hit, Dictionary<string, int> learned) =>
        Aion2StaggerTable.ValueOf(hit.SkillId) is > 0 and int fromTable ? fromTable : learned.TryGetValue(hit.Skill, out int value) ? value : null;

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
            DateTime boundary = DateTime.MinValue;
            for (int i = 0; i < ordered.Count;)
            {
                // the readings of one packet
                int last = i;
                while (last + 1 < ordered.Count && ordered[last + 1].At - ordered[i].At <= Packet)
                {
                    last++;
                }

                var drops = new List<int>();
                for (int r = Math.Max(i, 1); r <= last; r++)
                {
                    StaggerReading x = ordered[r - 1], y = ordered[r];
                    if (!x.Broke && !y.Broke && y.Current < x.Current)
                    {
                        drops.Add(x.Current - y.Current);
                    }
                }

                DateTime end = ordered[last].At + Packet;
                while (first < hits.Count && hits[first].Timestamp <= boundary)
                {
                    first++;
                }

                if (drops.Count > 0)
                {
                    var inside = new List<Hit>();
                    for (int h = first; h < hits.Count && hits[h].Timestamp <= end; h++)
                    {
                        inside.Add(new Hit(hits[h].SourceObjectId, hits[h].Skill!, hits[h].SkillId));
                    }

                    result.Add(new Interval(ordered[last].At, drops, inside));
                }

                boundary = end;
                i = last + 1;
            }
        }

        return result;
    }
}
