using System.Globalization;
using System.Text;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>How a text of the dummy test is written: the string table, the number culture and the skill-name mapper, so the pure text code below
/// runs in the window and in the self-check alike (which passes a fake table and the invariant culture).</summary>
public sealed record DummyTextEnv(Func<string, string> Loc, CultureInfo Culture, Func<string, string> SkillName)
{
    /// <summary>The meter's own: the current UI language, the current culture, skill names as the other windows show them.</summary>
    public static DummyTextEnv Current => new(key => LocalizationManager.Instance[key], CultureInfo.CurrentCulture, Aion2.Protocol.Aion2SkillNames.Display);

    public string Format(string key, params object[] args) => string.Format(Culture, Loc(key), args);

    public string Skill(string? englishName) => englishName is null ? "" : SkillName(englishName);
}

public enum DummyStatus
{
    /// <summary>Done, as it should be (green check).</summary>
    Ok,

    /// <summary>Something the player should do or know before the test counts for much (amber).</summary>
    Warn,

    /// <summary>Not known yet (grey).</summary>
    Pending,
}

/// <summary>One line of the setup checklist.</summary>
public sealed record DummyStatusLine(DummyStatus Status, string Text);

/// <summary>
/// The dummy test window's text, without any WPF: number and time formats, the setup checklist lines, the localized sentences for the comparison
/// (the analysis gives English text and the kinds and ids; these are written from those), and the two exports (a Discord table and a CSV).
/// Pure, so the self-check runs it. Every user-visible string goes through <see cref="DummyTextEnv.Loc"/> (assets/i18n/ui_strings.json, DummyTest.*).
/// </summary>
public static class DummyTestText
{
    /// <summary>The planned lengths offered, in seconds (the same lengths compare with each other).</summary>
    public static readonly IReadOnlyList<int> PresetSeconds = new[] { 30, 60, 120, 180, 300 };

    public const string Arrow = "→";

    /// <summary>The Discord table lists at most this many skills (the biggest by damage; the message limit is 2000 characters). The CSV has all.</summary>
    public const int MaxTableRows = 14;

    // ---------------------------------------------------------------- formats

    /// <summary>"1:07.4": minutes, seconds and tenths, rounded up so a countdown never shows 0:00.0 before it is over.</summary>
    public static string Clock(TimeSpan time, CultureInfo culture)
    {
        long tenths = (long)Math.Ceiling(Math.Max(0, time.TotalMilliseconds) / 100.0);
        return string.Format(culture, "{0}:{1:D2}{2}{3}", tenths / 600, tenths / 10 % 60, culture.NumberFormat.NumberDecimalSeparator, tenths % 10);
    }

    /// <summary>"30 s", "1 min", "2 min 30 s".</summary>
    public static string Duration(TimeSpan time)
    {
        int total = (int)Math.Round(time.TotalSeconds);
        if (total < 60)
        {
            return total.ToString(CultureInfo.InvariantCulture) + " s";
        }

        int minutes = total / 60, seconds = total % 60;
        return minutes.ToString(CultureInfo.InvariantCulture) + " min" + (seconds == 0 ? "" : " " + seconds.ToString(CultureInfo.InvariantCulture) + " s");
    }

    /// <summary>How long ago, coarsely: "12 s", "3 min", "2 h".</summary>
    public static string Age(TimeSpan age)
    {
        int seconds = (int)Math.Max(0, Math.Round(age.TotalSeconds));
        return seconds < 60 ? seconds.ToString(CultureInfo.InvariantCulture) + " s"
            : seconds < 3600 ? (seconds / 60).ToString(CultureInfo.InvariantCulture) + " min"
            : (seconds / 3600).ToString(CultureInfo.InvariantCulture) + " h";
    }

    public static string Number(double value, CultureInfo culture) => value.ToString("N0", culture);

    /// <summary>"+6.2 %" for 0.062.</summary>
    public static string Percent(double fraction, CultureInfo culture) => (fraction * 100).ToString("+0.0;-0.0;0.0", culture) + " %";

    /// <summary>"+1,234" (the sign always shown).</summary>
    public static string Signed(double value, CultureInfo culture) => value.ToString("+#,##0;-#,##0;0", culture);

    /// <summary>"[-1.2 % ; +4.5 %]"; empty when there is no interval.</summary>
    public static string Interval(double? low, double? high, CultureInfo culture) =>
        low is double l && high is double h ? "[" + Percent(l, culture) + " ; " + Percent(h, culture) + "]" : "";

    /// <summary>"18", "18 → 20", "-" when neither is known.</summary>
    public static string Levels(int? a, int? b, string arrow = Arrow) =>
        a is null && b is null ? "-" : a == b ? a!.Value.ToString(CultureInfo.InvariantCulture)
        : (a?.ToString(CultureInfo.InvariantCulture) ?? "?") + " " + arrow + " " + (b?.ToString(CultureInfo.InvariantCulture) ?? "?");

    /// <summary>A run's DPS in the list: damage over the planned time (the same divisor for every run of a length); a run stopped early over what it lasted.</summary>
    public static double RunDps(DummyRunRow row)
    {
        double seconds = row.Complete ? row.Planned.TotalSeconds : row.Length.TotalSeconds;
        return seconds >= 1 ? row.TotalDamage / seconds : row.TotalDamage;
    }

    /// <summary>"1 run" / "3 runs" (DummyTest.Runs.One and .Many).</summary>
    public static string Runs(int count, DummyTextEnv env) => env.Format(count == 1 ? "DummyTest.Runs.One" : "DummyTest.Runs.Many", count);

    // ---------------------------------------------------------------- per-skill cells

    public static string VerdictKey(DummyTestVerdict verdict) => verdict switch
    {
        DummyTestVerdict.Better => "DummyTest.Verdict.Better",
        DummyTestVerdict.Worse => "DummyTest.Verdict.Worse",
        DummyTestVerdict.NoMeasurableDifference => "DummyTest.Verdict.None",
        _ => "DummyTest.Verdict.NotEnough",
    };

    /// <summary>The verdict as a machine word, for the CSV.</summary>
    public static string VerdictToken(DummyTestVerdict verdict) => verdict switch
    {
        DummyTestVerdict.Better => "better",
        DummyTestVerdict.Worse => "worse",
        DummyTestVerdict.NoMeasurableDifference => "no_measurable_difference",
        _ => "not_enough_data",
    };

    private static string Pair(string a, string b, string arrow) => a + " " + arrow + " " + b;

    /// <summary>Accepted casts per minute, A then B ("-" when a side did not cast it by id).</summary>
    public static string UsesPerMinute(DummyTestRow row, CultureInfo culture, string arrow = Arrow)
    {
        static string Side(DummySkillStats? s, CultureInfo c) => s is { Uses: > 0 } ? s.UsesPerMinute.ToString("0.0", c) : "-";
        return Pair(Side(row.A, culture), Side(row.B, culture), arrow);
    }

    public static string PlainMean(double? mean, CultureInfo culture) => mean is double m ? Number(m, culture) : "-";

    /// <summary>The change of the plain hit, "+6.2 %" or "-".</summary>
    public static string Change(DummyTestRow row, CultureInfo culture) => row.PlainChange is double change ? Percent(change, culture) : "-";

    /// <summary>Crit rate A then B in percent: "20 → 22 %".</summary>
    public static string CritRate(DummyTestRow row, CultureInfo culture, string arrow = Arrow)
    {
        static string Side(DummySkillStats? s, CultureInfo c) => s is null ? "-" : (s.CritRate * 100).ToString("0", c);
        return Side(row.A, culture) + " " + arrow + " " + Side(row.B, culture) + " %";
    }

    // ---------------------------------------------------------------- the sentences of a comparison

    private static string SkillOf(DummyTestComparison comparison, int baseId, DummyTextEnv env) =>
        comparison.Rows.FirstOrDefault(r => r.BaseId == baseId) is { } row ? env.Skill(row.Skill) : "#" + baseId.ToString(CultureInfo.InvariantCulture);

    /// <summary>What the level change explains, in the UI language (the analysis's own text is English); empty for a comparison that was refused.</summary>
    public static string Conclusion(DummyTestComparison comparison, DummyTextEnv env)
    {
        if (comparison.Levels is not { } levels)
        {
            return "";
        }

        switch (levels.Kind)
        {
            case DummyLevelConclusionKind.LevelsUnknown:
                return env.Loc("DummyTest.Level.Unknown");
            case DummyLevelConclusionKind.NoLevelChange:
                return env.Loc("DummyTest.Level.NoChange");
            case DummyLevelConclusionKind.Mixed:
                return env.Format("DummyTest.Level.Mixed", levels.ChangedBaseIds.Count,
                    string.Join(", ", levels.ChangedBaseIds.Select(id => SkillOf(comparison, id, env))));
            default:
                string name = env.Skill(levels.Skill);
                if (levels.Row is { PlainChange: double change, PlainChangeLow: double low, PlainChangeHigh: double high } row)
                {
                    string key = row.Verdict switch
                    {
                        DummyTestVerdict.Better => "DummyTest.Level.Single.Better",
                        DummyTestVerdict.Worse => "DummyTest.Level.Single.Worse",
                        _ => "DummyTest.Level.Single.None",
                    };
                    return env.Format(key, name, Levels(row.LevelA, row.LevelB), Percent(change, env.Culture), Percent(low, env.Culture), Percent(high, env.Culture));
                }

                return env.Format("DummyTest.Level.Single.NoData", name, DummyAnalysis.MinPlainHits);
        }
    }

    /// <summary>The total DPS sentence: the change, whether it is inside the noise, and what the noise comes from.</summary>
    public static string DpsStatement(DummyTestComparison comparison, DummyTextEnv env)
    {
        if (comparison.Dps is not { } dps)
        {
            return "";
        }

        string change = Signed(dps.Delta, env.Culture) + " DPS" + (dps.DeltaFraction is double f ? " (" + Percent(f, env.Culture) + ")" : "");
        string basis = dps.Method == DummyNoiseMethod.RunToRun
            ? env.Format("DummyTest.Dps.BasisRuns", comparison.RunsA, comparison.RunsB)
            : env.Loc("DummyTest.Dps.BasisHits");
        return env.Format(dps.WithinNoise ? "DummyTest.Dps.Within" : "DummyTest.Dps.Beyond", change, Number(dps.Margin, env.Culture), basis);
    }

    /// <summary>"12,345 → 12,900 (+4.5 %)" for the headline.</summary>
    public static string DpsHeadline(DummyDpsComparison dps, DummyTextEnv env, string arrow = Arrow) =>
        Number(dps.DpsA, env.Culture) + " " + arrow + " " + Number(dps.DpsB, env.Culture)
        + (dps.DeltaFraction is double f ? " (" + Percent(f, env.Culture) + ")" : "");

    /// <summary>One warning of the comparison, in the UI language. The kind picks the sentence, the ids, names and the row of the skill fill it in.</summary>
    public static string WarningText(DummyTestWarning warning, DummyTestComparison comparison, DummyTextEnv env)
    {
        string ids = warning.Ids is { Count: > 0 } list ? string.Join(", ", list.Select(i => i.ToString(CultureInfo.InvariantCulture))) : "";
        DummyTestRow? row = warning.Skill is null ? null : comparison.Rows.FirstOrDefault(r => r.Skill == warning.Skill);
        string skill = env.Skill(warning.Skill);
        switch (warning.Kind)
        {
            case DummyTestWarningKind.PlannedLengthsDiffer:
                return env.Format("DummyTest.Warn.Lengths",
                    string.Join(", ", (warning.Ids ?? Array.Empty<int>()).Select(s => Duration(TimeSpan.FromSeconds(s)))));
            case DummyTestWarningKind.NoCompleteRuns:
                return env.Loc("DummyTest.Warn.NoComplete");
            case DummyTestWarningKind.IncompleteRunsExcluded:
                return env.Loc("DummyTest.Warn.Incomplete");
            case DummyTestWarningKind.TargetDiffers:
                return env.Format("DummyTest.Warn.Target", string.Join(", ", warning.Names ?? Array.Empty<string>()));
            case DummyTestWarningKind.StatsDiffer:
                return env.Format("DummyTest.Warn.Stats", ids);
            case DummyTestWarningKind.EffectsDiffer:
                return env.Format("DummyTest.Warn.Effects", ids);
            case DummyTestWarningKind.LevelsNotConfirmed:
                return env.Loc("DummyTest.Warn.Unconfirmed");
            case DummyTestWarningKind.LevelsInconsistent:
                return env.Format("DummyTest.Warn.Inconsistent",
                    string.Join(", ", (warning.Ids ?? Array.Empty<int>()).Select(id => SkillOf(comparison, id, env))));
            case DummyTestWarningKind.FewPlainHits:
                return env.Format("DummyTest.Warn.FewHits", skill, row?.A?.PlainHits ?? 0, row?.B?.PlainHits ?? 0, DummyAnalysis.MinPlainHits);
            case DummyTestWarningKind.CritRateDiffers:
                return env.Format("DummyTest.Warn.Crit", skill,
                    ((row?.A?.CritRate ?? 0) * 100).ToString("0.0", env.Culture), ((row?.B?.CritRate ?? 0) * 100).ToString("0.0", env.Culture));
            default:
                return warning.Message;
        }
    }

    /// <summary>The warnings of the results page, shortened (Stacy, 2026-10-23: far too many lines): the per-skill ones (too few plain hits, a crit rate that
    /// differs) become one line each when several skills share them, and the stat and effect numbers, which mean nothing to the player, are left out.</summary>
    public static List<string> WarningTexts(IEnumerable<DummyTestWarning> warnings, DummyTestComparison comparison, DummyTextEnv env)
    {
        var texts = new List<string>();
        var few = new List<DummyTestWarning>();
        var crit = new List<DummyTestWarning>();
        foreach (DummyTestWarning w in warnings)
        {
            switch (w.Kind)
            {
                case DummyTestWarningKind.FewPlainHits:
                    few.Add(w);
                    break;
                case DummyTestWarningKind.CritRateDiffers:
                    crit.Add(w);
                    break;
                case DummyTestWarningKind.StatsDiffer:
                    texts.Add(env.Loc("DummyTest.Warn.Stats.Short"));
                    break;
                case DummyTestWarningKind.EffectsDiffer:
                    texts.Add(env.Loc("DummyTest.Warn.Effects.Short"));
                    break;
                default:
                    texts.Add(WarningText(w, comparison, env));
                    break;
            }
        }

        if (few.Count == 1)
        {
            texts.Add(WarningText(few[0], comparison, env));
        }
        else if (few.Count > 1)
        {
            texts.Add(env.Format("DummyTest.Warn.FewHits.Many", few.Count, DummyAnalysis.MinPlainHits));
        }

        if (crit.Count == 1)
        {
            texts.Add(WarningText(crit[0], comparison, env));
        }
        else if (crit.Count > 1)
        {
            texts.Add(env.Format("DummyTest.Warn.Crit.Many", crit.Count, string.Join(", ", crit.Select(w => env.Skill(w.Skill)).Take(3)) + (crit.Count > 3 ? "..." : "")));
        }

        return texts;
    }

    /// <summary>The note under one skill of the improvement ranking: how many levels were tested and how far to trust the slope.</summary>
    public static string CurveNote(DummyLevelCurve curve, DummyTextEnv env)
    {
        string note = curve.Tier switch
        {
            DummyCurveTier.NotMeasurable => env.Format("DummyTest.Curve.Only", curve.Points.Count > 0 ? curve.Points[0].Level : 0),
            DummyCurveTier.TwoPoints => env.Loc("DummyTest.Curve.TwoPoints"),
            _ => env.Format("DummyTest.Curve.Measured", curve.DistinctLevels),
        };
        if (curve.CritMultiplierAssumed && curve.Tier != DummyCurveTier.NotMeasurable)
        {
            note += " " + env.Format("DummyTest.Curve.CritAssumed", curve.CritMultiplier.ToString("0.00", env.Culture));
        }

        return note;
    }

    public static string TierText(DummyCurveTier tier, DummyTextEnv env) => env.Loc(tier switch
    {
        DummyCurveTier.Measured => "DummyTest.Tier.Measured",
        DummyCurveTier.TwoPoints => "DummyTest.Tier.TwoPoints",
        _ => "DummyTest.Tier.NotMeasurable",
    });

    /// <summary>The estimated gain of one more level ("+1,240 DPS per level"), or the advice to test another level.</summary>
    public static string CurveGain(DummyLevelCurve curve, DummyTextEnv env) =>
        curve.DpsGainPerLevel is double gain ? env.Format("DummyTest.Curve.Gain", Signed(gain, env.Culture)) : env.Loc("DummyTest.Curve.TestAnother");

    /// <summary>The slope as a share of the plain hit ("+3.1 % per level on a plain hit"); empty without a slope.</summary>
    public static string CurveRelative(DummyLevelCurve curve, DummyTextEnv env) =>
        curve.RelativeSlope is double relative ? env.Format("DummyTest.Curve.Relative", Percent(relative, env.Culture)) : "";

    /// <summary>"18, 20": the levels that were tested.</summary>
    public static string CurveLevels(DummyLevelCurve curve) =>
        string.Join(", ", curve.Points.Select(p => p.Level).Distinct().OrderBy(l => l).Select(l => l.ToString(CultureInfo.InvariantCulture)));

    // ---------------------------------------------------------------- the setup checklist

    public static string StateText(DummyState state, bool complete, DummyTextEnv env) => env.Loc(state switch
    {
        DummyState.Armed => "DummyTest.State.Armed",
        DummyState.Running => "DummyTest.State.Running",
        DummyState.Finished => complete ? "DummyTest.State.Finished" : "DummyTest.State.Stopped",
        _ => "DummyTest.State.Idle",
    });

    /// <summary>The target line. <paramref name="targetName"/> is empty until the run's first hits tell it.</summary>
    public static DummyStatusLine TargetLine(string targetName, bool isDummy, DummyTextEnv env) =>
        targetName.Length == 0 ? new(DummyStatus.Pending, env.Loc("DummyTest.Target.Unknown"))
        : isDummy ? new(DummyStatus.Ok, env.Format("DummyTest.Target.Dummy", targetName))
        : new(DummyStatus.Warn, env.Format("DummyTest.Target.Other", targetName));

    /// <summary>The skills line: what the last skill list was and whether the player confirmed it.</summary>
    public static DummyStatusLine SkillsLine(SkillsReadInfo info, DummyTextEnv env)
    {
        if (info.At is null)
        {
            return new(DummyStatus.Warn, env.Loc("DummyTest.Skills.None"));
        }

        string ago = info.Age is TimeSpan age ? env.Format("DummyTest.Ago", Age(age)) : "";
        if (info.Confirmed)
        {
            return new(DummyStatus.Ok, env.Format("DummyTest.Skills.Confirmed", info.Count, ago));
        }

        return info.PlayerConfirmed && !info.FrameAfterRequest
            ? new(DummyStatus.Warn, env.Format("DummyTest.Skills.NoFrame", info.Count, ago))
            : new(DummyStatus.Warn, env.Format("DummyTest.Skills.Unconfirmed", info.Count, ago));
    }

    /// <summary>The instruction under the skills line, by where the two-step confirmation stands.</summary>
    public static string SkillsHint(SkillsReadInfo info, DummyTextEnv env) => env.Loc(
        info.Confirmed ? "DummyTest.Skills.Hint.Done"
        : !info.Requested ? "DummyTest.Skills.Hint.Prepare"
        : info.PlayerConfirmed ? "DummyTest.Skills.Hint.Waiting"
        : "DummyTest.Skills.Hint.Open");

    public static DummyStatusLine StatsLine(bool present, DummyTextEnv env) =>
        present ? new(DummyStatus.Ok, env.Loc("DummyTest.Stats.Read")) : new(DummyStatus.Warn, env.Loc("DummyTest.Stats.None"));

    public static DummyStatusLine EffectsLine(int count, DummyTextEnv env) =>
        count > 0 ? new(DummyStatus.Ok, env.Format("DummyTest.Effects.Some", count)) : new(DummyStatus.Pending, env.Loc("DummyTest.Effects.None"));

    // ---------------------------------------------------------------- exports

    /// <summary>
    /// The per-skill comparison as a Discord message: a header (who against whom, the DPS and the level conclusion), then the table in a code fence
    /// (aligned columns, ASCII arrows). Empty for a comparison that was refused or has no skill. <paramref name="sideA"/> and <paramref name="sideB"/>
    /// describe the two configurations ("Smash 20, 2 runs").
    /// </summary>
    public static string DiscordTable(DummyTestComparison comparison, DummyTextEnv env, string sideA, string sideB)
    {
        if (!comparison.Comparable || comparison.Rows.Count == 0)
        {
            return "";
        }

        const string Ascii = "->";
        CultureInfo c = env.Culture;
        var rows = new List<IReadOnlyList<string>>();
        foreach (DummyTestRow r in comparison.Rows.Take(MaxTableRows))
        {
            rows.Add(new[]
            {
                env.Skill(r.Skill),
                Levels(r.LevelA, r.LevelB, Ascii),
                UsesPerMinute(r, c, Ascii),
                PlainMean(r.PlainMeanA, c),
                PlainMean(r.PlainMeanB, c),
                Change(r, c),
                CritRate(r, c, Ascii),
                env.Loc(VerdictKey(r.Verdict)),
            });
        }

        string table = AsciiTable.Render(
            new[]
            {
                env.Loc("DummyTest.Col.Skill"), env.Loc("DummyTest.Col.Level"), env.Loc("DummyTest.Col.Uses"),
                env.Loc("DummyTest.Col.PlainA"), env.Loc("DummyTest.Col.PlainB"), env.Loc("DummyTest.Col.Change"),
                env.Loc("DummyTest.Col.Crit"), env.Loc("DummyTest.Col.Verdict"),
            },
            rows,
            new[] { false, false, true, true, true, true, true, false });

        var text = new StringBuilder();
        text.AppendLine(env.Format("DummyTest.Export.Header", sideA, sideB, Duration(TimeSpan.FromSeconds(comparison.PlannedSeconds))));
        if (comparison.Dps is { } dps)
        {
            text.AppendLine(env.Format("DummyTest.Export.Dps", DpsHeadline(dps, env, Ascii), env.Loc(dps.WithinNoise ? "DummyTest.Dps.WithinShort" : "DummyTest.Dps.BeyondShort")));
        }

        if (Conclusion(comparison, env) is { Length: > 0 } conclusion)
        {
            text.AppendLine(conclusion);
        }

        text.Append(table);
        return text.ToString();
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    /// <summary>The per-skill rows as a CSV: invariant numbers (a dot), English column names and verdict words so a spreadsheet or a script reads it the same in any language.
    /// Empty for a comparison that was refused.</summary>
    public static string Csv(DummyTestComparison comparison)
    {
        if (!comparison.Comparable)
        {
            return "";
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        static string N(double? value, string format, CultureInfo c) => value is double v ? v.ToString(format, c) : "";
        var csv = new StringBuilder();
        csv.Append("skill_id,skill,level_a,level_b,uses_per_min_a,uses_per_min_b,plain_hits_a,plain_hits_b,plain_mean_a,plain_mean_b,")
           .Append("change_pct,change_low_pct,change_high_pct,p_value,crit_rate_a,crit_rate_b,total_damage_a,total_damage_b,dps_a,dps_b,verdict\r\n");
        foreach (DummyTestRow r in comparison.Rows)
        {
            string[] cells =
            {
                r.BaseId.ToString(inv),
                Quote(r.Skill),
                r.LevelA?.ToString(inv) ?? "",
                r.LevelB?.ToString(inv) ?? "",
                N(r.A?.UsesPerMinute, "0.00", inv),
                N(r.B?.UsesPerMinute, "0.00", inv),
                (r.A?.PlainHits ?? 0).ToString(inv),
                (r.B?.PlainHits ?? 0).ToString(inv),
                N(r.PlainMeanA, "0.0", inv),
                N(r.PlainMeanB, "0.0", inv),
                N(r.PlainChange * 100, "0.00", inv),
                N(r.PlainChangeLow * 100, "0.00", inv),
                N(r.PlainChangeHigh * 100, "0.00", inv),
                N(r.PValue, "0.0000", inv),
                N(r.A is null ? null : r.A.CritRate * 100, "0.0", inv),
                N(r.B is null ? null : r.B.CritRate * 100, "0.0", inv),
                (r.A?.TotalDamage ?? 0).ToString(inv),
                (r.B?.TotalDamage ?? 0).ToString(inv),
                N(r.A?.Dps, "0.0", inv),
                N(r.B?.Dps, "0.0", inv),
                VerdictToken(r.Verdict),
            };
            csv.Append(string.Join(",", cells)).Append("\r\n");
        }

        return csv.ToString();
    }
}
