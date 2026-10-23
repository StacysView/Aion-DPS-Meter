namespace AionDPS.Combat;

/// <summary>One step of the typical opener of a configuration: the skill most runs cast at that place, and when.</summary>
/// <param name="MedianSeconds">Median time of that cast, from the start of the run.</param>
/// <param name="Runs">How many runs cast this skill at this place.</param>
/// <param name="RunsWithStep">How many runs had a cast at this place at all.</param>
public sealed record DummyOpenerStep(int SkillId, string Skill, double MedianSeconds, int Runs, int RunsWithStep);

/// <summary>The damage of one slice of the run, averaged over the runs: <see cref="Dps"/> is damage per second inside the slice.</summary>
public sealed record DummyDpsBucket(double FromSeconds, double ToSeconds, double Dps);

/// <summary>How a configuration was played: its opener (the first accepted casts in their usual order) and how its damage was spread over the run.</summary>
public sealed record DummyRotationSummary(int Runs, IReadOnlyList<DummyOpenerStep> Opener, IReadOnlyList<DummyDpsBucket> Curve);

/// <summary>
/// The rotation side of the dummy test (Stacy, 2026-10-23: "which skill first, buffs before, start after?"): from the accepted skill uses and the hit times
/// of each run, what the opener of a configuration usually is and how the damage went over time. Pure, like <see cref="DummyAnalysis"/>; it describes, the
/// verdict stays the total DPS against its noise.
/// </summary>
public static class DummyRotation
{
    /// <summary>The opener lists this many casts.</summary>
    public const int OpenerSteps = 8;

    /// <summary>The curve has about this many slices, each a multiple of 5 s and at least 5 s.</summary>
    public const int CurveSlices = 6;

    /// <summary>The width of a curve slice for a run of this many planned seconds.</summary>
    public static double BucketSeconds(double plannedSeconds) =>
        Math.Max(5, Math.Round(plannedSeconds / CurveSlices / 5, MidpointRounding.AwayFromZero) * 5);

    public static DummyRotationSummary Summarize(IReadOnlyList<DummyRun> runs, Func<int, string> nameOf)
    {
        var opener = new List<DummyOpenerStep>();
        var casts = runs.Select(r => r.Uses.Where(u => u.Accepted && u.At >= r.Start && u.At <= r.Start + r.Planned).OrderBy(u => u.At)
            .Select(u => (Id: DummyAnalysis.BaseOf(u.SkillId), Seconds: (u.At - r.Start).TotalSeconds)).ToList()).ToList();
        for (int i = 0; i < OpenerSteps; i++)
        {
            List<(int Id, double Seconds)> atPlace = casts.Where(c => c.Count > i).Select(c => c[i]).ToList();
            if (atPlace.Count == 0)
            {
                break;
            }

            // The most common skill at this place; on a tie the one cast earlier.
            var best = atPlace.GroupBy(c => c.Id)
                .Select(g => (Id: g.Key, Count: g.Count(), Median: DummyStatistics.Median(g.Select(c => c.Seconds).ToList())))
                .OrderByDescending(g => g.Count).ThenBy(g => g.Median).First();
            opener.Add(new DummyOpenerStep(best.Id, nameOf(best.Id), best.Median, best.Count, atPlace.Count));
        }

        return new DummyRotationSummary(runs.Count, opener, Curve(runs));
    }

    private static List<DummyDpsBucket> Curve(IReadOnlyList<DummyRun> runs)
    {
        var curve = new List<DummyDpsBucket>();
        if (runs.Count == 0)
        {
            return curve;
        }

        double planned = runs[0].Planned.TotalSeconds;
        double width = BucketSeconds(planned);
        int slices = (int)Math.Ceiling(planned / width);
        for (int i = 0; i < slices; i++)
        {
            double from = i * width;
            double to = Math.Min(planned, from + width);
            bool last = i == slices - 1;
            double total = 0;
            foreach (DummyRun run in runs)
            {
                foreach (DamageEvent e in run.Events)
                {
                    if (e.IsHeal || e.Timestamp < run.Start || e.Timestamp > run.Start + run.Planned)
                    {
                        continue;
                    }

                    double t = (e.Timestamp - run.Start).TotalSeconds;
                    if (t >= from && (t < to || (last && t <= to)))
                    {
                        total += e.Amount;
                    }
                }
            }

            curve.Add(new DummyDpsBucket(from, to, to > from ? total / runs.Count / (to - from) : 0));
        }

        return curve;
    }
}
