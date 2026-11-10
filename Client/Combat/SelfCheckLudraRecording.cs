using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;

namespace AionDPS.Combat;

/// <summary>The Ludra recording (Stacy, 2026-11-10): the markers in the capture file, when the recording starts and stops by itself.</summary>
public static class SelfCheckLudraRecording
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Ludra recording and voice markers:");
        bool ok = RunMarkers();
        ok &= RunSteps();
        ok &= RunBossHere();
        return ok;
    }

    /// <summary>A marker line sits between the segments: the replay steps over it, the markers come back with their word and time.</summary>
    private static bool RunMarkers()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aiondps-selftest-markers-{Guid.NewGuid():N}.jsonl");
        try
        {
            DateTime t = new(2026, 11, 10, 13, 50, 0);
            using (var writer = new SegmentRecording.Writer(path))
            {
                writer.Write(new TcpSegment(t, "10.0.0.1:13328", "192.168.0.2:50000", 1, new byte[] { 1, 2, 3 }, FromServer: true));
                writer.WriteMarker(t.AddSeconds(1.5), "faux");
                writer.Write(new TcpSegment(t.AddSeconds(2), "10.0.0.1:13328", "192.168.0.2:50000", 4, new byte[] { 4 }, FromServer: true));
                writer.WriteMarker(t.AddSeconds(3), "orbe");
            }

            var segments = SegmentRecording.Read(path).ToList();
            var markers = SegmentRecording.ReadMarkers(path);
            bool ok = segments.Count == 2 && segments[1].Payload.Span.SequenceEqual(new byte[] { 4 })
                && markers is [{ Word: "faux" } first, { Word: "orbe" } second] && first.At == t.AddSeconds(1.5) && second.At == t.AddSeconds(3);
            Console.WriteLine($"  -> 2 segments and 2 markers in one file, each read back on its own: {ok}");
            return ok;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool RunSteps()
    {
        DateTime zone = new(2026, 11, 10, 13, 40, 0), next = zone.AddMinutes(30);
        bool start = LudraRecording.Next(true, false, null, zone, null, ludraBossHere: true) == LudraRecordingStep.Start;
        bool notWithoutBoss = LudraRecording.Next(true, false, null, zone, null, ludraBossHere: false) == LudraRecordingStep.None;
        bool notWhenOff = LudraRecording.Next(false, false, null, zone, null, ludraBossHere: true) == LudraRecordingStep.None;
        bool notOverHand = LudraRecording.Next(true, true, null, zone, null, ludraBossHere: true) == LudraRecordingStep.None; // one started by hand runs
        bool keeps = LudraRecording.Next(true, true, zone, zone, null, ludraBossHere: false) == LudraRecordingStep.None; // between two bosses
        bool stopsOnLeaving = LudraRecording.Next(true, true, zone, next, null, ludraBossHere: false) == LudraRecordingStep.Stop;
        bool stopsWhenOff = LudraRecording.Next(false, true, zone, zone, null, ludraBossHere: true) == LudraRecordingStep.Stop;
        bool declined = LudraRecording.Next(true, false, null, zone, zone, ludraBossHere: true) == LudraRecordingStep.None; // stopped by hand in this zone
        bool nextZone = LudraRecording.Next(true, false, null, next, zone, ludraBossHere: true) == LudraRecordingStep.Start;
        bool ok = start && notWithoutBoss && notWhenOff && notOverHand && keeps && stopsOnLeaving && stopsWhenOff && declined && nextZone;
        Console.WriteLine($"  -> starts at a boss of Ludra, runs between bosses, stops at the zone change; a recording by hand is left alone, one stopped by hand stays stopped in that zone: {ok}");
        return ok;
    }

    /// <summary>Ketu (2301007, Abyssal Forge: Ludra) read alive since the zone change counts; dead, read before the zone change, or another dungeon's boss does not.</summary>
    private static bool RunBossHere()
    {
        DateTime zone = new(2026, 11, 10, 13, 40, 0);
        var dir = new Aion2EntityDirectory();
        dir.RegisterNpc(19741, 2301007);
        dir.HitPoints.Note(19741, zone.AddMinutes(8), 76_479_114);
        bool here = LudraRecording.LudraBossHere(dir, zone);
        bool before = !LudraRecording.LudraBossHere(dir, zone.AddMinutes(9));

        var dead = new Aion2EntityDirectory();
        dead.RegisterNpc(19741, 2301007);
        dead.HitPoints.Note(19741, zone.AddMinutes(8), 0);
        bool notDead = !LudraRecording.LudraBossHere(dead, zone);

        var elsewhere = new Aion2EntityDirectory();
        elsewhere.RegisterNpc(18668, 2300171); // Ultimate Berk, Krao Cave
        elsewhere.HitPoints.Note(18668, zone.AddMinutes(1), 1_000_000);
        bool notOther = !LudraRecording.LudraBossHere(elsewhere, zone);

        bool ok = here && before && notDead && notOther;
        Console.WriteLine($"  -> a boss of Ludra alive in this zone: Ketu {here}, read before the zone change {!before}, dead {!notDead}, Ultimate Berk {!notOther}: {ok}");
        return ok;
    }
}
