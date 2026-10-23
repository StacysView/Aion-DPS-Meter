using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The dummy test window's pure part (Ui/DummyTestText: formats, the setup checklist, the sentences of a comparison, the Discord table and the CSV)
/// on synthetic runs, with the real strings of assets/i18n/ui_strings.json in English and in French: so a key that is missing, a placeholder that does not
/// match what the code passes (a FormatException), an em dash, or an English and a French sentence that disagree on their placeholders all fail here.</summary>
public static class SelfCheckDummyUi
{
    private const int CleaveId = 11010010;
    private const int BoltId = 12010010;
    private const int SmashId = 13010010;
    private static readonly DateTime Origin = new(2026, 10, 23, 20, 0, 0);

    public static bool Run()
    {
        Console.WriteLine("[selftest] Dummy test window text (formats, checklist, sentences, Discord table, CSV, strings):");
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "i18n", "ui_strings.json");
        if (!File.Exists(path))
        {
            return Report("ui_strings.json found", false);
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        bool ok = RunFormats();
        ok &= RunStrings(doc.RootElement);
        foreach (string language in new[] { "en", "fr" })
        {
            var used = new HashSet<string>();
            DummyTextEnv env = new(key => Lookup(doc.RootElement, key, language, used).Replace("[A]", "A").Replace("[B]", "B"), CultureInfo.InvariantCulture, name => name);
            ok &= RunSentences(env, language == "en", language);
            ok &= RunStatus(env, language);
            ok &= RunExports(env, language);
            ok &= Report($"every string the text code asked for exists in {language} ({used.Count} keys)", used.All(key => Has(doc.RootElement, key, language)));
        }

        return ok;
    }

    private static bool Report(string what, bool ok)
    {
        Console.WriteLine($"  -> {what}: {ok}");
        return ok;
    }

    private static bool Has(JsonElement root, string key, string language) =>
        root.TryGetProperty(key, out JsonElement entry) && entry.TryGetProperty(language, out JsonElement text) && !string.IsNullOrWhiteSpace(text.GetString());

    private static string Lookup(JsonElement root, string key, string language, HashSet<string> used)
    {
        used.Add(key);
        return Has(root, key, language) ? root.GetProperty(key).GetProperty(language).GetString()! : "<" + key + ">";
    }

    // ---------------------------------------------------------------- synthetic runs

    private static void Add(DummyRun run, int id, string name, int hits, double mean)
    {
        for (int i = 0; i < hits; i++)
        {
            DateTime t = run.Start + run.Planned * ((i + 0.5) / hits);
            long amount = (long)Math.Round(mean + (i % 7) * 10);
            run.Events.Add(new DamageEvent(t, 1, 100, amount, false, name, false, false, id, HitKind.None));
            run.Uses.Add(new SkillUse(t.AddMilliseconds(-100), id, true));
        }
    }

    /// <summary>A complete run: Cleave (60 hits) at <paramref name="cleaveLevel"/> hitting <paramref name="bonus"/> harder, Bolt (6 hits, too few to compare) and a skill with a comma and quotes in its name.</summary>
    private static DummyRun MakeRun(int number, int cleaveLevel, int boltLevel, double bonus, bool confirmed = true, TimeSpan? planned = null, string target = "Training Scarecrow")
    {
        TimeSpan length = planned ?? TimeSpan.FromMinutes(1);
        var run = new DummyRun
        {
            Number = number, Planned = length, Length = length, Start = Origin.AddMinutes(10 * number), PlayerId = 1, TargetName = target, LevelsConfirmed = confirmed,
            Levels = new List<SkillLevel> { new(CleaveId, cleaveLevel, cleaveLevel), new(BoltId, boltLevel, boltLevel), new(SmashId, 5, 5) },
        };
        Add(run, CleaveId, "Cleave", 60, 1000 + bonus);
        Add(run, BoltId, "Bolt", 6, 800);
        Add(run, SmashId, "Smash, \"Big\"", 12, 500);
        return run;
    }

    // ---------------------------------------------------------------- formats

    private static bool RunFormats()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        bool clock = DummyTestText.Clock(TimeSpan.FromMilliseconds(67_400), inv) == "1:07.4"
            && DummyTestText.Clock(TimeSpan.Zero, inv) == "0:00.0"
            && DummyTestText.Clock(TimeSpan.FromSeconds(-3), inv) == "0:00.0"
            && DummyTestText.Clock(TimeSpan.FromMilliseconds(59_950), inv) == "1:00.0"
            && DummyTestText.Clock(TimeSpan.FromMinutes(5), inv) == "5:00.0";
        bool lengths = DummyTestText.Duration(TimeSpan.FromSeconds(30)) == "30 s" && DummyTestText.Duration(TimeSpan.FromMinutes(1)) == "1 min"
            && DummyTestText.Duration(TimeSpan.FromSeconds(150)) == "2 min 30 s" && DummyTestText.Duration(TimeSpan.FromMinutes(5)) == "5 min"
            && DummyTestText.PresetSeconds.SequenceEqual(new[] { 30, 60, 120, 180, 300 });
        bool ages = DummyTestText.Age(TimeSpan.FromSeconds(12)) == "12 s" && DummyTestText.Age(TimeSpan.FromSeconds(200)) == "3 min" && DummyTestText.Age(TimeSpan.FromHours(2)) == "2 h";
        bool numbers = DummyTestText.Percent(0.062, inv) == "+6.2 %" && DummyTestText.Percent(-0.0123, inv) == "-1.2 %" && DummyTestText.Percent(0, inv) == "0.0 %"
            && DummyTestText.Signed(1234.4, inv) == "+1,234" && DummyTestText.Signed(-5, inv) == "-5" && DummyTestText.Number(12345.6, inv) == "12,346"
            && DummyTestText.Interval(-0.012, 0.045, inv) == "[-1.2 % ; +4.5 %]" && DummyTestText.Interval(null, 0.1, inv) == "";
        bool levels = DummyTestText.Levels(18, 20) == "18 → 20" && DummyTestText.Levels(18, 18) == "18" && DummyTestText.Levels(null, null) == "-"
            && DummyTestText.Levels(18, 20, "->") == "18 -> 20" && DummyTestText.Levels(null, 20) == "? → 20";
        DateTime now = Origin;
        var complete = new DummyRunRow(1, 1, "x", "", now, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), true, "Dummy", 60_000, true, now);
        var stopped = complete with { Length = TimeSpan.FromSeconds(30), Complete = false };
        bool dps = Math.Abs(DummyTestText.RunDps(complete) - 1000) < 1e-9 && Math.Abs(DummyTestText.RunDps(stopped) - 2000) < 1e-9;
        bool verdicts = Enum.GetValues<DummyTestVerdict>().Select(v => DummyTestText.VerdictToken(v)).Distinct().Count() == 4
            && Enum.GetValues<DummyTestVerdict>().Select(DummyTestText.VerdictKey).Distinct().Count() == 4;
        return Report("clock, durations, ages, percentages, levels, run DPS, verdict words", clock && lengths && ages && numbers && levels && dps && verdicts);
    }

    // ---------------------------------------------------------------- the strings file

    private static bool RunStrings(JsonElement root)
    {
        var placeholder = new Regex(@"\{\d+(?:[:,][^}]*)?\}");
        int keys = 0;
        var bad = new List<string>();
        foreach (JsonProperty entry in root.EnumerateObject().Where(e => e.Name.StartsWith("DummyTest.", StringComparison.Ordinal) || e.Name == "Main.MenuApp.DummyTest"))
        {
            keys++;
            string? en = entry.Value.TryGetProperty("en", out JsonElement enText) ? enText.GetString() : null;
            string? fr = entry.Value.TryGetProperty("fr", out JsonElement frText) ? frText.GetString() : null;
            if (string.IsNullOrWhiteSpace(en) || string.IsNullOrWhiteSpace(fr))
            {
                bad.Add(entry.Name + " (missing en or fr)");
                continue;
            }

            // No em dash (or en dash) in anything the player reads; the same placeholders in both languages; no stray brace.
            if (en.Contains('\u2014') || fr.Contains('\u2014') || en.Contains('\u2013') || fr.Contains('\u2013'))
            {
                bad.Add(entry.Name + " (dash)");
            }

            if (!placeholder.Matches(en).Select(m => m.Value).OrderBy(s => s).SequenceEqual(placeholder.Matches(fr).Select(m => m.Value).OrderBy(s => s)))
            {
                bad.Add(entry.Name + " (placeholders differ)");
            }

            if (placeholder.Replace(en, "").IndexOfAny(new[] { '{', '}' }) >= 0 || placeholder.Replace(fr, "").IndexOfAny(new[] { '{', '}' }) >= 0)
            {
                bad.Add(entry.Name + " (stray brace)");
            }
        }

        if (bad.Count > 0)
        {
            Console.WriteLine("     " + string.Join("; ", bad));
        }

        return Report($"{keys} DummyTest strings: en and fr present, same placeholders, no dash", keys >= 100 && bad.Count == 0);
    }

    // ---------------------------------------------------------------- sentences of a comparison

    private static bool RunSentences(DummyTextEnv env, bool english, string language)
    {
        DummyRun a = MakeRun(1, 18, 10, 0), b = MakeRun(2, 20, 10, 60);
        DummyTestComparison single = DummyAnalysis.Compare(new[] { a }, new[] { b });
        string conclusion = DummyTestText.Conclusion(single, env);
        bool singleOk = single.Comparable && single.Levels?.Kind == DummyLevelConclusionKind.SingleChange
            && conclusion.Contains("Cleave") && conclusion.Contains("18 → 20") && conclusion.Contains("+5.");
        if (english)
        {
            singleOk &= conclusion.StartsWith("Only one skill changed: Cleave (level 18 → 20). Its plain hit: +5.", StringComparison.Ordinal) && conclusion.EndsWith("A measurable gain.", StringComparison.Ordinal);
        }

        string dps = DummyTestText.DpsStatement(single, env);
        bool dpsOk = single.Dps is { } d && dps.StartsWith("DPS ", StringComparison.Ordinal) && dps.Contains("+")
            && (d.WithinNoise ? dps.Contains(english ? "within the noise" : "dans le bruit") : dps.Contains(english ? "beyond the noise" : "au-delà du bruit"))
            && dps.Contains(english ? "the spread of the hits inside the runs" : "la dispersion des coups");

        // Warnings come out in the language by kind, with the skill and its counts filled in.
        DummyTestWarning few = single.Warnings.First(w => w.Kind == DummyTestWarningKind.FewPlainHits);
        string fewText = DummyTestText.WarningText(few, single, env);
        bool fewOk = fewText.Contains("Bolt") && fewText.Contains('6') && fewText.Contains("10");
        if (english)
        {
            fewOk &= fewText == "Bolt: too few usable hits to compare (6 in A, 6 in B, at least 10 are needed on each side).";
        }

        var rows = single.Rows;
        bool rowsOk = rows.Count == 3 && rows[0].Skill == "Cleave" && rows[0].Verdict == DummyTestVerdict.Better
            && rows.First(r => r.Skill == "Bolt").Verdict == DummyTestVerdict.NotEnoughData
            && rows.First(r => r.Skill.StartsWith("Smash")).Verdict == DummyTestVerdict.NoMeasurableDifference;

        var all = new DummyTestComparison(true, null, 1, 1, 60, rows, single.Dps, single.Levels, new[]
        {
            new DummyTestWarning(DummyTestWarningKind.PlannedLengthsDiffer, "", Ids: new[] { 30, 60 }),
            new DummyTestWarning(DummyTestWarningKind.NoCompleteRuns, ""),
            new DummyTestWarning(DummyTestWarningKind.IncompleteRunsExcluded, ""),
            new DummyTestWarning(DummyTestWarningKind.TargetDiffers, "", Names: new[] { "Scarecrow", "Dummy" }),
            new DummyTestWarning(DummyTestWarningKind.StatsDiffer, "", Ids: new[] { 5, 9 }),
            new DummyTestWarning(DummyTestWarningKind.EffectsDiffer, "", Ids: new[] { 7001 }),
            new DummyTestWarning(DummyTestWarningKind.LevelsNotConfirmed, ""),
            new DummyTestWarning(DummyTestWarningKind.LevelsInconsistent, "", Ids: new[] { CleaveId / 10000 * 10000 }),
            new DummyTestWarning(DummyTestWarningKind.CritRateDiffers, "", "Cleave"),
        });
        List<string> texts = all.Warnings.Select(w => DummyTestText.WarningText(w, all, env)).ToList();
        bool warnOk = texts.All(t => t.Length > 0 && !t.Contains('<') && !t.Contains('{')) && texts.Distinct().Count() == texts.Count
            && texts[0].Contains("30 s, 1 min") && texts[3].Contains("Scarecrow, Dummy") && texts[4].Contains("5, 9") && texts[5].Contains("7001")
            && texts[7].Contains("Cleave") && texts[8].Contains("Cleave");

        // The results page shortens them: many skills with too few plain hits make one line, and the stat and effect numbers are left out.
        var manyFew = new[]
        {
            new DummyTestWarning(DummyTestWarningKind.FewPlainHits, "", "Cleave"), new DummyTestWarning(DummyTestWarningKind.FewPlainHits, "", "Bolt"),
            new DummyTestWarning(DummyTestWarningKind.FewPlainHits, "", "Smash"),
            new DummyTestWarning(DummyTestWarningKind.StatsDiffer, "", Ids: new[] { 5, 9 }), new DummyTestWarning(DummyTestWarningKind.EffectsDiffer, "", Ids: new[] { 7001 }),
        };
        List<string> shortTexts = DummyTestText.WarningTexts(manyFew, all, env);
        bool shortOk = shortTexts.Count == 3 && shortTexts.All(t => !t.Contains('{') && !t.Contains("5, 9") && !t.Contains("7001")) && shortTexts.Any(t => t.Contains('3'));
        warnOk &= shortOk;

        // Mixed levels name every skill that changed; unconfirmed levels say so.
        DummyTestComparison mixed = DummyAnalysis.Compare(new[] { a }, new[] { MakeRun(3, 20, 11, 60) });
        string mixedText = DummyTestText.Conclusion(mixed, env);
        bool mixedOk = mixed.Levels?.Kind == DummyLevelConclusionKind.Mixed && mixedText.Contains("Cleave") && mixedText.Contains("Bolt") && mixedText.Contains('2');
        DummyTestComparison unknown = DummyAnalysis.Compare(new[] { a }, new[] { MakeRun(4, 20, 10, 60, confirmed: false) });
        bool unknownOk = unknown.Levels?.Kind == DummyLevelConclusionKind.LevelsUnknown && DummyTestText.Conclusion(unknown, env) == env.Loc("DummyTest.Level.Unknown");
        DummyTestComparison same = DummyAnalysis.Compare(new[] { a }, new[] { MakeRun(5, 18, 10, 0) });
        bool noChangeOk = same.Levels?.Kind == DummyLevelConclusionKind.NoLevelChange && DummyTestText.Conclusion(same, env) == env.Loc("DummyTest.Level.NoChange");

        // A refused comparison has no conclusion, no table and no CSV, but says why.
        DummyTestComparison refused = DummyAnalysis.Compare(new[] { a }, new[] { MakeRun(6, 20, 10, 0, planned: TimeSpan.FromSeconds(30)) });
        bool refusedOk = !refused.Comparable && DummyTestText.Conclusion(refused, env) == "" && DummyTestText.DiscordTable(refused, env, "A", "B") == ""
            && DummyTestText.Csv(refused) == ""
            && DummyTestText.WarningText(refused.Warnings.First(w => w.Kind == DummyTestWarningKind.PlannedLengthsDiffer), refused, env).Contains("30 s");

        // The ranking: Cleave tested at two levels, Bolt at one.
        IReadOnlyList<DummyLevelCurve> curves = DummyAnalysis.LevelCurves(new[] { a, b });
        DummyLevelCurve cleave = curves.First(c => c.Skill == "Cleave"), bolt = curves.First(c => c.Skill == "Bolt");
        bool curveOk = cleave.Tier == DummyCurveTier.TwoPoints && bolt.Tier == DummyCurveTier.NotMeasurable
            && DummyTestText.CurveLevels(cleave) == "18, 20" && DummyTestText.CurveGain(bolt, env) == env.Loc("DummyTest.Curve.TestAnother")
            && DummyTestText.CurveGain(cleave, env).Contains("DPS") && DummyTestText.CurveGain(cleave, env).Contains('+')
            && DummyTestText.CurveNote(bolt, env).Contains("10") && DummyTestText.CurveNote(cleave, env).Contains("x1.85")
            && DummyTestText.CurveRelative(cleave, env).Contains('%') && DummyTestText.CurveRelative(bolt, env) == ""
            && new[] { DummyCurveTier.Measured, DummyCurveTier.TwoPoints, DummyCurveTier.NotMeasurable }.Select(t => DummyTestText.TierText(t, env)).Distinct().Count() == 3;

        return Report($"{language}: level conclusion, DPS noise statement, every warning kind, mixed / unknown / no change / refused, ranking notes",
            singleOk && dpsOk && fewOk && rowsOk && warnOk && mixedOk && unknownOk && noChangeOk && refusedOk && curveOk);
    }

    // ---------------------------------------------------------------- the setup checklist

    private static bool RunStatus(DummyTextEnv env, string language)
    {
        DateTime at = Origin;
        TimeSpan age = TimeSpan.FromSeconds(12);
        var none = new SkillsReadInfo(null, 0, 0, null, false, false, false);
        var unconfirmed = new SkillsReadInfo(at, 143, 0x51, age, false, false, false);
        var requested = unconfirmed with { Requested = true };
        var noFrame = requested with { PlayerConfirmed = true };
        var confirmed = noFrame with { FrameAfterRequest = true };

        DummyStatusLine l1 = DummyTestText.SkillsLine(none, env), l2 = DummyTestText.SkillsLine(unconfirmed, env), l3 = DummyTestText.SkillsLine(noFrame, env), l4 = DummyTestText.SkillsLine(confirmed, env);
        bool skills = l1.Status == DummyStatus.Warn && l2.Status == DummyStatus.Warn && l3.Status == DummyStatus.Warn && l4.Status == DummyStatus.Ok
            && l4.Text.Contains("143") && l4.Text.Contains("12 s") && new[] { l1.Text, l2.Text, l3.Text, l4.Text }.Distinct().Count() == 4;
        bool hints = new[] { none, unconfirmed, requested, noFrame, confirmed }.Select(i => DummyTestText.SkillsHint(i, env)).Distinct().Count() == 4
            && DummyTestText.SkillsHint(requested, env) == env.Loc("DummyTest.Skills.Hint.Open")
            && DummyTestText.SkillsHint(noFrame, env) == env.Loc("DummyTest.Skills.Hint.Waiting")
            && DummyTestText.SkillsHint(confirmed, env) == env.Loc("DummyTest.Skills.Hint.Done")
            && DummyTestText.SkillsHint(unconfirmed, env) == env.Loc("DummyTest.Skills.Hint.Prepare");
        bool target = DummyTestText.TargetLine("", false, env).Status == DummyStatus.Pending
            && DummyTestText.TargetLine("Training Scarecrow", true, env) is { Status: DummyStatus.Ok } dummy && dummy.Text.Contains("Training Scarecrow")
            && DummyTestText.TargetLine("Boar", false, env) is { Status: DummyStatus.Warn } other && other.Text.Contains("Boar");
        bool stats = DummyTestText.StatsLine(DummyTestText.FullStatTable + 30, env).Status == DummyStatus.Ok && DummyTestText.StatsLine(12, env) is { Status: DummyStatus.Warn } partial
            && partial.Text.Contains("12") && DummyTestText.StatsLine(true, env).Status == DummyStatus.Ok && DummyTestText.StatsLine(false, env).Status == DummyStatus.Warn
            && DummyTestText.EffectsLine(3, env).Text.Contains('3') && DummyTestText.EffectsLine(0, env).Status == DummyStatus.Pending;
        bool states = new[] { DummyState.Idle, DummyState.Armed, DummyState.Running, DummyState.Finished }.Select(s => DummyTestText.StateText(s, true, env)).Distinct().Count() == 4
            && DummyTestText.StateText(DummyState.Finished, false, env) != DummyTestText.StateText(DummyState.Finished, true, env);
        bool counts = DummyTestText.Runs(1, env) != DummyTestText.Runs(2, env) && DummyTestText.Runs(2, env).Contains('2');
        return Report($"{language}: checklist lines (target, skills read in its five steps, stats, effects), states, run counts", skills && hints && target && stats && states && counts);
    }

    // ---------------------------------------------------------------- exports

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }

    private static bool RunExports(DummyTextEnv env, string language)
    {
        DummyTestComparison cmp = DummyAnalysis.Compare(new[] { MakeRun(1, 18, 10, 0) }, new[] { MakeRun(2, 20, 10, 60) });

        // The Discord message: a header, the DPS line, the conclusion, then one fenced table with a row per skill and ASCII arrows.
        string table = DummyTestText.DiscordTable(cmp, env, "Cleave 18 (1 run)", "Cleave 20 (1 run)");
        List<string> lines = table.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        int fences = lines.Count(l => l.StartsWith("```", StringComparison.Ordinal));
        int open = lines.FindIndex(l => l.StartsWith("```", StringComparison.Ordinal));
        List<string> body = lines.Skip(open + 1).TakeWhile(l => !l.StartsWith("```", StringComparison.Ordinal)).ToList();
        bool tableOk = table.Length > 0 && fences == 2 && open >= 2 && body.Count == 2 + cmp.Rows.Count
            && body[1].All(ch => ch == '-' || ch == ' ') && body.Any(l => l.Contains("18 -> 20")) && !string.Join("\n", body).Contains('→')
            && table.Contains("Cleave 18 (1 run)") && table.Contains("Cleave 20 (1 run)") && table.Contains("1 min")
            && body.Skip(2).Any(l => l.StartsWith("Cleave", StringComparison.Ordinal) && l.Contains(env.Loc("DummyTest.Verdict.Better")));
        if (language == "en")
        {
            tableOk &= lines[0] == "**Dummy test** A: Cleave 18 (1 run) against B: Cleave 20 (1 run) (1 min)" && lines[1].StartsWith("DPS from A to B: ", StringComparison.Ordinal);
        }

        // The CSV: one line per skill, 21 columns each, a dot for decimals, a field with a comma and quotes quoted, a machine word for the verdict.
        string csv = DummyTestText.Csv(cmp);
        List<string> rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        List<string> head = SplitCsv(rows[0]);
        bool csvOk = rows.Count == 1 + cmp.Rows.Count && head.Count == 21 && head[0] == "skill_id" && head[^1] == "verdict"
            && rows.Skip(1).All(r => SplitCsv(r).Count == 21) && !csv.Contains(';')
            && rows.Any(r => r.Contains("\"Smash, \"\"Big\"\"\"")) && SplitCsv(rows.First(r => r.Contains("Cleave")))[^1] == "better"
            && SplitCsv(rows.First(r => r.Contains("Cleave")))[1] == "Cleave" && SplitCsv(rows.First(r => r.Contains("Cleave")))[2] == "18" && SplitCsv(rows.First(r => r.Contains("Cleave")))[3] == "20"
            && SplitCsv(rows.First(r => r.Contains("Bolt")))[^1] == "not_enough_data" && SplitCsv(rows.First(r => r.Contains("Bolt")))[10] == ""
            && double.Parse(SplitCsv(rows.First(r => r.Contains("Cleave")))[10], CultureInfo.InvariantCulture) is > 4 and < 8;
        return Report($"{language}: Discord table (fence, header, aligned rows, ASCII arrows) and CSV (columns, quoting, invariant numbers, verdict words)", tableOk && csvOk);
    }
}
