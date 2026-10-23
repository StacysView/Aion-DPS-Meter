using System.Globalization;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>One skill over one run, or over several pooled runs: counts, the plain-hit statistics and the damage numbers.
/// A "plain" hit is a non-tick hit with no crit, double, perfect or parry flag, the only kind a level comparison averages.
/// Rates are fractions (0.25 = 25 %).</summary>
/// <param name="Key">Grouping key: the base id ("#15280000") or, when the events carry no id, "name:" plus the skill name.</param>
/// <param name="BaseId">The base skill id (level and variant digits cut off); 0 when grouped by name.</param>
/// <param name="Uses">Accepted casts of that base id in the window; <paramref name="RefusedUses"/> the refused ones (key pressed during an animation, cooldown).</param>
/// <param name="Hits">Non-tick damage hits; ticks add to <paramref name="TotalDamage"/> only.</param>
/// <param name="CritMultiplier">Median crit-only hit over median plain hit; null with fewer than <see cref="DummyAnalysis.MinMultiplierHits"/> of either.</param>
/// <param name="Dps">Total damage over the planned seconds of the run(s): the same divisor for every run of a length.</param>
/// <param name="DamageVariance">n * variance of the non-tick hits plus the same for the ticks: this skill's share of the variance of the run's total damage.</param>
public sealed record DummySkillStats(
    string Key, int BaseId, string Skill,
    int Uses, int RefusedUses, int Hits, int PlainHits,
    double PlainMean, double PlainSd, long PlainMin, long PlainMax,
    int CritHits, double CritRate, RateInterval CritInterval,
    double DoubleRate, double PerfectRate, double ParryRate, double? CritMultiplier,
    long TotalDamage, long TickDamage, double Share, double Dps,
    double UsesPerMinute, double? HitsPerUse, double DamageVariance);

/// <summary>A finished run in numbers. <see cref="DpsStandardError"/> is the analytic one, from the hit spreads;
/// the stat table is null when the run has none.</summary>
public sealed record DummyRunSummary(
    DummyRun Run, long TotalDamage, double Dps, double DpsStandardError, int Hits,
    IReadOnlyList<int> EffectIds, IReadOnlyDictionary<int, int>? StatTable, IReadOnlyList<DummySkillStats> Skills);

public enum DummyTestWarningKind
{
    /// <summary>The runs do not share one planned length: nothing is compared.</summary>
    PlannedLengthsDiffer,

    /// <summary>A side has no complete run: nothing is compared.</summary>
    NoCompleteRuns,

    /// <summary>Runs stopped early were left out.</summary>
    IncompleteRunsExcluded,

    /// <summary>The runs were not against the same target (<see cref="DummyTestWarning.Names"/> lists them).</summary>
    TargetDiffers,

    /// <summary>The stat tables differ (<see cref="DummyTestWarning.Ids"/> are the stat ids): not the same gear.</summary>
    StatsDiffer,

    /// <summary>A buff, potion or food was on in some runs and not in others (<see cref="DummyTestWarning.Ids"/> are the effect ids).</summary>
    EffectsDiffer,

    /// <summary>Some runs' skill levels were not confirmed: left out of the level conclusion.</summary>
    LevelsNotConfirmed,

    /// <summary>Runs of one side report different levels for the same skill (<see cref="DummyTestWarning.Ids"/>): that skill's level is unknown.</summary>
    LevelsInconsistent,

    /// <summary>Fewer than <see cref="DummyAnalysis.MinPlainHits"/> plain hits of a skill on a side.</summary>
    FewPlainHits,

    /// <summary>The crit rate of a skill differs significantly: a skill level cannot do that, so stats or buffs changed.</summary>
    CritRateDiffers,
}

/// <summary>A caveat of a comparison; <see cref="Message"/> is English, the kind and the ids let the UI localize.</summary>
public sealed record DummyTestWarning(DummyTestWarningKind Kind, string Message, string? Skill = null, IReadOnlyList<int>? Ids = null, IReadOnlyList<string>? Names = null);

public enum DummyTestVerdict
{
    Better,
    Worse,

    /// <summary>The interval of the change contains 0: the data cannot tell the two apart.</summary>
    NoMeasurableDifference,

    /// <summary>Fewer than <see cref="DummyAnalysis.MinPlainHits"/> plain hits on a side.</summary>
    NotEnoughData,
}

/// <summary>One skill in both configurations (A first, B second). Changes are fractions (0.06 = +6 %) of A's plain mean;
/// the interval is Welch's 95 % interval of the difference divided by A's mean, so it excludes 0 exactly when the verdict is not "no measurable difference".</summary>
public sealed record DummyTestRow(
    string Skill, int BaseId, DummySkillStats? A, DummySkillStats? B,
    double? PlainChange, double? PlainChangeLow, double? PlainChangeHigh, double? PValue, DummyTestVerdict Verdict,
    bool CritRateDiffers, int? LevelA, int? LevelB)
{
    public bool LevelChanged => LevelA is int a && LevelB is int b && a != b;

    public double? PlainMeanA => A is { PlainHits: > 0 } ? A.PlainMean : null;

    public double? PlainMeanB => B is { PlainHits: > 0 } ? B.PlainMean : null;

    public double? UsesPerMinuteA => A?.UsesPerMinute;

    public double? UsesPerMinuteB => B?.UsesPerMinute;
}

public enum DummyLevelConclusionKind
{
    /// <summary>A side has no run with confirmed levels: no level conclusion.</summary>
    LevelsUnknown,

    /// <summary>No skill level differs: whatever changed the damage, it was not a level.</summary>
    NoLevelChange,

    /// <summary>Exactly one skill level differs: the verdict names it.</summary>
    SingleChange,

    /// <summary>Two or more differ: no skill is named for the DPS change.</summary>
    Mixed,
}

/// <summary>What the level change explains. <see cref="Skill"/> and <see cref="Row"/> are set for a single change only
/// (Row is null when that skill never hit).</summary>
public sealed record DummyLevelConclusion(
    DummyLevelConclusionKind Kind, IReadOnlyList<int> ChangedBaseIds, int? BaseId, string? Skill, DummyTestRow? Row, string Text);

public enum DummyNoiseMethod
{
    /// <summary>Standard error from the hit variances inside each run (used with fewer than three runs per side).</summary>
    HitSpread,

    /// <summary>Standard error from the spread between the runs (three or more per side).</summary>
    RunToRun,
}

/// <summary>Total DPS of A and B. <see cref="Margin"/> is the noise the delta must beat: 1.96 times the combined error for
/// <see cref="DummyNoiseMethod.HitSpread"/>, the Student value at the Welch df for <see cref="DummyNoiseMethod.RunToRun"/>.</summary>
public sealed record DummyDpsComparison(
    double DpsA, double DpsB, double Delta, double? DeltaFraction, double StandardErrorA, double StandardErrorB,
    double Margin, bool WithinNoise, DummyNoiseMethod Method, string Statement);

/// <summary>The result of comparing two configurations. When <see cref="Comparable"/> is false only
/// <see cref="Refusal"/> and <see cref="Warnings"/> are meaningful.</summary>
public sealed record DummyTestComparison(
    bool Comparable, string? Refusal, int RunsA, int RunsB, int PlannedSeconds,
    IReadOnlyList<DummyTestRow> Rows, DummyDpsComparison? Dps, DummyLevelConclusion? Levels,
    IReadOnlyList<DummyTestWarning> Warnings);

public enum DummyCurveTier
{
    /// <summary>Three or more levels tested, or the slope's interval excludes 0.</summary>
    Measured,

    /// <summary>Two levels: a line through two points, no honest error.</summary>
    TwoPoints,

    /// <summary>One level only: no slope, no ranking value.</summary>
    NotMeasurable,
}

public sealed record DummyCurvePoint(int Level, double PlainMean, int PlainHits, int RunNumber);

/// <summary>What one skill level is worth, from every run that read its level. Slope values are damage per plain hit and level.</summary>
/// <param name="RelativeSlope">Slope over the fitted plain mean at the lowest tested level (0.03 = about +3 % per level).</param>
/// <param name="DpsGainPerLevel">Slope * (1 + crit rate * (crit multiplier - 1)) * hits per minute, from the latest run that cast the skill; null unless a slope exists.</param>
public sealed record DummyLevelCurve(
    int BaseId, string Skill, IReadOnlyList<DummyCurvePoint> Points, int DistinctLevels,
    double? SlopePerLevel, double? SlopeStandardError, double? SlopeLow, double? SlopeHigh, double? RelativeSlope,
    double CritRate, double CritMultiplier, bool CritMultiplierAssumed, double HitsPerMinute,
    double? DpsGainPerLevel, DummyCurveTier Tier, string Note);

/// <summary>
/// The analysis behind the dummy test: per-run skill statistics, the A-against-B comparison and the level curves.
/// Pure (no UI, no packets, no I/O). Only events and uses inside a run's window [Start, Start + Planned] count, and
/// DPS always divides by the planned seconds, never by the last hit's time.
/// </summary>
public static class DummyAnalysis
{
    /// <summary>Fewer plain hits than this on a side and a skill's plain mean is not compared.</summary>
    public const int MinPlainHits = 10;

    /// <summary>A crit multiplier needs this many plain and this many crit-only hits.</summary>
    public const int MinMultiplierHits = 5;

    /// <summary>A run contributes a point to a level curve with at least this many plain hits of the skill.</summary>
    public const int MinCurvePointHits = 5;

    /// <summary>Crit multiplier used (and said so) when none was measured. SkeeveTV's measurement: about x1.85.</summary>
    public const double AssumedCritMultiplier = 1.85;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The base skill id: level and variant digits cut off (id / 10000 * 10000). Ids below 10000 are kept whole.</summary>
    public static int BaseOf(int skillId) => skillId < 10000 ? skillId : skillId / 10000 * 10000;

    // ---------------------------------------------------------------- per run

    /// <summary>One run in numbers: damage, DPS (damage / Planned), its analytic standard error, effects, stats, per-skill table.</summary>
    public static DummyRunSummary Summarize(DummyRun run)
    {
        double seconds = run.Planned.TotalSeconds;
        List<DamageEvent> events = WindowEvents(run);
        List<DummySkillStats> skills = BuildSkills(events, WindowUses(run), seconds);
        long total = skills.Sum(s => s.TotalDamage);
        double variance = skills.Sum(s => s.DamageVariance);
        double dps = seconds > 0 ? total / seconds : 0;
        double se = seconds > 0 ? Math.Sqrt(variance) / seconds : 0;
        IReadOnlyDictionary<int, int>? table = run.Stats?.Values;
        return new DummyRunSummary(run, total, dps, se, skills.Sum(s => s.Hits),
            run.Effects.Select(e => e.EffectId).Distinct().OrderBy(id => id).ToList(), table, skills);
    }

    private static bool InWindow(DateTime t, DummyRun run) => t >= run.Start && t <= run.Start + run.Planned;

    /// <summary>The run's damage events inside the window (heals left out). The run already holds the local player's hits (summons attributed to the owner), so the source id is not filtered again.</summary>
    private static List<DamageEvent> WindowEvents(DummyRun run) =>
        run.Events.Where(e => !e.IsHeal && InWindow(e.Timestamp, run)).ToList();

    private static List<SkillUse> WindowUses(DummyRun run) => run.Uses.Where(u => InWindow(u.At, run)).ToList();

    private static string KeyOf(DamageEvent e) =>
        e.SkillId != 0 ? "#" + BaseOf(e.SkillId).ToString(Inv) : "name:" + (e.Skill ?? "(auto attack)");

    private static bool IsCrit(DamageEvent e) => e.IsCritical || (e.Kind & HitKind.Crit) != 0;

    private static bool IsPlain(DamageEvent e) => !e.IsTick && !IsCrit(e) && e.Kind == HitKind.None;

    /// <summary>Per-skill statistics of events and uses covering <paramref name="seconds"/> of planned time (several pooled runs add their seconds).</summary>
    private static List<DummySkillStats> BuildSkills(IReadOnlyList<DamageEvent> events, IReadOnlyList<SkillUse> uses, double seconds)
    {
        var accepted = new Dictionary<int, int>();
        var refused = new Dictionary<int, int>();
        foreach (SkillUse u in uses)
        {
            Dictionary<int, int> map = u.Accepted ? accepted : refused;
            int b = BaseOf(u.SkillId);
            map[b] = map.GetValueOrDefault(b) + 1;
        }

        long grand = events.Sum(e => e.Amount);
        var result = new List<DummySkillStats>();
        foreach (IGrouping<string, DamageEvent> g in events.GroupBy(KeyOf))
        {
            List<DamageEvent> all = g.ToList();
            int firstId = all.Select(e => e.SkillId).FirstOrDefault(id => id != 0);
            int baseId = firstId != 0 ? BaseOf(firstId) : 0;
            string name = all.Select(e => e.Skill).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? (baseId != 0 ? "#" + baseId.ToString(Inv) : "(auto attack)");
            List<DamageEvent> hits = all.Where(e => !e.IsTick).ToList();
            List<DamageEvent> ticks = all.Where(e => e.IsTick).ToList();
            List<double> plain = hits.Where(IsPlain).Select(e => (double)e.Amount).ToList();
            List<double> critOnly = hits.Where(e => IsCrit(e) && (e.Kind & ~HitKind.Crit) == HitKind.None).Select(e => (double)e.Amount).ToList();
            int crits = hits.Count(IsCrit);
            double? multiplier = null;
            if (plain.Count >= MinMultiplierHits && critOnly.Count >= MinMultiplierHits)
            {
                double plainMedian = DummyStatistics.Median(plain);
                if (plainMedian > 0)
                {
                    multiplier = DummyStatistics.Median(critOnly) / plainMedian;
                }
            }

            int n = hits.Count;
            long total = all.Sum(e => e.Amount);
            int useCount = baseId != 0 ? accepted.GetValueOrDefault(baseId) : 0;
            int refusedCount = baseId != 0 ? refused.GetValueOrDefault(baseId) : 0;
            double hitVariance = n * DummyStatistics.SampleVariance(hits.Select(e => (double)e.Amount).ToList());
            double tickVariance = ticks.Count * DummyStatistics.SampleVariance(ticks.Select(e => (double)e.Amount).ToList());
            result.Add(new DummySkillStats(
                g.Key, baseId, name, useCount, refusedCount, n, plain.Count,
                DummyStatistics.Mean(plain), DummyStatistics.SampleSd(plain),
                plain.Count > 0 ? (long)plain.Min() : 0, plain.Count > 0 ? (long)plain.Max() : 0,
                crits, n > 0 ? (double)crits / n : 0, DummyStatistics.Wilson(crits, n),
                Rate(hits, e => (e.Kind & HitKind.Double) != 0), Rate(hits, e => (e.Kind & HitKind.Perfect) != 0),
                Rate(hits, e => (e.Kind & HitKind.Parry) != 0), multiplier,
                total, ticks.Sum(e => e.Amount), grand > 0 ? (double)total / grand : 0, seconds > 0 ? total / seconds : 0,
                seconds > 0 ? useCount / (seconds / 60) : 0, useCount > 0 ? (double)n / useCount : null,
                hitVariance + tickVariance));
        }

        return result.OrderByDescending(s => s.TotalDamage).ToList();
    }

    private static double Rate(List<DamageEvent> hits, Func<DamageEvent, bool> flag) =>
        hits.Count > 0 ? (double)hits.Count(flag) / hits.Count : 0;

    // ---------------------------------------------------------------- comparison

    /// <summary>One configuration: its complete runs pooled.</summary>
    private sealed class Side
    {
        public List<DummyRun> Runs = new();
        public List<DummyRunSummary> Summaries = new();
        public List<DummySkillStats> Skills = new();
        public double Seconds;

        /// <summary>Base id to confirmed level; null when no run of the side has confirmed levels.</summary>
        public Dictionary<int, int>? Levels;
        public int UnconfirmedRuns;
        public List<int> InconsistentLevels = new();
    }

    private static Side BuildSide(IEnumerable<DummyRun> complete)
    {
        var side = new Side { Runs = complete.ToList() };
        side.Summaries = side.Runs.Select(Summarize).ToList();
        side.Seconds = side.Runs.Sum(r => r.Planned.TotalSeconds);
        side.Skills = BuildSkills(
            side.Runs.SelectMany(WindowEvents).ToList(), side.Runs.SelectMany(WindowUses).ToList(), side.Seconds);

        List<DummyRun> confirmed = side.Runs.Where(r => r.LevelsConfirmed && r.Levels is not null).ToList();
        side.UnconfirmedRuns = side.Runs.Count - confirmed.Count;
        if (confirmed.Count > 0)
        {
            side.Levels = new Dictionary<int, int>();
            var seen = new Dictionary<int, HashSet<int>>();
            foreach (DummyRun r in confirmed)
            {
                foreach (IGrouping<int, SkillLevel> g in r.Levels!.GroupBy(l => BaseOf(l.SkillId)))
                {
                    if (!seen.TryGetValue(g.Key, out HashSet<int>? set))
                    {
                        seen[g.Key] = set = new HashSet<int>();
                    }

                    set.Add(g.Max(l => l.Level)); // the level the game shows (base + bonuses): a Daevanion or gear change moves it with the base unchanged
                }
            }

            foreach ((int baseId, HashSet<int> levels) in seen)
            {
                if (levels.Count == 1)
                {
                    side.Levels[baseId] = levels.First();
                }
                else
                {
                    side.InconsistentLevels.Add(baseId);
                }
            }
        }

        return side;
    }

    /// <summary>Compares configuration A with configuration B (each a list of runs, pooled). Incomplete runs are left out;
    /// runs of different planned lengths are not compared at all (<see cref="DummyTestComparison.Comparable"/> false).</summary>
    public static DummyTestComparison Compare(IReadOnlyList<DummyRun> runsA, IReadOnlyList<DummyRun> runsB)
    {
        var warnings = new List<DummyTestWarning>();
        List<DummyRun> completeA = runsA.Where(r => r.Complete).ToList();
        List<DummyRun> completeB = runsB.Where(r => r.Complete).ToList();
        int excluded = runsA.Count + runsB.Count - completeA.Count - completeB.Count;
        if (excluded > 0)
        {
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.IncompleteRunsExcluded,
                $"{excluded} run(s) stopped early were left out of the comparison."));
        }

        if (completeA.Count == 0 || completeB.Count == 0)
        {
            const string Reason = "Each side needs at least one complete run.";
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.NoCompleteRuns, Reason));
            return Refuse(Reason, completeA.Count, completeB.Count, warnings);
        }

        List<int> lengths = completeA.Concat(completeB).Select(r => (int)Math.Round(r.Planned.TotalSeconds)).Distinct().OrderBy(s => s).ToList();
        if (lengths.Count > 1)
        {
            string reason = $"The runs have different planned lengths ({string.Join(", ", lengths.Select(s => s + " s"))}): only runs of the same length are compared.";
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.PlannedLengthsDiffer, reason, Ids: lengths));
            return Refuse(reason, completeA.Count, completeB.Count, warnings);
        }

        Side a = BuildSide(completeA);
        Side b = BuildSide(completeB);

        AddContextWarnings(a, b, warnings);

        // Rows over the union of skills.
        var rows = new List<DummyTestRow>();
        foreach (string key in a.Skills.Select(s => s.Key).Union(b.Skills.Select(s => s.Key)))
        {
            DummySkillStats? sa = a.Skills.FirstOrDefault(s => s.Key == key);
            DummySkillStats? sb = b.Skills.FirstOrDefault(s => s.Key == key);
            DummyTestRow row = BuildRow(sa, sb, a, b);
            rows.Add(row);
            if (row.Verdict == DummyTestVerdict.NotEnoughData && ((sa?.Hits ?? 0) + (sb?.Hits ?? 0)) > 0)
            {
                warnings.Add(new DummyTestWarning(DummyTestWarningKind.FewPlainHits,
                    $"{row.Skill}: {sa?.PlainHits ?? 0} plain hits in A and {sb?.PlainHits ?? 0} in B (at least {MinPlainHits} each are needed to compare its plain hit).", row.Skill));
            }

            if (row.CritRateDiffers)
            {
                warnings.Add(new DummyTestWarning(DummyTestWarningKind.CritRateDiffers,
                    $"{row.Skill}: crit rate {FormatRate(sa!.CritRate)} in A against {FormatRate(sb!.CritRate)} in B differs more than chance allows; a skill level cannot do that, so stats or buffs changed.", row.Skill));
            }
        }

        rows = rows.OrderByDescending(r => (r.A?.TotalDamage ?? 0) + (r.B?.TotalDamage ?? 0)).ToList();
        DummyDpsComparison dps = CompareDps(a, b);
        DummyLevelConclusion levels = ConcludeLevels(a, b, rows);
        return new DummyTestComparison(true, null, completeA.Count, completeB.Count, lengths[0], rows, dps, levels, warnings);
    }

    private static DummyTestComparison Refuse(string reason, int runsA, int runsB, List<DummyTestWarning> warnings) =>
        new(false, reason, runsA, runsB, 0, Array.Empty<DummyTestRow>(), null, null, warnings);

    private static void AddContextWarnings(Side a, Side b, List<DummyTestWarning> warnings)
    {
        List<DummyRun> all = a.Runs.Concat(b.Runs).ToList();

        List<string> targets = all.Select(r => r.TargetName).Where(n => n.Length > 0).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (targets.Count > 1)
        {
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.TargetDiffers,
                $"The runs were not against the same target ({string.Join(", ", targets)}): its defence differs, so the damage is not comparable.", Names: targets));
        }

        // Stat tables: any id whose value is not the same in every run that has a table.
        List<IReadOnlyDictionary<int, int>> tables = all.Where(r => r.Stats is not null).Select(r => r.Stats!.Values).ToList();
        if (tables.Count > 1)
        {
            List<int> differing = tables.SelectMany(t => t.Keys).Distinct().Where(id =>
                tables.Select(t => t.TryGetValue(id, out int v) ? (int?)v : null).Distinct().Count() > 1).OrderBy(id => id).ToList();
            if (differing.Count > 0)
            {
                warnings.Add(new DummyTestWarning(DummyTestWarningKind.StatsDiffer,
                    $"The stat tables differ (stat ids {string.Join(", ", differing)}): not the same gear, so the damage is not comparable.", Ids: differing));
            }
        }

        // Effects: in some runs and not in all.
        List<HashSet<int>> effectSets = all.Select(r => r.Effects.Select(e => e.EffectId).ToHashSet()).ToList();
        List<int> effectDiff = effectSets.SelectMany(s => s).Distinct().Where(id => effectSets.Any(s => !s.Contains(id))).OrderBy(id => id).ToList();
        if (effectDiff.Count > 0)
        {
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.EffectsDiffer,
                $"Different buffs, potions or food were active (effect ids {string.Join(", ", effectDiff)}): they multiply the damage.", Ids: effectDiff));
        }

        int unconfirmed = a.UnconfirmedRuns + b.UnconfirmedRuns;
        if (unconfirmed > 0)
        {
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.LevelsNotConfirmed,
                $"{unconfirmed} run(s) had no confirmed skill levels: they are left out of the level conclusions (their damage still counts)."));
        }

        List<int> inconsistent = a.InconsistentLevels.Union(b.InconsistentLevels).OrderBy(id => id).ToList();
        if (inconsistent.Count > 0)
        {
            warnings.Add(new DummyTestWarning(DummyTestWarningKind.LevelsInconsistent,
                $"Runs of one configuration report different levels for the same skill (ids {string.Join(", ", inconsistent)}): those skills' levels are treated as unknown.", Ids: inconsistent));
        }
    }

    private static DummyTestRow BuildRow(DummySkillStats? sa, DummySkillStats? sb, Side a, Side b)
    {
        DummySkillStats any = (sa ?? sb)!;
        int baseId = any.BaseId;
        double? change = null, low = null, high = null, p = null;
        DummyTestVerdict verdict = DummyTestVerdict.NotEnoughData;
        if (sa is not null && sb is not null && sa.PlainHits >= MinPlainHits && sb.PlainHits >= MinPlainHits && sa.PlainMean > 0)
        {
            WelchResult? w = DummyStatistics.Welch(sa.PlainMean, sa.PlainSd, sa.PlainHits, sb.PlainMean, sb.PlainSd, sb.PlainHits);
            if (w is WelchResult r)
            {
                change = r.Difference / sa.PlainMean;
                low = r.Low / sa.PlainMean;
                high = r.High / sa.PlainMean;
                p = r.P;
                verdict = !r.Significant ? DummyTestVerdict.NoMeasurableDifference
                    : r.Difference > 0 ? DummyTestVerdict.Better : DummyTestVerdict.Worse;
            }
        }

        bool critDiffers = sa is not null && sb is not null && sa.Hits >= MinPlainHits && sb.Hits >= MinPlainHits
            && DummyStatistics.TwoProportionP(sa.CritHits, sa.Hits, sb.CritHits, sb.Hits) < 0.05;
        int? levelA = baseId != 0 && a.Levels is not null && a.Levels.TryGetValue(baseId, out int la) ? la : null;
        int? levelB = baseId != 0 && b.Levels is not null && b.Levels.TryGetValue(baseId, out int lb) ? lb : null;
        return new DummyTestRow(any.Skill, baseId, sa, sb, change, low, high, p, verdict, critDiffers, levelA, levelB);
    }

    private static DummyDpsComparison CompareDps(Side a, Side b)
    {
        double dpsA = DummyStatistics.Mean(a.Summaries.Select(s => s.Dps).ToList());
        double dpsB = DummyStatistics.Mean(b.Summaries.Select(s => s.Dps).ToList());
        double delta = dpsB - dpsA;
        double seA, seB, crit;
        DummyNoiseMethod method;
        if (a.Summaries.Count >= 3 && b.Summaries.Count >= 3)
        {
            method = DummyNoiseMethod.RunToRun;
            int nA = a.Summaries.Count, nB = b.Summaries.Count;
            seA = DummyStatistics.SampleSd(a.Summaries.Select(s => s.Dps).ToList()) / Math.Sqrt(nA);
            seB = DummyStatistics.SampleSd(b.Summaries.Select(s => s.Dps).ToList()) / Math.Sqrt(nB);
            double v = seA * seA + seB * seB;
            double denom = seA * seA * seA * seA / (nA - 1) + seB * seB * seB * seB / (nB - 1);
            crit = v > 0 && denom > 0 ? DummyStatistics.StudentCritical(v * v / denom) : DummyStatistics.Z95;
        }
        else
        {
            method = DummyNoiseMethod.HitSpread;
            seA = Math.Sqrt(a.Summaries.Sum(s => s.DpsStandardError * s.DpsStandardError)) / a.Summaries.Count;
            seB = Math.Sqrt(b.Summaries.Sum(s => s.DpsStandardError * s.DpsStandardError)) / b.Summaries.Count;
            crit = DummyStatistics.Z95;
        }

        double margin = crit * Math.Sqrt(seA * seA + seB * seB);
        bool within = Math.Abs(delta) < margin;
        double? fraction = dpsA > 0 ? delta / dpsA : null;
        string basis = method == DummyNoiseMethod.RunToRun
            ? $"the run-to-run spread of {a.Summaries.Count} and {b.Summaries.Count} runs"
            : "the spread of the hits inside the run(s) (too few runs to measure the run-to-run spread; three per side are needed)";
        string change = $"{FormatSigned(delta)} DPS" + (fraction is double f ? $" ({FormatPercent(f)})" : "");
        string statement = within
            ? $"DPS {change}: within noise. A change that small is smaller than the +/-{margin.ToString("N0", Inv)} DPS that {basis} gives."
            : $"DPS {change}: beyond noise (more than the +/-{margin.ToString("N0", Inv)} DPS that {basis} gives).";
        return new DummyDpsComparison(dpsA, dpsB, delta, fraction, seA, seB, margin, within, method, statement);
    }

    private static DummyLevelConclusion ConcludeLevels(Side a, Side b, IReadOnlyList<DummyTestRow> rows)
    {
        if (a.Levels is null || b.Levels is null)
        {
            return new DummyLevelConclusion(DummyLevelConclusionKind.LevelsUnknown, Array.Empty<int>(), null, null, null,
                "Skill levels were not confirmed on both sides: no level conclusion. The skill comparison below still holds.");
        }

        List<int> changed = a.Levels.Where(kv => b.Levels.TryGetValue(kv.Key, out int lb) && lb != kv.Value)
            .Select(kv => kv.Key).OrderBy(id => id).ToList();
        string NameOf(int skillBase) => rows.FirstOrDefault(r => r.BaseId == skillBase)?.Skill ?? "skill #" + skillBase.ToString(Inv);
        if (changed.Count == 0)
        {
            return new DummyLevelConclusion(DummyLevelConclusionKind.NoLevelChange, changed, null, null, null,
                "No skill level changed between A and B: any difference in damage comes from something else (gear, luck, rotation, buffs).");
        }

        if (changed.Count >= 2)
        {
            return new DummyLevelConclusion(DummyLevelConclusionKind.Mixed, changed, null, null, null,
                $"Mixed: {changed.Count} skills changed level ({string.Join(", ", changed.Select(NameOf))}), so the DPS change cannot be pinned on one of them. "
                + "Each skill's own plain-hit change below is still valid. Change one skill at a time to learn what each level is worth.");
        }

        int single = changed[0];
        DummyTestRow? row = rows.FirstOrDefault(r => r.BaseId == single);
        string name = NameOf(single);
        string levels = $"level {a.Levels[single]} -> {b.Levels[single]}";
        string text;
        if (row is { PlainChange: double c, PlainChangeLow: double lo, PlainChangeHigh: double hi })
        {
            string tail = row.Verdict switch
            {
                DummyTestVerdict.Better => "a measurable gain",
                DummyTestVerdict.Worse => "a measurable loss",
                _ => "no measurable difference",
            };
            text = $"Only {name} changed ({levels}): its plain hit {FormatPercent(c)} (95 % interval {FormatPercent(lo)} to {FormatPercent(hi)}), {tail}.";
        }
        else
        {
            text = $"Only {name} changed ({levels}), but there are not enough plain hits of it (at least {MinPlainHits} per side) to say what the level did.";
        }

        return new DummyLevelConclusion(DummyLevelConclusionKind.SingleChange, changed, single, name, row, text);
    }

    // ---------------------------------------------------------------- level curves

    /// <summary>
    /// What one level of each skill is worth, from every complete run with confirmed levels. Per skill a point is (level,
    /// plain mean of that run); with two or more distinct levels the slope per level is fitted. Ranked by the estimated DPS
    /// gain of one more level, highest first; skills tested at one level only come last, with the advice to test another.
    /// </summary>
    public static IReadOnlyList<DummyLevelCurve> LevelCurves(IReadOnlyList<DummyRun> runs)
    {
        var perSkill = new Dictionary<int, List<(DummyRunSummary Summary, DummySkillStats Stats, int Level)>>();
        foreach (DummyRun run in runs.Where(r => r.Complete && r.LevelsConfirmed && r.Levels is not null))
        {
            DummyRunSummary summary = Summarize(run);
            var levelOf = run.Levels!.GroupBy(l => BaseOf(l.SkillId)).ToDictionary(g => g.Key, g => g.Max(l => l.Level));
            foreach (DummySkillStats s in summary.Skills)
            {
                if (s.BaseId == 0 || s.PlainHits < MinCurvePointHits || !levelOf.TryGetValue(s.BaseId, out int level))
                {
                    continue;
                }

                if (!perSkill.TryGetValue(s.BaseId, out var list))
                {
                    perSkill[s.BaseId] = list = new();
                }

                list.Add((summary, s, level));
            }
        }

        var curves = new List<DummyLevelCurve>();
        foreach ((int baseId, var entries) in perSkill)
        {
            curves.Add(BuildCurve(baseId, entries));
        }

        return curves
            .OrderBy(c => c.DpsGainPerLevel is null ? 1 : 0)
            .ThenByDescending(c => c.DpsGainPerLevel ?? 0)
            .ThenBy(c => c.Skill, StringComparer.Ordinal)
            .ToList();
    }

    private static DummyLevelCurve BuildCurve(int baseId, List<(DummyRunSummary Summary, DummySkillStats Stats, int Level)> entries)
    {
        var points = entries.Select(e => new DummyCurvePoint(e.Level, e.Stats.PlainMean, e.Stats.PlainHits, e.Summary.Run.Number))
            .OrderBy(p => p.Level).ThenBy(p => p.RunNumber).ToList();
        int distinct = points.Select(p => p.Level).Distinct().Count();
        string skill = entries[0].Stats.Skill;

        // The latest run that cast the skill stands for the current rotation.
        var latest = entries.OrderByDescending(e => e.Summary.Run.Start).First();
        double minutes = latest.Summary.Run.Planned.TotalMinutes;
        double hitsPerMinute = minutes > 0 ? latest.Stats.Hits / minutes : 0;
        double critRate = latest.Stats.CritRate;
        double? measured = latest.Stats.CritMultiplier;
        if (measured is null)
        {
            List<double> others = entries.Where(e => e.Stats.CritMultiplier is not null).Select(e => e.Stats.CritMultiplier!.Value).ToList();
            if (others.Count > 0)
            {
                measured = DummyStatistics.Median(others);
            }
        }

        bool assumed = measured is null;
        double multiplier = measured ?? AssumedCritMultiplier;

        LineFit? fit = distinct >= 2
            ? DummyStatistics.LinearFit(points.Select(p => (double)p.Level).ToList(), points.Select(p => p.PlainMean).ToList())
            : null;
        if (fit is not LineFit f)
        {
            string only = $"Only level {points[0].Level} was tested: test another level to see what a level is worth.";
            return new DummyLevelCurve(baseId, skill, points, distinct, null, null, null, null, null, critRate, multiplier, assumed,
                hitsPerMinute, null, DummyCurveTier.NotMeasurable, only);
        }

        double atLowest = f.Intercept + f.Slope * points[0].Level;
        double? relative = atLowest > 0 ? f.Slope / atLowest : null;
        double gain = f.Slope * (1 + critRate * (multiplier - 1)) * hitsPerMinute;
        bool excludesZero = f.SlopeLow is double lo && f.SlopeHigh is double hi && (lo > 0 || hi < 0);
        DummyCurveTier tier = distinct >= 3 || excludesZero ? DummyCurveTier.Measured : DummyCurveTier.TwoPoints;
        string note = tier == DummyCurveTier.Measured
            ? $"Measured over {distinct} levels."
            : "A line through two levels: the slope has no honest error yet, test a third level.";
        if (assumed)
        {
            note += $" Crit multiplier not measured, x{AssumedCritMultiplier.ToString("0.00", Inv)} assumed.";
        }

        return new DummyLevelCurve(baseId, skill, points, distinct, f.Slope, f.SlopeStandardError, f.SlopeLow, f.SlopeHigh, relative,
            critRate, multiplier, assumed, hitsPerMinute, gain, tier, note);
    }

    // ---------------------------------------------------------------- text

    /// <summary>"+6.2 %" for 0.062.</summary>
    public static string FormatPercent(double fraction) => (fraction * 100).ToString("+0.0;-0.0;0.0", Inv) + " %";

    private static string FormatRate(double fraction) => (fraction * 100).ToString("0.0", Inv) + " %";

    private static string FormatSigned(double value) => value.ToString("+#,##0;-#,##0;0", Inv);
}
