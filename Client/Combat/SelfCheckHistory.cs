using System.IO;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>Fight history: segmentation by silence gap, recording only finished fights, and a
/// SQLite round trip (insert, search, load with events intact, prune).</summary>
public static class SelfCheckHistory
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunSegmenterScenario();
        ok &= RunRecorderAndStoreScenario();
        ok &= RunTakenHitsScenario();
        ok &= RunRecentFightsScenario();
        ok &= RunEventBlobScenario();
        return ok;
    }

    /// <summary>Taken mode's rule on synthetic hits: a monster's and an outside player's hits count,
    /// one's own, a teammate's, a heal and a hit on a monster do not.</summary>
    private static bool RunTakenHitsScenario()
    {
        Console.WriteLine("[selftest] Taken: monsters' and outside players' hits, never one's own or a teammate's:");
        const int Me = 1, Mate = 2, Foe = 3, Monster = 100;
        var players = new HashSet<int> { Me, Mate, Foe };
        var mates = new HashSet<int> { Me, Mate };
        DateTime t = new(2026, 10, 4, 20, 45, 0);
        bool Counts(int source, int target, bool heal = false) =>
            TakenHits.IsHostile(new DamageEvent(t, source, target, 100, heal), players.Contains, mates.Contains);
        bool ok = Counts(Monster, Me) && Counts(Foe, Me) && !Counts(Me, Me) && !Counts(Mate, Me)
            && !Counts(Foe, Me, heal: true) && !Counts(Me, Monster);
        Console.WriteLine($"  -> monster yes, outside player yes, self no, teammate no, heal no, on a monster no: {ok}");
        return ok;
    }

    /// <summary>An overlay fight as a history entry (synthetic): the shown players only, damage, DPS
    /// over their own span (a stranger's earlier hits left out), healing, hits taken from a monster
    /// and an outside player; a 5 s poke is no fight; the store keeps the newest ten.</summary>
    private static bool RunRecentFightsScenario()
    {
        Console.WriteLine("[selftest] Recent fights: one overlay fight as a history entry, the newest ten kept:");
        const int Me = 1, Mate = 2, Stranger = 3, Foe = 4, Boss = 100;
        var players = new HashSet<int> { Me, Mate, Stranger, Foe };
        var shown = new HashSet<int> { Me, Mate };
        DateTime t = new(2026, 10, 4, 21, 0, 0);
        var events = new List<DamageEvent>();
        for (int i = 0; i <= 20; i++)
        {
            events.Add(new DamageEvent(t.AddSeconds(i), Me, Boss, 1000, IsHeal: false, Skill: "Combustion"));
        }

        events.Add(new DamageEvent(t.AddSeconds(5), Stranger, Boss, 5000, IsHeal: false));
        // A stranger hitting the boss long before the shown players' fight does not stretch it.
        events.Add(new DamageEvent(t.AddSeconds(-30), Stranger, Boss, 5000, IsHeal: false));
        events.Add(new DamageEvent(t.AddSeconds(6), Mate, Me, 700, IsHeal: true, Skill: "Healing Light"));
        events.Add(new DamageEvent(t.AddSeconds(7), Boss, Me, 300, IsHeal: false, Skill: "Attack"));
        events.Add(new DamageEvent(t.AddSeconds(8), Foe, Me, 200, IsHeal: false, Skill: "Tempest Shot"));
        var context = new FightContext(id => id switch { Me => "Me", Mate => "Mate", Stranger => "Stranger", Foe => "Foe", _ => "Boss" },
            _ => "", _ => "", players.Contains, id => id == Me, _ => false, _ => false, "aion2", null);
        FightDetail? fight = RecentFights.Describe(events, "Boss", context, shown.Contains, shown.Contains, _ => false);
        FightParticipant? me = fight?.Participants.FirstOrDefault(p => p.Name == "Me");
        FightParticipant? mate = fight?.Participants.FirstOrDefault(p => p.Name == "Mate");
        bool described = fight is not null && fight.Participants.Count == 2 && me is not null && mate is not null
            && me.Damage == 21_000 && Math.Abs((me.Dps ?? 0) - 1050) < 0.5 && me.DamageTaken == 500
            && mate.Healing == 700 && fight.Summary.TargetName == "Boss" && fight.Summary.Duration == TimeSpan.FromSeconds(20);
        var poke = events.Where(ev => ev.Timestamp <= t.AddSeconds(5)).ToList();
        bool tooShort = RecentFights.Describe(poke, "Boss", context, shown.Contains, shown.Contains, _ => false) is null;

        string path = Path.Combine(Path.GetTempPath(), $"aiondps-recent-{Guid.NewGuid():N}.db");
        bool keptTen;
        using (var store = new FightStore(path))
        {
            for (int i = 0; i < 12; i++)
            {
                var shifted = events.Select(ev => ev with { Timestamp = ev.Timestamp.AddMinutes(i) }).ToList();
                RecentFights.Save(store, RecentFights.Describe(shifted, $"Fight {i}", context, shown.Contains, shown.Contains, _ => false)!);
            }

            var left = store.Query(null, 50);
            keptTen = left.Count == 10 && left[0].TargetName == "Fight 11" && left.All(f => f.TargetName != "Fight 0");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(path);
        Console.WriteLine($"  -> shown players with damage/DPS/healing/taken: {described}; a 5 s poke is no fight: {tooShort}; newest ten kept: {keptTen}");
        return described && tooShort && keptTen;
    }

    /// <summary>A stored event keeps its skill id and tick flag - the skills window finds a skill's
    /// icon by its id, and a fight opened from the history had none.</summary>
    private static bool RunEventBlobScenario()
    {
        Console.WriteLine("[selftest] Stored events keep their skill id and tick flag (icons in the history's skills window):");
        var sent = new DamageEvent(new DateTime(2026, 10, 4, 21, 0, 0, DateTimeKind.Local), 1, 100, 1234, IsHeal: false,
            Skill: "Combustion", IsCritical: true, IsTick: true, SkillId: 16040010);
        var (events, _) = EventBlob.Unpack(EventBlob.Pack(new[] { sent }, new Dictionary<int, string>()));
        bool ok = events.Count == 1 && events[0] == sent;
        Console.WriteLine($"  -> the event comes back whole: {ok}");
        return ok;
    }

    private static void TryDelete(string path)
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

    private const int You = 1;
    private const int Healer = 2;
    private const int Boss = 100;
    private const int Dummy = 101;

    private static List<DamageEvent> TwoRuns(DateTime start)
    {
        var events = new List<DamageEvent>();
        for (int i = 0; i < 30; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 2), You, Boss, 1000 + i, IsHeal: false, Skill: "Cleave"));
        }
        events.Add(new DamageEvent(start.AddSeconds(20), Boss, You, 500, IsHeal: false));
        events.Add(new DamageEvent(start.AddSeconds(21), Healer, You, 400, IsHeal: true, Skill: "Healing Light"));
        // Ten minutes later: the same boss again - a separate run.
        DateTime second = start.AddMinutes(10);
        for (int i = 0; i < 20; i++)
        {
            events.Add(new DamageEvent(second.AddSeconds(i * 3), You, Boss, 2000, IsHeal: false));
        }
        // A training dummy poke that must never be recorded.
        events.Add(new DamageEvent(start.AddMinutes(20), You, Dummy, 1, IsHeal: false));
        events.Add(new DamageEvent(start.AddMinutes(20).AddSeconds(30), You, Dummy, 1, IsHeal: false));
        return events;
    }

    private static bool RunSegmenterScenario()
    {
        Console.WriteLine("[selftest] Fight segmentation (120 s silence gap):");
        var start = new DateTime(2026, 9, 22, 20, 0, 0);
        List<FightSegment> segments = FightSegmenter.Segment(TwoRuns(start), Boss);
        bool twoRuns = segments.Count == 2;
        bool firstBounds = twoRuns && segments[0].Start == start && segments[0].End == start.AddSeconds(58) && segments[0].Hits.Count == 30;
        bool secondBounds = twoRuns && segments[1].Start == start.AddMinutes(10) && segments[1].Hits.Count == 20;
        bool healsExcluded = twoRuns && segments[0].Hits.All(h => !h.IsHeal && h.TargetObjectId == Boss);
        Console.WriteLine($"  -> two runs, split at the ten-minute gap: {twoRuns}");
        Console.WriteLine($"  -> first run spans its own hits only: {firstBounds}");
        Console.WriteLine($"  -> second run picked up whole: {secondBounds}");
        Console.WriteLine($"  -> heals and the boss's own hits are not part of a run: {healsExcluded}");
        return twoRuns && firstBounds && secondBounds && healsExcluded;
    }

    private static bool RunRecorderAndStoreScenario()
    {
        Console.WriteLine("[selftest] Fight recorder + SQLite store:");
        string path = Path.Combine(Path.GetTempPath(), "aiondps-selftest-" + Guid.NewGuid().ToString("N"), "fights.db");
        try
        {
            using var store = new FightStore(path);
            var recorder = new FightRecorder(store);
            var start = new DateTime(2026, 9, 22, 20, 0, 0);
            List<DamageEvent> events = TwoRuns(start);
            var names = new Dictionary<int, string> { [You] = "You", [Healer] = "Sardine", [Boss] = "Raksha Boilheart", [Dummy] = "Training Dummy" };
            var context = new FightContext(
                NameOf: id => names.GetValueOrDefault(id, $"0x{id:X8}"),
                ClassOf: id => id == You ? "Gladiator" : id == Healer ? "Cleric" : "?",
                FactionOf: _ => "Elyos",
                IsPlayer: id => id is You or Healer,
                IsSelf: id => id == You,
                IsEnemy: _ => false,
                IsIgnoredTarget: name => name == "Training Dummy",
                Game: "aion",
                ServerName: "Origin Aion");

            // Mid-first-fight: nothing has ended yet.
            int duringFirst = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(30)).ToList(), start.AddSeconds(31), context);
            // Long after the first run, before the second: exactly the first run is filed.
            int afterFirst = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(58)).ToList(), start.AddMinutes(5), context);
            // Same call again must not file it twice.
            int again = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(58)).ToList(), start.AddMinutes(6), context);
            // Flush at the end files the second run but never the dummy.
            int flushed = recorder.Tick(events, start.AddMinutes(21), context, flushAll: true);

            bool recordedWhenEnded = duringFirst == 0 && afterFirst == 1 && again == 0 && flushed == 1 && store.Count() == 2;

            List<FightSummary> all = store.Query(null);
            List<FightSummary> bySardine = store.Query("sardine");
            List<FightSummary> byBoss = store.Query("boilheart");
            bool searchWorks = all.Count == 2 && bySardine.Count == 1 && byBoss.Count == 2 && all[0].StartedAt > all[1].StartedAt;

            FightDetail? first = store.Load(bySardine[0].Id);
            bool detailIntact = first is not null
                && first.Summary.TargetName == "Raksha Boilheart"
                && first.Summary.SelfName == "You"
                && first.Participants.Count == 2
                && first.Participants[0].Name == "You" && first.Participants[0].Damage == Enumerable.Range(0, 30).Sum(i => 1000 + i)
                && first.Participants[0].DamageTaken == 500
                && first.Participants.Single(p => p.Name == "Sardine").Healing == 400
                && first.Events.Count == 32
                && first.Events.All(e => e.Timestamp.Kind == DateTimeKind.Unspecified)
                && first.Events.Any(e => e.Skill == "Cleave")
                && first.Names[Boss] == "Raksha Boilheart";

            int pruned = store.Prune(retentionDays: 3650, maxFights: 1);
            bool pruneKeepsNewest = pruned == 1 && store.Count() == 1 && store.Query(null)[0].StartedAt == start.AddMinutes(10);

            Console.WriteLine($"  -> fights are filed once, only after the silence gap or a flush, never the dummy: {recordedWhenEnded}");
            Console.WriteLine($"  -> search by target or participant, newest first: {searchWorks}");
            Console.WriteLine($"  -> loaded detail keeps participants, events, skills and names: {detailIntact}");
            Console.WriteLine($"  -> prune to a cap keeps the newest fight: {pruneKeepsNewest}");
            return recordedWhenEnded && searchWorks && detailIntact && pruneKeepsNewest;
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
