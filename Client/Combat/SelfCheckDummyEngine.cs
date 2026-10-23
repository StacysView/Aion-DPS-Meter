using System.IO;
using AionDPS.Aion2;
using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The dummy test's engine, store and controller on synthetic data and a driven clock (no window, no capture): the clock waits for the first own
/// hit, starts on a use before the hit, the window is cut exactly at Planned (an event exactly at the end is kept, one millisecond later is dropped),
/// a late packet inside the grace is kept only when it is inside the window, Stop gives an incomplete run, runs of one length share a divisor, the store
/// keeps everything (levels, stats, effects, hit kinds) and prunes to 200, and two stored runs compare through the controller.</summary>
public static class SelfCheckDummyEngine
{
    private const int Me = 1;
    private const int Dummy = 100;
    private const int CleaveId = 11010010;
    private static readonly DateTime T0 = new(2026, 10, 23, 21, 0, 0);
    private static readonly IReadOnlyList<SkillUse> NoUses = Array.Empty<SkillUse>();
    private static readonly IReadOnlyList<EffectChange> NoEffects = Array.Empty<EffectChange>();
    private static readonly IReadOnlyList<DamageEvent> NoHits = Array.Empty<DamageEvent>();

    public static bool Run()
    {
        bool ok = true;
        ok &= RunArmedScenario();
        ok &= RunStartLeadScenario();
        ok &= RunWindowScenario();
        ok &= RunGraceScenario();
        ok &= RunStopScenario();
        ok &= RunDivisorScenario();
        ok &= RunStoreScenario();
        ok &= RunPruneScenario();
        ok &= RunControllerScenario();
        ok &= RunStatTableScenario();
        CleanTemp();
        return ok;
    }

    private static readonly List<string> TempFiles = new();

    /// <summary>The three store scenarios each open a database in %TEMP%; they are all closed by now, so the files go.</summary>
    private static void CleanTemp()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in TempFiles)
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file still held open is left for the OS's temp cleanup.
                }
            }
        }

        TempFiles.Clear();
    }

    private static DamageEvent Hit(DateTime at, long amount = 1000, HitKind kind = HitKind.None) =>
        new(at, Me, Dummy, amount, false, "Cleave", kind == HitKind.Crit, false, CleaveId, kind);

    private static void FeedHits(DummyTest test, DateTime now, params DamageEvent[] hits) => test.Feed(hits, NoUses, NoEffects, now, Me);

    private static (DummyTest Test, List<DummyRun> Finished) NewTest()
    {
        var test = new DummyTest();
        var finished = new List<DummyRun>();
        test.RunFinished += finished.Add;
        return (test, finished);
    }

    /// <summary>A whole run through the state machine: armed at <paramref name="armAt"/>, one feed of hits at the given seconds after it, then the clock runs on until the run is closed.</summary>
    private static DummyRun Record(TimeSpan planned, DateTime armAt, long amount, params double[] hitSeconds)
    {
        (DummyTest test, List<DummyRun> finished) = NewTest();
        test.Arm(planned, "x", "", armAt);
        FeedHits(test, armAt.AddSeconds(hitSeconds.Min()), hitSeconds.Select(s => Hit(armAt.AddSeconds(s), amount)).ToArray());
        test.Tick(armAt.AddSeconds(hitSeconds.Min()) + planned + TimeSpan.FromSeconds(10));
        return finished.Single();
    }

    private static bool RunArmedScenario()
    {
        Console.WriteLine("[selftest] Dummy engine: armed waits for the first own hit (a heal, a refused use, an effect ending or a hit before Arm do not start it):");
        (DummyTest test, _) = NewTest();
        test.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        FeedHits(test, T0.AddSeconds(1), Hit(T0.AddSeconds(-1)), Hit(T0.AddSeconds(1)) with { IsHeal = true });
        test.Feed(NoHits, new[] { new SkillUse(T0.AddSeconds(1), CleaveId, Accepted: false, RejectCode: 7) }, new[] { new EffectChange(T0.AddSeconds(1), 5000, On: false) }, T0.AddSeconds(1), Me);
        bool waiting = test.State == DummyState.Armed && test.Current is null && test.Remaining(T0.AddSeconds(5)) == TimeSpan.FromSeconds(30);
        FeedHits(test, T0.AddSeconds(3), Hit(T0.AddSeconds(2)));
        bool started = test.State == DummyState.Running && test.Current is { } run && run.Start == T0.AddSeconds(2) && run.Events.Count == 1 && run.PlayerId == Me
            && test.Remaining(T0.AddSeconds(12)) == TimeSpan.FromSeconds(20);
        bool refusedRearm = !test.Arm(TimeSpan.FromSeconds(60), "B", "", T0.AddSeconds(4));
        Console.WriteLine($"  -> still armed after those: {waiting}; running from the hit at +2 s with 20 s left at +12 s: {started}; no Arm while running: {refusedRearm}");
        return waiting && started && refusedRearm;
    }

    private static bool RunStartLeadScenario()
    {
        Console.WriteLine("[selftest] Dummy engine: the clock starts on a use before the first hit and moves back to a use within the lead, never earlier than Arm:");
        (DummyTest byUse, _) = NewTest();
        byUse.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        byUse.Feed(NoHits, new[] { new SkillUse(T0.AddSeconds(1), CleaveId, Accepted: true) }, NoEffects, T0.AddSeconds(1.2), Me);
        bool useStarts = byUse.State == DummyState.Running && byUse.Current!.Start == T0.AddSeconds(1);

        (DummyTest byEffect, _) = NewTest();
        byEffect.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        byEffect.Feed(NoHits, NoUses, new[] { new EffectChange(T0.AddSeconds(1), 5000, On: true) }, T0.AddSeconds(1.2), Me);
        // An effect put on is no action of the player (other players' buffs, food, auras renew by themselves): it never starts the clock.
        bool effectStarts = byEffect.State == DummyState.Armed;
        // Nor does a damage-over-time tick or a summon's blow (the spirit keeps striking the dummy by itself); a plain own hit does.
        (DummyTest byOthers, _) = NewTest();
        byOthers.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        byOthers.Feed(new[] { Hit(T0.AddSeconds(1)) with { IsTick = true }, Hit(T0.AddSeconds(1.5)) with { FromSummon = true } }, NoUses, NoEffects, T0.AddSeconds(2), Me);
        bool strayIgnored = byOthers.State == DummyState.Armed;
        byOthers.Feed(new[] { Hit(T0.AddSeconds(3)) }, NoUses, NoEffects, T0.AddSeconds(3.2), Me);
        bool ownHitStarts = byOthers.State == DummyState.Running && byOthers.Current!.Start == T0.AddSeconds(3);

        // The first hit at +5 (lead floor +2); a use acknowledged late at +3 pulls the start to it, one at +1 is too early and stays out.
        (DummyTest back, _) = NewTest();
        back.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        FeedHits(back, T0.AddSeconds(5.2), Hit(T0.AddSeconds(5)));
        back.Feed(NoHits, new[] { new SkillUse(T0.AddSeconds(3), CleaveId, Accepted: true) }, NoEffects, T0.AddSeconds(5.5), Me);
        DateTime moved = back.Current!.Start;
        back.Feed(NoHits, new[] { new SkillUse(T0.AddSeconds(1), CleaveId, Accepted: true) }, NoEffects, T0.AddSeconds(5.7), Me);
        bool movesBack = moved == T0.AddSeconds(3) && back.Current.Start == T0.AddSeconds(3) && back.Current.Uses.Count == 1 && back.Remaining(T0.AddSeconds(3)) == TimeSpan.FromSeconds(30);

        // Armed at +4: a use at +3 is before Arm and counts for nothing, even within the lead of a hit at +5.
        (DummyTest afterArm, _) = NewTest();
        afterArm.Arm(TimeSpan.FromSeconds(30), "A", "", T0.AddSeconds(4));
        FeedHits(afterArm, T0.AddSeconds(5.2), Hit(T0.AddSeconds(5)));
        afterArm.Feed(NoHits, new[] { new SkillUse(T0.AddSeconds(3), CleaveId, Accepted: true) }, NoEffects, T0.AddSeconds(5.5), Me);
        bool notBeforeArm = afterArm.Current!.Start == T0.AddSeconds(5);

        Console.WriteLine($"  -> use starts: {useStarts}; effect on never starts: {effectStarts}; tick and summon blow never start, an own hit does: {strayIgnored && ownHitStarts}; start moves back +5 to +3 and not to +1: {movesBack}; not before Arm: {notBeforeArm}");
        return useStarts && effectStarts && strayIgnored && ownHitStarts && movesBack && notBeforeArm;
    }

    private static bool RunWindowScenario()
    {
        Console.WriteLine("[selftest] Dummy engine: the window is [Start, Start + Planned], an event exactly at the end is kept, 1 ms later is dropped, even from a later poll:");
        (DummyTest test, List<DummyRun> finished) = NewTest();
        test.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        DateTime start = T0.AddSeconds(1);
        DateTime end = start.AddSeconds(30);
        FeedHits(test, start, Hit(start));
        FeedHits(test, end.AddSeconds(0.5), Hit(end), Hit(end.AddMilliseconds(1)));
        test.Tick(end.AddSeconds(0.5));
        FeedHits(test, end.AddSeconds(1), Hit(end.AddSeconds(2)));
        test.Tick(end + DummyTest.Grace + TimeSpan.FromSeconds(0.1));
        DummyRun? run = finished.SingleOrDefault();
        bool ok = run is not null && run.Events.Count == 2 && run.Events.Max(e => e.Timestamp) == end && run.Complete && run.Length == TimeSpan.FromSeconds(30)
            && test.State == DummyState.Finished && !test.IsOpen;
        Console.WriteLine($"  -> start and end kept, +1 ms and +2 s dropped, complete: {ok}");
        return ok;
    }

    private static bool RunGraceScenario()
    {
        Console.WriteLine("[selftest] Dummy engine: a late packet in the grace is kept only inside the window; after the grace nothing is added:");
        (DummyTest test, List<DummyRun> finished) = NewTest();
        test.Arm(TimeSpan.FromSeconds(30), "A", "", T0);
        DateTime start = T0.AddSeconds(1);
        DateTime end = start.AddSeconds(30);
        FeedHits(test, start, Hit(start));
        test.Tick(end.AddSeconds(0.2));
        bool finishedOpen = test.State == DummyState.Finished && test.IsOpen && finished.Count == 0 && test.Remaining(end.AddSeconds(0.2)) == TimeSpan.Zero;
        // Hits of the last second reach the poll one second after the time was up.
        FeedHits(test, end.AddSeconds(1), Hit(end.AddSeconds(-0.5)), Hit(end.AddSeconds(0.5)));
        test.Tick(end.AddSeconds(1));
        bool stillOpen = test.IsOpen && finished.Count == 0;
        test.Tick(end + DummyTest.Grace);
        bool closed = !test.IsOpen && finished.Count == 1;
        FeedHits(test, end.AddSeconds(4), Hit(end.AddSeconds(-0.2)));
        bool kept = finished[0].Events.Count == 2 && finished[0].Events.All(e => e.Timestamp <= end);
        test.Tick(end + DummyTest.Grace + DummyTest.FinishedShown + TimeSpan.FromSeconds(1));
        Console.WriteLine($"  -> finished but open: {finishedOpen}; open at +1 s: {stillOpen}; closed at the grace: {closed}; late hit inside kept, outside and after-close dropped: {kept}; idle after the shown time: {test.State == DummyState.Idle}");
        return finishedOpen && stillOpen && closed && kept && test.State == DummyState.Idle;
    }

    private static bool RunStopScenario()
    {
        Console.WriteLine("[selftest] Dummy engine: Stop gives an incomplete run cut at the stop:");
        (DummyTest test, List<DummyRun> finished) = NewTest();
        test.Arm(TimeSpan.FromSeconds(60), "A", "", T0);
        DateTime start = T0.AddSeconds(1);
        FeedHits(test, start, Hit(start), Hit(start.AddSeconds(5)));
        test.Stop(start.AddSeconds(10));
        FeedHits(test, start.AddSeconds(11), Hit(start.AddSeconds(9.5)), Hit(start.AddSeconds(12)));
        test.Tick(start.AddSeconds(10) + DummyTest.Grace);
        DummyRun? run = finished.SingleOrDefault();
        bool ok = run is not null && run.Length == TimeSpan.FromSeconds(10) && !run.Complete && run.Events.Count == 3 && run.Events.All(e => e.Timestamp <= start.AddSeconds(10));

        (DummyTest armed, List<DummyRun> none) = NewTest();
        armed.Arm(TimeSpan.FromSeconds(60), "A", "", T0);
        armed.Stop(T0.AddSeconds(2));
        bool armedStop = armed.State == DummyState.Idle && none.Count == 0;
        Console.WriteLine($"  -> 10 s of 60 s, 3 hits, Complete false: {ok}; Stop while armed cancels: {armedStop}");
        return ok && armedStop;
    }

    private static bool RunDivisorScenario()
    {
        Console.WriteLine("[selftest] Dummy engine: two runs of one length share the divisor (damage / Planned), whatever the last hit's time:");
        DummyRun early = Record(TimeSpan.FromSeconds(30), T0, 300, Enumerable.Range(1, 10).Select(i => (double)i).ToArray());
        DummyRun spread = Record(TimeSpan.FromSeconds(30), T0.AddMinutes(5), 100, Enumerable.Range(1, 30).Select(i => (double)i).ToArray());
        DummyRunSummary a = DummyAnalysis.Summarize(early);
        DummyRunSummary b = DummyAnalysis.Summarize(spread);
        bool ok = a.TotalDamage == 3000 && b.TotalDamage == 3000 && Math.Abs(a.Dps - 100) < 1e-9 && Math.Abs(b.Dps - 100) < 1e-9;
        Console.WriteLine($"  -> 3000 damage in both runs, both 100 DPS: {ok}");
        return ok;
    }

    private static bool RunStatTableScenario()
    {
        Console.WriteLine("[selftest] Own stat table: the whole table that comes before the character is known is kept and adopted; a stat a table lacks is not a difference:");
        // The first frame after a login is the whole table and can arrive before the meter knows the character.
        var directory = new Aion2EntityDirectory();
        directory.NotePendingStats(Me, T0, new[] { (1, 1500), (2, 7), (3, 90) });
        bool keptBack = directory.LocalStats is null;
        directory.LocalPlayerId = Me;
        bool adopted = directory.LocalStats is { } table && table.Values.Count == 3 && table.Values[1] == 1500 && table.Values[3] == 90;
        directory.NoteLocalStats(T0.AddSeconds(5), new[] { (1, 1600) });
        bool merged = directory.LocalStats is { } later && later.Values[1] == 1600 && later.Values[2] == 7 && later.Values.Count == 3;

        // A partial table (one stat) against a whole one: only the stat both have is compared.
        DummyRun Run(int number, params (int Id, int Value)[] stats)
        {
            DateTime start = T0.AddMinutes(10 * number);
            var run = new DummyRun { Number = number, Start = start, Planned = TimeSpan.FromSeconds(30), Length = TimeSpan.FromSeconds(30), PlayerId = Me, TargetName = "Dummy",
                Stats = new StatSnapshot(start, stats.ToDictionary(s => s.Id, s => s.Value)) };
            for (int i = 0; i < 12; i++)
            {
                run.Events.Add(Hit(start.AddSeconds(1 + i)));
            }

            return run;
        }

        DummyTestComparison same = DummyAnalysis.Compare(new[] { Run(1, (1, 1500), (2, 7), (3, 90)) }, new[] { Run(2, (1, 1500)) });
        DummyTestComparison different = DummyAnalysis.Compare(new[] { Run(1, (1, 1500), (2, 7), (3, 90)) }, new[] { Run(2, (1, 1600)) });
        bool notDifferent = same.Warnings.All(w => w.Kind != DummyTestWarningKind.StatsDiffer);
        bool different1 = different.Warnings.FirstOrDefault(w => w.Kind == DummyTestWarningKind.StatsDiffer) is { Ids: { Count: 1 } ids } && ids[0] == 1;
        bool ok = keptBack && adopted && merged && notDifferent && different1;
        Console.WriteLine($"  -> kept until the character is known: {keptBack}; adopted: {adopted}; later changes merge in: {merged}; a missing stat is no difference: {notDifferent}; a real one is: {different1}");
        return ok;
    }

    private static string TempPath(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aiondps-{name}-{Guid.NewGuid():N}.db");
        TempFiles.Add(path);
        return path;
    }

    private static bool RunStoreScenario()
    {
        Console.WriteLine("[selftest] Dummy run store: a run comes back whole (hit kinds, uses, effects, names, levels, stats, times) and the file reopens:");
        string path = TempPath("dummyruns");
        DateTime start = new(2026, 10, 23, 21, 30, 0, DateTimeKind.Local);
        var run = new DummyRun
        {
            Number = 7, Label = "Smash 20 / Cleave 18", Note = "food on", Start = start, Planned = TimeSpan.FromMinutes(1), Length = TimeSpan.FromMinutes(1),
            TargetName = "Training Scarecrow", PlayerId = Me, LevelsConfirmed = true, LevelsReadAt = start.AddMinutes(-3),
            Levels = new List<SkillLevel> { new(11010000, 12, 10), new(12010000, 9, 9) },
            Stats = new StatSnapshot(start.AddMinutes(-2), new Dictionary<int, int> { [1] = 1500, [4] = 33 }),
            Names = new Dictionary<int, string> { [Dummy] = "Training Scarecrow", [Me] = "Me" },
        };
        run.Events.Add(Hit(start.AddSeconds(1), 1200, HitKind.Crit));
        run.Events.Add(Hit(start.AddSeconds(2), 900, HitKind.Double | HitKind.Perfect) with { IsTick = true });
        run.Events.Add(Hit(start.AddSeconds(3), 400, HitKind.Parry));
        run.Uses.Add(new SkillUse(start.AddSeconds(1), CleaveId, true));
        run.Uses.Add(new SkillUse(start.AddSeconds(2), CleaveId, false, 7));
        run.Effects.Add(new EffectChange(start, 5000, true));
        run.Effects.Add(new EffectChange(start.AddSeconds(40), 5000, false));

        bool ok;
        using (var store = new DummyRunStore(path))
        {
            long id = store.Insert(run);
            DummyRun? back = store.Load(id);
            bool same = back is not null && back.Id == id && back.Number == 7 && back.Label == run.Label && back.Note == run.Note && back.Start == run.Start
                && back.Start.Kind == DateTimeKind.Local && back.Planned == run.Planned && back.Length == run.Length && back.Complete && back.TargetName == run.TargetName
                && back.PlayerId == Me && back.LevelsConfirmed && back.LevelsReadAt == run.LevelsReadAt
                && back.Events.SequenceEqual(run.Events) && back.Events[0].Timestamp.Kind == DateTimeKind.Local && back.Events[1].Kind == (HitKind.Double | HitKind.Perfect) && back.Events[1].IsTick
                && back.Uses.SequenceEqual(run.Uses) && back.Effects.SequenceEqual(run.Effects)
                && back.Names.Count == 2 && back.Names[Dummy] == "Training Scarecrow"
                && back.Levels is { Count: 2 } levels && levels[0] == new SkillLevel(11010000, 12, 10) && levels[1] == new SkillLevel(12010000, 9, 9)
                && back.Stats is { } stats && stats.At == run.Stats!.At && stats.Values[1] == 1500 && stats.Values[4] == 33 && stats.Values.Count == 2;
            DummyRunRow row = store.Query().Single();
            bool listed = row.Id == id && row.Number == 7 && row.Complete && row.TargetName == "Training Scarecrow" && row.LevelsConfirmed
                && row.TotalDamage == DummyAnalysis.Summarize(run).TotalDamage && row.Planned == TimeSpan.FromMinutes(1) && store.NextNumber() == 8;
            store.UpdateNote(id, "changed");
            store.UpdateLabel(id, "Other");
            DummyRunRow edited = store.Query().Single();
            bool editedOk = edited.Note == "changed" && edited.Label == "Other" && store.Load(id)!.Note == "changed";
            ok = same && listed && editedOk;
            Console.WriteLine($"  -> whole run back: {same}; summary row and next number: {listed}; note and label edited: {editedOk}");
        }

        using (var again = new DummyRunStore(path))
        {
            bool reopened = again.Count() == 1;
            long gone = again.Query().Single().Id;
            again.Delete(gone);
            bool deleted = again.Count() == 0 && again.Load(gone) is null;
            Console.WriteLine($"  -> reopening the file keeps the run: {reopened}; delete removes it: {deleted}");
            ok &= reopened && deleted;
        }

        return ok;
    }

    private static bool RunPruneScenario()
    {
        Console.WriteLine("[selftest] Dummy run store: Prune keeps the newest 200:");
        string path = TempPath("dummyprune");
        bool ok;
        using (var store = new DummyRunStore(path))
        {
            for (int i = 1; i <= 205; i++)
            {
                DateTime start = T0.AddMinutes(i);
                var run = new DummyRun { Number = i, Start = start, Planned = TimeSpan.FromSeconds(30), Length = TimeSpan.FromSeconds(30), PlayerId = Me };
                run.Events.Add(Hit(start.AddSeconds(1)));
                store.Insert(run);
            }

            int removed = store.Prune();
            List<DummyRunRow> left = store.Query(500);
            ok = removed == 5 && store.Count() == DummyRunStore.Keep && left.Count == 200 && left[0].Number == 205 && left[^1].Number == 6 && store.NextNumber() == 206;
            Console.WriteLine($"  -> 205 filed, 5 removed, 200 left, newest first (205 .. 6): {ok}");
        }

        return ok;
    }

    private static bool RunControllerScenario()
    {
        Console.WriteLine("[selftest] Dummy test controller: two runs on a driven clock are filed, read the setup, and compare:");
        string path = TempPath("dummyctl");
        var directory = new Aion2EntityDirectory { LocalPlayerId = Me };
        directory.Register(Dummy, "Training Scarecrow");
        directory.SetLocalSkills(new[] { new Aion2SkillEntry(11010000, 12, 10), new Aion2SkillEntry(12010000, 9, 9) });
        directory.NoteLocalStats(T0.AddMinutes(-5), new[] { (1, 1500), (4, 33) });
        directory.Own.NoteEffectOn(T0.AddMinutes(-4), 1, 5000, renewed: false);

        DateTime now = T0.AddMinutes(-3);
        var controller = new DummyTestController(() => directory, new DummyRunStore(path), () => now);
        try
        {
            // The player asks for a reading, the game sends the skill frame, the player confirms: confirmed. A confirmation without a frame after the request is not.
            controller.RequestSkillConfirmation();
            bool unconfirmed = !controller.ConfirmSkillsRead();
            controller.RequestSkillConfirmation();
            now = now.AddSeconds(5);
            directory.NoteSkillFrame(now, 0x1338);
            bool confirmed = controller.ConfirmSkillsRead() && controller.SkillsReadInfo is { Confirmed: true, Count: 2, Opcode: 0x1338 };

            var finished = new List<DummyRun>();
            controller.RunFinished += finished.Add;
            for (int k = 0; k < 2; k++)
            {
                DateTime armed = T0.AddMinutes(10 * k);
                now = armed;
                bool armedOk = controller.Arm(TimeSpan.FromSeconds(30), $"Setup {k + 1}", "") && controller.State == DummyState.Armed;
                directory.Own.NoteUse(armed.AddSeconds(1), CleaveId, accepted: true, rejectCode: 0);
                var hits = new List<DamageEvent> { Hit(armed.AddSeconds(-1), 5000) };
                for (int i = 0; i < 30; i++)
                {
                    hits.Add(Hit(armed.AddSeconds(1.1 + i), k == 0 ? 1000 : 1100));
                }

                hits.Add(Hit(armed.AddSeconds(40), 5000));
                hits.Add(new DamageEvent(armed.AddSeconds(2), 7, Dummy, 9999, false, "Other", SkillId: CleaveId));
                now = armed.AddSeconds(2);
                controller.Poll(hits);
                bool running = armedOk && controller.State == DummyState.Running && controller.TargetName == "Training Scarecrow" && controller.TargetIsDummy
                    && controller.Live(armed.AddSeconds(11)) is { } live && live.Hits == 30 && controller.Remaining(armed.AddSeconds(11)) == TimeSpan.FromSeconds(20);
                now = armed.AddSeconds(45);
                controller.Poll(NoHits);
                if (!running)
                {
                    Console.WriteLine($"  -> run {k + 1} did not run as expected (state {controller.State}, target '{controller.TargetName}')");
                    return false;
                }
            }

            IReadOnlyList<DummyRunRow> rows = controller.Runs;
            bool filed = finished.Count == 2 && finished.All(r => r.Id > 0) && rows.Count == 2 && rows[0].Number == 2 && rows[1].Number == 1
                && rows.All(r => r.Complete && r.LevelsConfirmed && r.TotalDamage > 0);
            DummyRun first = controller.LoadRun(rows[1].Id)!;
            bool setup = first.Events.Count == 30 && first.Events.All(e => e.SourceObjectId == Me) && first.Start == T0.AddSeconds(1) && first.TargetName == "Training Scarecrow"
                && first.Levels is { Count: 2 } && first.LevelsConfirmed && first.LevelsReadAt is not null && first.Stats?.Values[1] == 1500
                && first.Effects.Any(e => e.EffectId == 5000 && e.On && e.At == first.Start) && first.Uses.Count == 1;

            DummyTestComparison compare = controller.Compare(new[] { rows[1].Id }, new[] { rows[0].Id });
            bool comparable = compare.Comparable && compare.RunsA == 1 && compare.RunsB == 1 && compare.Dps is { } dps && dps.DpsB > dps.DpsA && compare.PlannedSeconds == 30;
            _ = controller.LevelCurves();
            Console.WriteLine($"  -> levels confirmed only after a frame: {unconfirmed && confirmed}; two runs filed, own hits only: {filed}; setup in the stored run: {setup}; compare of the two: {comparable}");
            return unconfirmed && confirmed && filed && setup && comparable;
        }
        finally
        {
            controller.Dispose();
        }
    }
}
