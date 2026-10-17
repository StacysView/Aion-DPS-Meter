using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>Self-checks for the field boss list (opcode 01 91) and its reader. Run from SelfCheck.Run().</summary>
public static class SelfCheckBossLoot
{
    // Real captures of the field boss list of map 1010 (2026-10-16).
    private const string Boss315 = "01910000F2030000180089950604FCB88D46A1010000008C950650149B46A1010000018A95060034D5C600C79DC7008087435EF58A46A1010000009095069D95D446A1010000008B9506B7129346A1010000008D950681EBCC46A1010000008F9506BEC9B046A1010000008E9506A08BB746A101000000919506002160E346A101000000929506BC4CD046A1010000009D95064971DB46A101000000939506DF143447A10100000094950640C2EF46A101000000959506EE032347A101000000969506E6BAF446A101000000979506AFABE646A10100000098950600DE8C2A47A1010000009995063B0ED746A1010000009A950694461947A1010000009B95067E6EBA46A1010000009C95066799F246A1010000009E95066EDDEC46A1010000009F95068B28E846A101000000A0950645E8DC46A1010000000000";
    private const string Boss412 = "01910000F2030000180089950604FCB88D46A1010000008C950650149B46A1010000018A95060034D5C600C79DC7008087435EF58A46A1010000009095069D95D446A1010000008B9506B7129346A1010000008D950681EBCC46A1010000008F9506BEC9B046A1010000008E9506A08BB746A101000000919506002160E346A101000000929506BC4CD046A1010000009D95064971DB46A101000000939506DF143447A10100000094950640C2EF46A101000000959506EE032347A101000000969506E6BAF446A101000000979506AFABE646A10100000098950600DE8C2A47A1010000009995063B0ED746A1010000009A950694461947A1010000009B95067E6EBA46A1010000009C95066799F246A1010000009E95066EDDEC46A1010000009F95068B28E846A101000000A0950645E8DC46A101000000039FFC11EBC810004F4B4C00BE72B1460E1B0948251FEF46485FBB8346A10100009CFC11E7C810004B4B4C00CED3B946BA91D2C64A2EA646E1012DBB8346A1010000A0FC11E9C810004D4B4C00E73A33C7EFDA0848005D9D46215EBB8346A101000000";

    public static bool Run()
    {
        bool ok = true;
        ok &= RunFieldBossListScenario();
        ok &= RunFieldBossFooterScenario();
        return ok;
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s);

    private static bool RunFieldBossListScenario()
    {
        Console.WriteLine("[selftest] field boss list (opcode 01 91)");
        bool ok = true;
        Aion2FieldBosses.ResetForTests();

        const long timeMs = 1792190148937;
        byte[] f315 = Hex(Boss315);
        byte[] f412 = Hex(Boss412);

        bool p315 = Aion2FieldBosses.TryParse(f315, out int map315, out var slots315);
        ok &= Report("315-byte frame parses, map 1010, 24 slots", p315 && map315 == 1010 && slots315.Count == 24);
        var s21 = slots315.Count >= 21 ? slots315.FirstOrDefault(s => s.Number == 21) : default;
        ok &= Report("slot 21 dead, time 1792190148937", s21.Number == 21 && !s21.Alive && s21.TimeMs == timeMs);
        var s2 = slots315.FirstOrDefault(s => s.Number == 2);
        ok &= Report("slot 2 alive at -27290 / -80782", s2.Number == 2 && s2.Alive && Math.Abs(s2.X - -27290) < 1 && Math.Abs(s2.Y - -80782) < 1);

        bool p412 = Aion2FieldBosses.TryParse(f412, out int map412, out var slots412);
        var t21 = slots412.FirstOrDefault(s => s.Number == 21);
        ok &= Report("412-byte frame (second list after) parses, 24 slots, same slot 21",
            p412 && map412 == 1010 && slots412.Count == 24 && !t21.Alive && t21.TimeMs == timeMs);

        Aion2FieldBosses.Update(map315, slots315);
        FieldBoss? gartua = Aion2FieldBosses.Maps.SelectMany(m => m.Bosses).FirstOrDefault(b => b.Npc == 2101074);
        DateTime back = DateTimeOffset.FromUnixTimeMilliseconds(timeMs).LocalDateTime;
        if (gartua is null)
        {
            ok &= Report("Eternal Gartua in the json", false);
        }
        else
        {
            BossStatus before = Aion2FieldBosses.StatusOf(gartua, back.AddMinutes(-10));
            BossStatus after = Aion2FieldBosses.StatusOf(gartua, back.AddMinutes(1));
            ok &= Report("10 min before: Respawn at the local time", before.Phase == BossPhase.Respawn && before.At == back);
            ok &= Report("1 min after: Now", after.Phase == BossPhase.Now);
        }

        byte[] bad = [0x01, 0x91, 0x00, 0x00, 0xDF, 0x27, 0x09, 0x00, 0x00];
        ok &= Report("map 600031 rejected", !Aion2FieldBosses.TryParse(bad, out _, out _));
        ok &= Report("315 frame cut to 200 bytes rejected", !Aion2FieldBosses.TryParse(f315.AsSpan(0, 200), out _, out _));

        Aion2FieldBosses.ResetForTests();
        return ok;
    }

    private static bool RunFieldBossFooterScenario()
    {
        Console.WriteLine("[selftest] field boss footer, reminder, settings");
        bool ok = true;
        Aion2FieldBosses.ResetForTests();
        byte[] f315 = Hex(Boss315);
        Aion2FieldBosses.TryParse(f315, out int map, out var slots);
        Aion2FieldBosses.Update(map, slots);
        DateTime t = DateTimeOffset.FromUnixTimeMilliseconds(1792190148937).LocalDateTime;
        int[] watched = [2101074];

        var before = AionDPS.Schedule.FieldBossTimers.Footer(watched, t.AddMinutes(-72), "fr");
        ok &= Report("72 min before: one entry Gartua, Respawn, 1h12",
            before.Count == 1 && before[0].ShortName == "Gartua" && before[0].Phase == BossPhase.Respawn && before[0].Left == TimeSpan.FromMinutes(72));
        var en = AionDPS.Schedule.FieldBossTimers.Footer(watched, t.AddMinutes(-72), "en");
        ok &= Report("English short name Gartua", en.Count == 1 && en[0].ShortName == "Gartua");
        var after = AionDPS.Schedule.FieldBossTimers.Footer(watched, t.AddMinutes(1), "fr");
        ok &= Report("1 min after: Now, no countdown", after.Count == 1 && after[0].Phase == BossPhase.Now && after[0].Left is null);
        var unknown = AionDPS.Schedule.FieldBossTimers.Footer([2101217], t, "fr");
        ok &= Report("unknown watched boss: Unknown, Left null", unknown.Count == 1 && unknown[0].Phase == BossPhase.Unknown && unknown[0].Left is null);
        var ordered = AionDPS.Schedule.FieldBossTimers.Footer([2101217, 2101074], t, "fr");
        ok &= Report("entries follow the order watched", ordered.Count == 2 && ordered[1].ShortName == "Gartua");

        var r4 = AionDPS.Schedule.FieldBossTimers.Reminder(watched, t.AddMinutes(-4));
        ok &= Report("4 min before: Back=false", r4 is { Back: false, Npc: 2101074 } && r4.Value.Respawn == t);
        ok &= Report("6 min before: none", AionDPS.Schedule.FieldBossTimers.Reminder(watched, t.AddMinutes(-6)) is null);
        var r30 = AionDPS.Schedule.FieldBossTimers.Reminder(watched, t.AddSeconds(30));
        ok &= Report("30 s after: Back=true", r30 is { Back: true });
        ok &= Report("3 min after: none", AionDPS.Schedule.FieldBossTimers.Reminder(watched, t.AddMinutes(3)) is null);

        var s = System.Text.Json.JsonSerializer.Deserialize<AionDPS.Ui.MeterSettings>("{\"WatchedFieldBosses\":[2100003]}");
        ok &= Report("saved watched list replaces the default", s is not null && s.WatchedFieldBosses.SequenceEqual([2100003]));
        var d = new AionDPS.Ui.MeterSettings();
        ok &= Report("defaults: timer on, reminder off, Gartua watched",
            d.ShowFieldBossTimer && !d.FieldBossReminder && d.WatchedFieldBosses.SequenceEqual([2101074]));

        Aion2FieldBosses.ResetForTests();
        return ok;
    }

    private static bool Report(string what, bool pass)
    {
        Console.WriteLine("  -> " + what + ": " + pass);
        return pass;
    }
}
