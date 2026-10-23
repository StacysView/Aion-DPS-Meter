using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>The dummy test's statistics and comparison on synthetic runs from a seeded random: the t-test and Wilson interval
/// against hand-computed values, the run window and the plain-hit rule, "no measurable difference" for the same setup,
/// a +6 % plain mean detected, a 4 % change over 15 hits not called better, mixed levels never naming a skill, runs of
/// different lengths refused, and the level curve recovering a planted +3 % per level.</summary>
public static class SelfCheckDummyTest
{
    private const int CleaveId = 11010010;
    private const int BoltId = 12010010;
    private const int FireId = 13010010;
    private static readonly DateTime Origin = new(2026, 10, 23, 20, 0, 0);

    public static bool Run()
    {
        bool ok = true;
        ok &= RunStatisticsScenario();
        ok &= RunWindowScenario();
        ok &= RunPlainHitScenario();
        ok &= RunSameConfigScenario();
        ok &= RunDetectsGainScenario();
        ok &= RunSmallDifferenceScenario();
        ok &= RunLevelConclusionScenario();
        ok &= RunRefusalAndWarningsScenario();
        ok &= RunDpsNoiseScenario();
        ok &= RunCurveScenario();
        return ok;
    }

    /// <summary>One skill of a synthetic run: <paramref name="Hits"/> hits around <paramref name="Mean"/> with a relative spread
    /// <paramref name="Sd"/>, a share of them crit (x1.85) or double (x2), at <paramref name="Level"/>.</summary>
    private sealed record Spec(int SkillId, string Name, double Mean, int Hits, int Level, double Sd = 0.08, double Crit = 0.2, double Double = 0);

    private static double Normal(Random rng) =>
        Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    /// <summary>A complete run: the specs' hits spread evenly over the planned time, one accepted use per hit.</summary>
    private static DummyRun MakeRun(Random rng, int number, TimeSpan planned, params Spec[] specs)
    {
        DateTime start = Origin.AddMinutes(10 * number);
        var run = new DummyRun
        {
            Number = number, Planned = planned, Length = planned, Start = start, PlayerId = 1,
            TargetName = "Training Scarecrow", LevelsConfirmed = true, Levels = new List<SkillLevel>(),
        };
        foreach (Spec s in specs)
        {
            run.Levels!.Add(new SkillLevel(s.SkillId, s.Level, s.Level));
            for (int i = 0; i < s.Hits; i++)
            {
                DateTime t = start + planned * ((i + 0.5) / s.Hits);
                long amount = Math.Max(1, (long)Math.Round(s.Mean * (1 + s.Sd * Normal(rng))));
                double roll = rng.NextDouble();
                HitKind kind = HitKind.None;
                if (roll < s.Crit)
                {
                    amount = (long)Math.Round(amount * 1.85);
                    kind = HitKind.Crit;
                }
                else if (roll < s.Crit + s.Double)
                {
                    amount *= 2;
                    kind = HitKind.Double;
                }

                run.Events.Add(new DamageEvent(t, 1, 100, amount, false, s.Name, kind == HitKind.Crit, false, s.SkillId, kind));
                run.Uses.Add(new SkillUse(t.AddMilliseconds(-100), s.SkillId, true));
            }
        }

        return run;
    }

    private static bool Near(double value, double expected, double tolerance) => Math.Abs(value - expected) <= tolerance;

    private static bool RunStatisticsScenario()
    {
        Console.WriteLine("[selftest] Dummy test statistics: t quantiles, Welch, Wilson, line fit against hand-computed values:");
        var sample = new List<double> { 2, 4, 4, 4, 5, 5, 7, 9 };
        bool basics = Near(DummyStatistics.Mean(sample), 5, 1e-9) && Near(DummyStatistics.SampleSd(sample), Math.Sqrt(32.0 / 7), 1e-9)
            && Near(DummyStatistics.Median(new List<double> { 3, 1, 2 }), 2, 1e-9) && Near(DummyStatistics.Median(new List<double> { 1, 2, 3, 4 }), 2.5, 1e-9);
        bool quantiles = Near(DummyStatistics.StudentCritical(1), 12.706, 0.005) && Near(DummyStatistics.StudentCritical(5), 2.5706, 0.001)
            && Near(DummyStatistics.StudentCritical(10), 2.2281, 0.001) && Near(DummyStatistics.StudentCritical(30), 2.0423, 0.001)
            && Near(DummyStatistics.StudentCritical(100000), 1.96, 0.002) && Near(DummyStatistics.NormalTwoSidedP(1.96), 0.05, 0.001);

        // A: mean 10, SD 2, n 10; B: mean 12, SD 3, n 12. se = sqrt(0.4 + 0.75), t = 2 / se, df = 1.15^2 / (0.4^2/9 + 0.75^2/11).
        WelchResult? w = DummyStatistics.Welch(10, 2, 10, 12, 3, 12);
        bool welch = w is WelchResult r && Near(r.T, 1.865, 0.001) && Near(r.Df, 19.19, 0.05) && Near(r.P, 0.0775, 0.001)
            && r.Low < 0 && r.High > 2 && !r.Significant && DummyStatistics.Welch(10, 2, 1, 12, 3, 12) is null;
        WelchResult? clear = DummyStatistics.Welch(100, 5, 50, 110, 5, 50);
        bool welchClear = clear is { Significant: true, P: < 0.001 } && clear.Value.Low > 5;

        RateInterval half = DummyStatistics.Wilson(5, 10);
        RateInterval none = DummyStatistics.Wilson(0, 10);
        bool wilson = Near(half.Low, 0.2366, 0.001) && Near(half.High, 0.7634, 0.001) && Near(none.Low, 0, 1e-9) && Near(none.High, 0.2775, 0.001)
            && DummyStatistics.Wilson(0, 0) is { Low: 0, High: 1 };

        var xs = new List<double> { 1, 2, 3, 4, 5 };
        LineFit? line = DummyStatistics.LinearFit(xs, new List<double> { 2.1, 3.9, 6.2, 7.8, 10.1 });
        bool fit = line is LineFit l && Near(l.Slope, 1.99, 1e-9) && Near(l.Intercept, 0.05, 1e-9) && Near(l.SlopeStandardError ?? -1, 0.0597, 0.0005)
            && DummyStatistics.LinearFit(new List<double> { 1, 2 }, new List<double> { 1, 3 }) is { SlopeStandardError: null }
            && DummyStatistics.LinearFit(new List<double> { 4, 4 }, new List<double> { 1, 3 }) is null;
        bool ok = basics && quantiles && welch && welchClear && wilson && fit;
        Console.WriteLine($"  -> mean/SD/median {basics}, t quantiles {quantiles}, Welch {welch}/{welchClear}, Wilson {wilson}, line fit {fit}: {ok}");
        return ok;
    }

    /// <summary>Only events and uses inside [Start, Start + Planned] count, DPS divides by Planned.</summary>
    private static bool RunWindowScenario()
    {
        Console.WriteLine("[selftest] Dummy test window: late and early events out, DPS over the planned seconds:");
        DateTime start = Origin;
        var run = new DummyRun { Number = 1, Planned = TimeSpan.FromSeconds(60), Length = TimeSpan.FromSeconds(60), Start = start, PlayerId = 1 };
        for (int i = 0; i < 60; i++)
        {
            run.Events.Add(new DamageEvent(start.AddSeconds(i), 1, 100, 1000, false, "Cleave", SkillId: CleaveId));
            run.Uses.Add(new SkillUse(start.AddSeconds(i), CleaveId, true));
        }

        run.Events.Add(new DamageEvent(start.AddSeconds(61), 1, 100, 99999, false, "Cleave", SkillId: CleaveId));
        run.Events.Add(new DamageEvent(start.AddSeconds(-1), 1, 100, 99999, false, "Cleave", SkillId: CleaveId));
        run.Events.Add(new DamageEvent(start.AddSeconds(5), 1, 1, 5000, true, "Heal", SkillId: BoltId));
        run.Uses.Add(new SkillUse(start.AddSeconds(3), CleaveId, false, 7));
        run.Uses.Add(new SkillUse(start.AddSeconds(90), CleaveId, true));
        DummyRunSummary sum = DummyAnalysis.Summarize(run);
        DummySkillStats? cleave = sum.Skills.SingleOrDefault();
        bool ok = sum.TotalDamage == 60000 && Near(sum.Dps, 1000, 1e-9) && sum.Hits == 60 && cleave is not null
            && cleave.Uses == 60 && cleave.RefusedUses == 1 && Near(cleave.UsesPerMinute, 60, 1e-9) && Near(cleave.HitsPerUse ?? -1, 1, 1e-9)
            && Near(cleave.Share, 1, 1e-9) && Near(cleave.Dps, 1000, 1e-9) && cleave.Skill == "Cleave" && cleave.BaseId == 11010000;
        Console.WriteLine($"  -> 60 hits of 1000 = 1000 DPS, 1 refused, nothing outside the window, heal ignored: {ok}");
        return ok;
    }

    /// <summary>Crits, doubles, perfects, parries and ticks never enter the plain mean.</summary>
    private static bool RunPlainHitScenario()
    {
        Console.WriteLine("[selftest] Dummy test plain hits: crit, double, perfect, parry and ticks stay out of the plain mean:");
        DateTime start = Origin;
        var run = new DummyRun { Number = 1, Planned = TimeSpan.FromSeconds(30), Length = TimeSpan.FromSeconds(30), Start = start, PlayerId = 1 };
        int n = 0;
        void Add(long amount, HitKind kind, bool tick = false) =>
            run.Events.Add(new DamageEvent(start.AddMilliseconds(100 * n++), 1, 100, amount, false, "Cleave", (kind & HitKind.Crit) != 0, tick, CleaveId, kind));
        for (int i = 0; i < 20; i++) { Add(1000, HitKind.None); }
        for (int i = 0; i < 5; i++) { Add(1850, HitKind.Crit); }
        for (int i = 0; i < 5; i++) { Add(2000, HitKind.Double); }
        for (int i = 0; i < 3; i++) { Add(1100, HitKind.Perfect); }
        for (int i = 0; i < 2; i++) { Add(400, HitKind.Parry); }
        for (int i = 0; i < 4; i++) { Add(300, HitKind.None, tick: true); }
        DummySkillStats s = DummyAnalysis.Summarize(run).Skills.Single();
        bool ok = s.PlainHits == 20 && Near(s.PlainMean, 1000, 1e-9) && Near(s.PlainSd, 0, 1e-9) && s.PlainMin == 1000 && s.PlainMax == 1000
            && s.Hits == 35 && s.CritHits == 5 && Near(s.CritRate, 5.0 / 35, 1e-9) && Near(s.DoubleRate, 5.0 / 35, 1e-9)
            && Near(s.PerfectRate, 3.0 / 35, 1e-9) && Near(s.ParryRate, 2.0 / 35, 1e-9) && Near(s.CritMultiplier ?? 0, 1.85, 1e-9)
            && s.TotalDamage == 44550 && s.TickDamage == 1200;
        Console.WriteLine($"  -> 20 plain of 35 hits, plain mean 1000, crit multiplier 1.85, ticks only in the total: {ok}");
        return ok;
    }

    private static DummyTestRow SingleRow(DummyTestComparison c) => c.Rows.Single(r => r.BaseId == 11010000);

    /// <summary>The same setup twice must not produce a "better" or "worse" except by the 5 % the interval allows.</summary>
    private static bool RunSameConfigScenario()
    {
        Console.WriteLine("[selftest] Dummy test, same setup twice: no measurable difference (false alarms near 5 %):");
        var rng = new Random(20261023);
        TimeSpan planned = TimeSpan.FromMinutes(1);
        int falseAlarms = 0, trials = 40;
        bool levelsChanged = false;
        int levelsChangeds = 0;
        for (int i = 0; i < trials; i++)
        {
            DummyTestComparison c = DummyAnalysis.Compare(
                new[] { MakeRun(rng, 1, planned, new Spec(CleaveId, "Cleave", 120_000, 100, 10)) },
                new[] { MakeRun(rng, 2, planned, new Spec(CleaveId, "Cleave", 120_000, 100, 10)) });
            if (SingleRow(c).Verdict is DummyTestVerdict.Better or DummyTestVerdict.Worse)
            {
                falseAlarms++;
            }

            if (!c.Dps!.WithinNoise)
            {
                levelsChangeds++;
            }

            levelsChanged |= c.Levels!.Kind != DummyLevelConclusionKind.NoLevelChange;
        }

        // Binomial(40, 0.05): 9 or more false alarms has a probability below 0.1 %.
        bool ok = falseAlarms <= 8 && levelsChangeds <= 8 && !levelsChanged;
        Console.WriteLine($"  -> {falseAlarms} of {trials} skill verdicts and {levelsChangeds} DPS verdicts raised a false alarm (expected about 2), levels unchanged: {ok}");
        return ok;
    }

    private static bool RunDetectsGainScenario()
    {
        Console.WriteLine("[selftest] Dummy test: a +6 % plain mean over about 80 plain hits is detected:");
        var rng = new Random(77);
        TimeSpan planned = TimeSpan.FromMinutes(1);
        DummyTestComparison c = DummyAnalysis.Compare(
            new[] { MakeRun(rng, 1, planned, new Spec(CleaveId, "Cleave", 100_000, 100, 10)) },
            new[] { MakeRun(rng, 2, planned, new Spec(CleaveId, "Cleave", 106_000, 100, 11)) });
        DummyTestRow row = SingleRow(c);
        bool ok = row.Verdict == DummyTestVerdict.Better && row.PlainChange is double ch && ch is > 0.02 and < 0.10
            && row.PlainChangeLow > 0 && row.PlainChangeHigh > row.PlainChangeLow && row.PValue < 0.05
            && c.Levels!.Kind == DummyLevelConclusionKind.SingleChange && c.Levels.Skill == "Cleave" && c.Levels.Text.Contains("Cleave");
        Console.WriteLine($"  -> {row.Verdict}, change {(row.PlainChange is double x ? DummyAnalysis.FormatPercent(x) : "-")} ({row.PlainHitsText()}), level conclusion names Cleave: {ok}");
        return ok;
    }

    /// <summary>120k against 125k with an 8 % spread over 15 hits per side: the power is far too low to call it, so most
    /// pairs must come out undecided and never "worse"; fewer than 10 plain hits is "not enough data".</summary>
    private static bool RunSmallDifferenceScenario()
    {
        Console.WriteLine("[selftest] Dummy test: 120k against 125k over 15 hits per side is not called better:");
        var rng = new Random(4242);
        TimeSpan planned = TimeSpan.FromMinutes(1);
        int trials = 200, better = 0, worse = 0, undecided = 0;
        for (int i = 0; i < trials; i++)
        {
            DummyTestComparison c = DummyAnalysis.Compare(
                new[] { MakeRun(rng, 1, planned, new Spec(CleaveId, "Cleave", 120_000, 15, 10, Crit: 0)) },
                new[] { MakeRun(rng, 2, planned, new Spec(CleaveId, "Cleave", 125_000, 15, 11, Crit: 0)) });
            switch (SingleRow(c).Verdict)
            {
                case DummyTestVerdict.Better: better++; break;
                case DummyTestVerdict.Worse: worse++; break;
                default: undecided++; break;
            }
        }

        // A real +4 % at this noise is seen about 3 times in 10; the module must say "undecided" the other 7.
        DummyTestComparison few = DummyAnalysis.Compare(
            new[] { MakeRun(rng, 1, planned, new Spec(CleaveId, "Cleave", 120_000, 6, 10, Crit: 0)) },
            new[] { MakeRun(rng, 2, planned, new Spec(CleaveId, "Cleave", 150_000, 6, 11, Crit: 0)) });
        DummyTestRow fewRow = SingleRow(few);
        bool fewOk = fewRow.Verdict == DummyTestVerdict.NotEnoughData && fewRow.PlainChange is null
            && few.Warnings.Any(w => w.Kind == DummyTestWarningKind.FewPlainHits);
        bool ok = better < trials * 0.45 && undecided > trials * 0.5 && worse <= 3 && fewOk;
        Console.WriteLine($"  -> of {trials} pairs: {better} better, {undecided} undecided, {worse} worse; 6 hits per side is NotEnoughData: {fewOk}: {ok}");
        return ok;
    }

    private static bool RunLevelConclusionScenario()
    {
        Console.WriteLine("[selftest] Dummy test levels: none, one (named), two or more (mixed, nobody named), unconfirmed:");
        var rng = new Random(99);
        TimeSpan planned = TimeSpan.FromMinutes(1);
        DummyRun Pair(int number, double cleave, int cleaveLevel, double bolt, int boltLevel, bool confirmed = true)
        {
            DummyRun r = MakeRun(rng, number, planned,
                new Spec(CleaveId, "Cleave", cleave, 100, cleaveLevel), new Spec(BoltId, "Bolt", bolt, 100, boltLevel));
            r.LevelsConfirmed = confirmed;
            return r;
        }

        DummyTestComparison none = DummyAnalysis.Compare(new[] { Pair(1, 100_000, 10, 50_000, 10) }, new[] { Pair(2, 100_000, 10, 50_000, 10) });
        DummyTestComparison single = DummyAnalysis.Compare(new[] { Pair(1, 100_000, 10, 50_000, 10) }, new[] { Pair(2, 112_000, 11, 50_000, 10) });
        DummyTestComparison mixed = DummyAnalysis.Compare(new[] { Pair(1, 100_000, 10, 50_000, 10) }, new[] { Pair(2, 112_000, 11, 56_000, 11) });
        DummyTestComparison unknown = DummyAnalysis.Compare(new[] { Pair(1, 100_000, 10, 50_000, 10) }, new[] { Pair(2, 112_000, 11, 50_000, 10, confirmed: false) });

        bool noneOk = none.Levels!.Kind == DummyLevelConclusionKind.NoLevelChange && none.Levels.Skill is null && none.Rows.All(r => !r.LevelChanged);
        bool singleOk = single.Levels!.Kind == DummyLevelConclusionKind.SingleChange && single.Levels.Skill == "Cleave"
            && single.Levels.BaseId == 11010000 && single.Levels.Row is { Verdict: DummyTestVerdict.Better }
            && single.Rows.Single(r => r.Skill == "Cleave").LevelChanged && !single.Rows.Single(r => r.Skill == "Bolt").LevelChanged;
        bool mixedOk = mixed.Levels!.Kind == DummyLevelConclusionKind.Mixed && mixed.Levels.Skill is null && mixed.Levels.Row is null
            && mixed.Levels.ChangedBaseIds.Count == 2 && mixed.Levels.Text.Contains("one skill at a time")
            && mixed.Rows.Count == 2 && mixed.Rows.All(r => r.Verdict == DummyTestVerdict.Better && r.LevelChanged);
        bool unknownOk = unknown.Levels!.Kind == DummyLevelConclusionKind.LevelsUnknown
            && unknown.Warnings.Any(w => w.Kind == DummyTestWarningKind.LevelsNotConfirmed) && unknown.Rows.All(r => !r.LevelChanged);
        bool ok = noneOk && singleOk && mixedOk && unknownOk;
        Console.WriteLine($"  -> none {noneOk}, single names Cleave {singleOk}, mixed names nobody {mixedOk}, unconfirmed {unknownOk}: {ok}");
        return ok;
    }

    private static bool RunRefusalAndWarningsScenario()
    {
        Console.WriteLine("[selftest] Dummy test refusal and warnings: lengths, incomplete runs, target, stats, effects:");
        var rng = new Random(5);
        Spec spec = new(CleaveId, "Cleave", 100_000, 60, 10);
        DummyRun A() => MakeRun(rng, 1, TimeSpan.FromMinutes(1), spec);
        DummyRun B() => MakeRun(rng, 2, TimeSpan.FromMinutes(1), spec);

        DummyTestComparison lengths = DummyAnalysis.Compare(new[] { A() }, new[] { MakeRun(rng, 2, TimeSpan.FromMinutes(2), spec) });
        bool lengthsOk = !lengths.Comparable && lengths.Refusal is not null && lengths.Dps is null && lengths.Levels is null && lengths.Rows.Count == 0
            && lengths.Warnings.Any(w => w.Kind == DummyTestWarningKind.PlannedLengthsDiffer);

        DummyRun early = B();
        early.Length = TimeSpan.FromSeconds(20);
        DummyTestComparison incomplete = DummyAnalysis.Compare(new[] { A() }, new[] { B(), early });
        DummyTestComparison onlyEarly = DummyAnalysis.Compare(new[] { A() }, new[] { early });
        bool incompleteOk = incomplete.Comparable && incomplete.RunsB == 1 && incomplete.Warnings.Any(w => w.Kind == DummyTestWarningKind.IncompleteRunsExcluded)
            && !onlyEarly.Comparable && onlyEarly.Warnings.Any(w => w.Kind == DummyTestWarningKind.NoCompleteRuns);

        DummyRun a = A(), b = B();
        b.TargetName = "Training Golem";
        a.Stats = new StatSnapshot(Origin, new Dictionary<int, int> { [1] = 100, [7] = 50 });
        b.Stats = new StatSnapshot(Origin, new Dictionary<int, int> { [1] = 100, [7] = 55 });
        b.Effects.Add(new EffectChange(Origin, 5001, true));
        DummyTestComparison ctx = DummyAnalysis.Compare(new[] { a }, new[] { b });
        bool ctxOk = ctx.Warnings.Any(w => w.Kind == DummyTestWarningKind.TargetDiffers && w.Names!.Contains("Training Golem"))
            && ctx.Warnings.Any(w => w.Kind == DummyTestWarningKind.StatsDiffer && w.Ids!.SequenceEqual(new[] { 7 }))
            && ctx.Warnings.Any(w => w.Kind == DummyTestWarningKind.EffectsDiffer && w.Ids!.SequenceEqual(new[] { 5001 }));

        // Same skill, a very different crit rate: a level cannot do that.
        DummyTestComparison crit = DummyAnalysis.Compare(
            new[] { MakeRun(rng, 1, TimeSpan.FromMinutes(1), spec with { Hits = 100, Crit = 0.1 }) },
            new[] { MakeRun(rng, 2, TimeSpan.FromMinutes(1), spec with { Hits = 100, Crit = 0.5 }) });
        bool critOk = SingleRow(crit).CritRateDiffers && crit.Warnings.Any(w => w.Kind == DummyTestWarningKind.CritRateDiffers);
        bool ok = lengthsOk && incompleteOk && ctxOk && critOk;
        Console.WriteLine($"  -> different lengths refused {lengthsOk}, incomplete excluded {incompleteOk}, target/stats/effects named {ctxOk}, crit rate flagged {critOk}: {ok}");
        return ok;
    }

    private static bool RunDpsNoiseScenario()
    {
        Console.WriteLine("[selftest] Dummy test total DPS: within noise from the hit spread, beyond noise from run-to-run with three runs:");
        var rng = new Random(31337);
        TimeSpan planned = TimeSpan.FromMinutes(1);
        DummyTestComparison same = DummyAnalysis.Compare(
            new[] { MakeRun(rng, 1, planned, new Spec(CleaveId, "Cleave", 120_000, 100, 10)) },
            new[] { MakeRun(rng, 2, planned, new Spec(CleaveId, "Cleave", 120_000, 100, 10)) });
        bool hitSpread = same.Dps!.Method == DummyNoiseMethod.HitSpread && same.Dps.Statement.Contains("noise") && same.Dps.Margin > 0;

        DummyRun[] ra = Enumerable.Range(1, 3).Select(i => MakeRun(rng, i, planned, new Spec(CleaveId, "Cleave", 100_000, 100, 10))).ToArray();
        DummyRun[] rb = Enumerable.Range(4, 3).Select(i => MakeRun(rng, i, planned, new Spec(CleaveId, "Cleave", 115_000, 100, 11))).ToArray();
        DummyTestComparison big = DummyAnalysis.Compare(ra, rb);
        bool runToRun = big.Dps!.Method == DummyNoiseMethod.RunToRun && !big.Dps.WithinNoise && big.Dps.Delta > 0
            && big.Dps.DeltaFraction is double f && f is > 0.08 and < 0.25 && big.RunsA == 3 && big.RunsB == 3 && big.Dps.Statement.Contains("run-to-run");
        bool ok = hitSpread && runToRun;
        Console.WriteLine($"  -> one run per side uses the hit spread {hitSpread}, three per side use run-to-run and see +15 % {runToRun}: {ok}");
        return ok;
    }

    private static bool RunCurveScenario()
    {
        Console.WriteLine("[selftest] Dummy test level curve: planted +3 % per level recovered, one level is not rankable:");
        var rng = new Random(2024);
        TimeSpan planned = TimeSpan.FromMinutes(1);
        var runs = new List<DummyRun>();
        for (int level = 14; level <= 20; level++)
        {
            runs.Add(MakeRun(rng, level, planned,
                new Spec(CleaveId, "Cleave", 100_000 * (1 + 0.03 * (level - 14)), 60, level),
                new Spec(BoltId, "Bolt", 40_000, 60, 5),
                new Spec(FireId, "Fire", 20_000 * (1 + 0.03 * (level - 14)), 60, level >= 19 ? level : 18)));
        }

        IReadOnlyList<DummyLevelCurve> curves = DummyAnalysis.LevelCurves(runs);
        DummyLevelCurve cleave = curves.Single(c => c.Skill == "Cleave");
        DummyLevelCurve bolt = curves.Single(c => c.Skill == "Bolt");
        DummyLevelCurve fire = curves.Single(c => c.Skill == "Fire");
        bool slope = cleave.Tier == DummyCurveTier.Measured && cleave.DistinctLevels == 7 && cleave.RelativeSlope is double rel && Near(rel, 0.03, 0.008)
            && cleave.SlopePerLevel is double sl && Near(sl, 3000, 800) && cleave.SlopeStandardError is double se && se > 0 && se < 1500
            && cleave.DpsGainPerLevel is double gain && gain > 0 && cleave.HitsPerMinute > 0;
        bool oneLevel = bolt.Tier == DummyCurveTier.NotMeasurable && bolt.SlopePerLevel is null && bolt.DpsGainPerLevel is null && bolt.Note.Contains("test another level");
        bool threeLevels = fire.DistinctLevels == 3 && fire.Tier != DummyCurveTier.NotMeasurable && fire.DpsGainPerLevel is not null;
        bool order = curves[^1].Skill == "Bolt" && Array.IndexOf(curves.Select(c => c.Skill).ToArray(), "Cleave") < Array.IndexOf(curves.Select(c => c.Skill).ToArray(), "Fire");
        bool ok = slope && oneLevel && threeLevels && order;
        Console.WriteLine($"  -> Cleave slope {(cleave.RelativeSlope is double r2 ? DummyAnalysis.FormatPercent(r2) : "-")} per level {slope}, Bolt not measurable {oneLevel}, ranked {order}: {ok}");
        return ok;
    }

    private static string PlainHitsText(this DummyTestRow row) => $"{row.A?.PlainHits ?? 0} and {row.B?.PlainHits ?? 0} plain hits";
}
