using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// Self-checks for the boss cast bar (Ui/BossCastBar.cs, SkeeveTV's cast bar, MIT): the windup frame (0x0938) on real
/// frames, and the rows the bar makes of them. Run from SelfCheck.Run().
/// </summary>
public static class SelfCheckBossCast
{
    // SkeeveTV's debug recording of 2026-10-12 (his SelfCheckCombatOverlay, MIT): monster 19144 winds up skill 1236440 at
    // entity 41205, 1,500 ms, with a place (aim flags 2).
    private const string SkeeveWindup = "0938C8950102D8DD12000102F5C10246E29042934E3F4705C6B846001D0347DC0B01904E";

    // Stacy's Canyon Urugugu run (C:\dev\aion2-captures\2026-10-02_12-02_dauphnir.jsonl; the local player is Boulenbouche,
    // entity 3279). The two bosses appear (0x4136, NPC 2310206 Guardian Captain Raur = entity 18668 at 32.5 s, NPC 2310218
    // Divine Auldor = entity 36741 at 249.3 s), then wind up:
    private const string RaurSpawn = "4136EC91010C22003E4023000002679A0346BC70834500C0614400D83D43008701C4AA22C4AA22640000006400000000000000000000000000000000000000000000000000000001000000000000000000000000000000000000000603110181969800FFFFFFFFFFFFFFFF8075D52ABB030000EC91010128679A0346BC70834500C06144110284969800FFFFFFFFFFFFFFFF8075D52ABB030000EC910101679A0346BC70834500C061441103BC060000FFFFFFFFFFFFFFFF8075D52ABB030000EC910105679A0346BC70834500C0614401001E0000000301EE020000EE020000BDA3EC4100";
    private const string AuldorSpawn = "4136859F020C20004A402300000200CAA24700103BC700FC0D4700D83D43008701E8DD0DE8DD0D640000006400000000000000000000000000000000000000000000000000000001000000000000000000000000000000000000000602110181969800FFFFFFFFFFFFFFFF8075D52ABB030000859F02010400CAA24700103BC700FC0D47110284969800FFFFFFFFFFFFFFFF8075D52ABB030000859F020100CAA24700103BC700FC0D4706001E00000081A4EC8300";

    // 100.2 s: Raur, skill 1504420 (no client text, a cone on the floor) at Boulenbouche, 1,500 ms, aim flags 0 (no place).
    private const string RaurAtYou = "0938EC910100A4F416000102CF194DDC2D4216E3FA45382B8745E3CE6144DC0B01";

    // 111.9 s: Raur is gone (0x4236).
    private const string RaurGone = "4236EC91010003";

    // 273.4 s: Auldor, 1600960 "Flying Overhead Slam" (Fracas aérien) at Boulenbouche, 830 ms, with a place.
    private const string AuldorSlamAtYou = "0938859F0202C06D18000102CF192BC72C43EF989E470B003AC700FC0D47BE0601904E";

    // 293.5 s: Auldor, 1600940 "Ambush Slam" at ScareNight (entity 10838), 1,070 ms.
    private const string AuldorAmbushAtOther = "0938859F0202AC6D18000102D6547F0B28C33B2A9E47677D3AC700ED0D47AE0801904E";

    // 332.7 s: Auldor, 1601000 - the client's text is just "Attack", its floor shape a circle - at Daidai (entity 1586), 1,170 ms.
    private const string AuldorAttackCircle = "0938859F0202E86D18000102B20CDE668AC2217AA447461938C760F70D47920901904E";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    public static bool Run()
    {
        Console.WriteLine("[selftest] Boss cast bar (windup frame 0x0938, real frames of SkeeveTV and of Stacy's Canyon Urugugu run):");
        string before = LocalizationManager.Instance.Language;
        bool ok = true;
        try
        {
            LocalizationManager.Instance.Language = "en";
            ok &= RunSkeeveFrame();
            ok &= RunUrugugu();
            LocalizationManager.Instance.Language = "fr";
            ok &= RunFrench();
        }
        finally
        {
            LocalizationManager.Instance.Language = before;
            Aion2SkillNames.Language = before;
        }

        return ok;
    }

    private static bool RunSkeeveFrame()
    {
        var dir = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), dir);
        DateTime at = new(2026, 10, 12, 21, 3, 6, 187);
        decoder.Decode(Hex(SkeeveWindup), at).ToArray();
        var casts = dir.Casts.Running(at.AddMilliseconds(200), TimeSpan.FromSeconds(1));
        bool windup = casts.Count == 1 && casts[0] is { Actor: 19144, SkillId: 1236440, Target: 41205, DurationMs: 1500 };
        // an ordinary monster (no boss of the catalog, no 500,000 hit points) is not shown
        bool hidden = BossCastBarData.Build(dir, at.AddMilliseconds(200), "en").Count == 0;
        dir.HitPoints.Note(19144, at, 2_400_000);
        bool elite = BossCastBarData.Build(dir, at.AddMilliseconds(200), "en") is [{ Caster: "#19144", Target: "#41205", CastMs: 1500, AtYou: false }];
        bool over = dir.Casts.Running(at.AddMilliseconds(1500 + 4500), TimeSpan.FromSeconds(1)).Count == 0;
        return Report("SkeeveTV's frame: caster 19144, skill 1236440, aimed at 41205, 1,500 ms; gone a few seconds after its end", windup && over)
            & Report("an ordinary monster's cast is not shown, an elite's (2.4 M hit points) is", hidden && elite);
    }

    private static bool RunUrugugu()
    {
        bool ok = true;
        var dir = new Aion2EntityDirectory { LocalPlayerId = 3279 };
        dir.Register(3279, "Boulenbouche");
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), dir);
        DateTime t0 = new(2026, 10, 2, 12, 2, 0);
        decoder.Decode(Hex(RaurSpawn), t0.AddSeconds(32.536)).ToArray();
        decoder.Decode(Hex(AuldorSpawn), t0.AddSeconds(249.337)).ToArray();
        ok &= Report("the spawn frames name the bosses (18668 = Guardian Captain Raur, 36741 = Divine Auldor)",
            dir.BossNpcIdOf(18668) == 2310206 && dir.BossNpcIdOf(36741) == 2310218 && dir.NpcIdOf(36741) == 2310218);

        // Raur's frontal attack at Stacy: no client text, so the caster leads and the floor shape names the skill; red (at you)
        DateTime raur = t0.AddSeconds(100.234);
        decoder.Decode(Hex(RaurAtYou), raur).ToArray();
        var rows = BossCastBarData.Build(dir, raur.AddMilliseconds(600), "en");
        ok &= Report($"Raur 1504420 at Boulenbouche: \"{Describe(rows)}\"",
            rows is [{ Skill: "Attack to the front", Caster: "Guardian Captain Raur", Target: "You", AtYou: true, Generic: true, CastMs: 1500 } r] && Math.Abs(r.ElapsedMs - 600) < 1);
        ok &= Report("the cast stays 0.7 s after its end, then leaves the bar",
            BossCastBarData.Build(dir, raur.AddMilliseconds(2100), "en").Count == 1 && BossCastBarData.Build(dir, raur.AddMilliseconds(2300), "en").Count == 0);
        decoder.Decode(Hex(RaurAtYou), raur.AddSeconds(5)).ToArray();
        decoder.Decode(Hex(RaurGone), raur.AddSeconds(5.5)).ToArray();
        ok &= Report("the remove frame (0x4236) ends a cast of the boss that left", BossCastBarData.Build(dir, raur.AddSeconds(5.6), "en").Count == 0);

        // Auldor's named slam at Stacy, then his named skill at someone else, both running: the one at Stacy is first only
        // because it ends first (rows are in the order they land)
        DateTime slam = t0.AddSeconds(273.384);
        dir.Register(10838, "ScareNight");
        decoder.Decode(Hex(AuldorSlamAtYou), slam).ToArray();
        decoder.Decode(Hex(AuldorAmbushAtOther), slam.AddMilliseconds(100)).ToArray();
        rows = BossCastBarData.Build(dir, slam.AddMilliseconds(400), "en");
        ok &= Report($"Auldor's two skills: \"{Describe(rows)}\"",
            rows is [{ Skill: "Flying Overhead Slam", Caster: "Divine Auldor", Target: "You", AtYou: true, Generic: false, CastMs: 830 },
                { Skill: "Ambush Slam", Target: "ScareNight", AtYou: false, Generic: false, CastMs: 1070 }]);

        // a plain "Attack" named by its circle
        DateTime circle = t0.AddSeconds(332.735);
        decoder.Decode(Hex(AuldorAttackCircle), circle).ToArray();
        dir.Register(1586, "Daidai"); // the recording's own names (its replay: 1586 = Daidai, 10838 = ScareNight)
        rows = BossCastBarData.Build(dir, circle.AddMilliseconds(300), "en");
        ok &= Report($"Auldor's 1601000 (\"Attack\"): \"{Describe(rows)}\"",
            rows is [{ Skill: "Attack in a circle", Caster: "Divine Auldor", Target: "Daidai", Generic: true, AtYou: false }]);

        // a player's windup is no boss attack (the same frame with Boulenbouche as caster), even with the hit points of a boss
        DateTime own = circle.AddSeconds(10);
        dir.NoteClass(3279, "Spiritmaster");
        dir.Casts.NoteWindup(new CastNotice(3279, 1601000, 36741, own, 1000));
        ok &= Report("a player's windup is not shown", BossCastBarData.Build(dir, own.AddMilliseconds(100), "en").Count == 0);
        return ok;
    }

    private static bool RunFrench()
    {
        var dir = new Aion2EntityDirectory { LocalPlayerId = 3279 };
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), dir);
        DateTime t0 = new(2026, 10, 2, 12, 2, 0);
        decoder.Decode(Hex(RaurSpawn), t0.AddSeconds(32.536)).ToArray();
        decoder.Decode(Hex(AuldorSpawn), t0.AddSeconds(249.337)).ToArray();
        decoder.Decode(Hex(RaurAtYou), t0.AddSeconds(100.234)).ToArray();
        var raur = BossCastBarData.Build(dir, t0.AddSeconds(100.5), "fr");
        decoder.Decode(Hex(AuldorSlamAtYou), t0.AddSeconds(273.384)).ToArray();
        var slam = BossCastBarData.Build(dir, t0.AddSeconds(273.5), "fr");
        decoder.Decode(Hex(AuldorAttackCircle), t0.AddSeconds(332.735)).ToArray();
        var circle = BossCastBarData.Build(dir, t0.AddSeconds(332.8), "fr");
        bool ok = raur is [{ Skill: "Attaque vers l'avant", Target: "Toi" }] && slam is [{ Skill: "Fracas aérien" }] && circle is [{ Skill: "Attaque en cercle" }];
        string name = slam.Count == 1 ? slam[0].Caster : "-";
        return Report($"in French: \"{Describe(raur)}\", \"{Describe(slam)}\", \"{Describe(circle)}\" (caster from the NPC table: {name})", ok && name.Length > 0 && name != "#36741");
    }

    private static string Describe(IReadOnlyList<CastRowData> rows) =>
        string.Join(" | ", rows.Select(r => $"{r.Skill} → {r.Target} ({r.Caster}{(r.AtYou ? ", red" : "")})"));

    private static bool Report(string label, bool ok)
    {
        Console.WriteLine($"  -> {label}: {ok}");
        return ok;
    }
}
