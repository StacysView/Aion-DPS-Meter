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
        ok &= RunLootScenario();
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

    // Real frames of the loot of a solo run in Verteron (2026-10-16 22:42:29).
    private const string DropA = "1E56017AA8000092B06CC714390948007881463CEE6CC7682409480078814609AA0000220E9300DEF263240100000002068E030000001705158C7546A1010000010301";
    private const string DropB = "1E56017AA8000092B06CC71439094800788146045E6CC7AA250948007E814609AA0000240E9300F643DE1F0100000002068E030000001705158C7546A1010000010301";
    private const string InvA = "1B56010000EFBA020000160100F643DE1F0100000000000000040000000000000000000000000000050000";
    private const string InvB = "1B56010000B6D91500001D0000DEF263240100000000000000013800000000000000000000000000050000";
    private const string PickA = "1F56017AA8000009AA0000240E9300068E03000000170500";
    private const string PickB = "1F56017AA8000009AA0000220E9300068E03000000170500";
    private const string DropOther = "1E56013771000044B453C7DAF1084800B6824648D653C79A140948009482460CAA0000321E9300F643DE1F01000000024AFB030000001705ED947646A1010000010301";
    private const string PickOther = "1F5601377100000CAA0000321E93004AFB03000000170500";
    private const string DropGone = "1E560107AB00001C376FC71209044800A48146B7636FC7D0EC034800A281462DA70000B6119300F643DE1F01000000026BAD0300000018053EC87546A1010000010301";
    private const string PickGone = "1F560107AB00002DA70000B61193006BAD03000000180501";

    private static bool RunLootScenario()
    {
        Console.WriteLine("[selftest] loot (0x1E56 / 0x1B56 / 0x1F56)");
        bool ok = true;
        Aion2Loot.ResetForTests();
        DateTime t = new(2026, 10, 16, 22, 42, 29);
        void Feed(string hex, DateTime at)
        {
            byte[] f = Hex(hex);
            Aion2Loot.Observe((f[0] << 8) | f[1], f, at);
        }

        byte[] zone = [0x21, 0x36, 0, 0, 0, 0, 0xF2, 0x03, 0, 0, 0, 0, 0, 0];
        Aion2Loot.Observe(0x2136, zone, t);
        Feed(DropA, t.AddSeconds(1));
        Feed(DropB, t.AddSeconds(1));
        Feed(InvA, t.AddSeconds(1.2));
        Feed(InvB, t.AddSeconds(1.3));
        Feed(PickA, t.AddSeconds(1.5));
        Feed(PickB, t.AddSeconds(1.6));
        var s = Aion2Loot.Sessions;
        ok &= Report("one session, map 1010, two items", s.Count == 1 && s[0].Maps.Count == 1 && s[0].Maps[0].Zone == 1010 && s[0].Maps[0].Items.Count == 2);
        var items = s.Count > 0 && s[0].Maps.Count > 0 ? s[0].Maps[0].Items : new List<LootItem>();
        ok &= Report("610530014 x1 and 534660086 x1",
            items.Any(i => i.ItemId == 610530014 && i.Count == 1) && items.Any(i => i.ItemId == 534660086 && i.Count == 1));

        Feed(DropOther, t.AddSeconds(5));
        Feed(PickOther, t.AddSeconds(5.2));
        ok &= Report("another player's drop (no inventory add): still 2 items", Aion2Loot.Sessions[0].Maps[0].Items.Count == 2);
        Feed(DropGone, t.AddSeconds(8));
        Feed(PickGone, t.AddSeconds(8.2));
        ok &= Report("vanished drop (last byte 01): still 2 items", Aion2Loot.Sessions[0].Maps[0].Items.Count == 2);

        byte[] shortDrop = Hex(DropA)[..20];
        Feed(InvA, t.AddSeconds(9));
        Aion2Loot.Observe(0x1E56, shortDrop, t.AddSeconds(9));
        ok &= Report("truncated 0x1E56 (20 bytes) ignored", Aion2Loot.Sessions[0].Maps[0].Items.Count == 2);

        // same item picked again later in the same map merges
        Feed(DropA, t.AddSeconds(20));
        Feed(InvB, t.AddSeconds(20.1));
        Feed(PickB, t.AddSeconds(20.3));
        var merged = Aion2Loot.Sessions[0].Maps[0].Items.FirstOrDefault(i => i.ItemId == 610530014);
        ok &= Report("same item, same map: counts merge (2), still one session", merged is { Count: 2 } && Aion2Loot.Sessions.Count == 1);

        // 31 minutes later: a new session
        Feed(DropA, t.AddMinutes(40));
        Feed(InvB, t.AddMinutes(40).AddSeconds(0.1));
        Feed(PickB, t.AddMinutes(40).AddSeconds(0.3));
        ok &= Report("pickup 40 min later: new session, newest first", Aion2Loot.Sessions.Count == 2 && Aion2Loot.Sessions[0].Start > Aion2Loot.Sessions[1].Start);

        ok &= Report("ZoneName(1010,fr) is Verteron", Aion2Loot.ZoneName(1010, "fr") == "Verteron");
        ok &= Report("ZoneName(600002,fr) Grotte de Krao, 0 unknown, 4242 Zone 4242",
            Aion2Loot.ZoneName(600002, "fr") == "Grotte de Krao" && Aion2Loot.ZoneName(0, "fr") == "Carte inconnue" && Aion2Loot.ZoneName(4242, "en") == "Zone 4242");
        ok &= Report("Name(110630047,en) Spiritforged Orb", Aion2ItemNames.Name(110630047, "en") == "Spiritforged Orb");
        ok &= Report("Name(999999999,fr) Objet 999999999", Aion2ItemNames.Name(999999999, "fr") == "Objet 999999999");

        Aion2Loot.LoadFromText("{not json");
        ok &= Report("garbage store loads as empty, no exception", Aion2Loot.Sessions.Count == 0);

        Aion2Loot.ResetForTests();
        return ok;
    }

    private static bool Report(string what, bool pass)
    {
        Console.WriteLine("  -> " + what + ": " + pass);
        return pass;
    }
}
