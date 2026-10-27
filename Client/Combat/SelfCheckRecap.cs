using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// The "Recap du jour" counters: the ticket frames (0x0B61 login list, 0x0C61 change), the shop counter 0x8E56, the mission head of the quest
/// list 0x1C8D and the membership frame 0x3BE3. The scenarios and their real frames (hex; debug recordings of 2026-10-13/14, character Aahz)
/// are ported from SkeeveTV's SelfCheckTickets (Aion 2 DPS Meter, MIT licence, https://github.com/SkeeveAN/Aion-DPS-Meter at d574db2f);
/// the routing through this fork's opcodes.json families is checked on top.
/// </summary>
public static class SelfCheckRecap
{
    // the login list of the character Aahz: 73 entries, 492 bytes (SkeeveTV's fixture)
    public const string SampleLogin = "0b61490c010000000a0101030000008085800100000000040400000002000600000004070000001c04080000000201090000008085800100000000040a0000000e040b00000002040c00000002010d0000008085800100000000040e0000000e040f0000000e006500000004660000000104670000000101c9000000808580010000000001ca000000808580010000000001cb000000808580010000000001cc000000808580010000000001cd000000808580010000000001ce0000008085800100000000008196980000829698000083969800008496980000859698000086969800008796980000889698000089969800008a969800008b969800008c96980004018793039c0404028793030204038793030804048793030804058793030804068793030304078793032304088793031c04098793030a040a8793030704658793030804c987930307044d8b93030704b18b93030804358f93030704998f930308041d9393030704819393030804059793030704699793030704b59b930302049d9f93030204bda29303070421a3930308048daa93030704f1aa9303080445b693030704a9b6930307042dba9303070415be93030704fdc193030704811d2c04030401b4c4040304814a5d050404824a5d050104834a5d050404844a5d050204854a5d050404864a5d0502";

    public const string SampleGain = "0c610108018793038005010a000000";
    public const string SampleUse = "0c61000c0187930319880403";
    public const string SampleRefill = "0c6100040a0000000e02";
    public const string SampleMissions = "1c8d030000000500000011150200";
    public const string SampleShop = "8e5600000126870100140000008079425da1010000";

    /// <summary>The membership frame: eight effects with one end (31 October 15:18:46 local, 16 days 20 hours left on the recording day).</summary>
    public static string SampleMembership => "3be3" + "08" + string.Concat(new[] { "22", "23", "25", "26", "27", "28", "29", "2a" }.Select(id => id + "04000000" + "a9a2aef2a0010000" + "a9c65392a1010000"));

    public static bool Run()
    {
        bool ok = true;
        ok &= RunFramesScenario();
        ok &= RunRoutingScenario();
        return ok;
    }

    private static bool RunFramesScenario()
    {
        Console.WriteLine("[selftest] Recap du jour: tickets (login list, changes, which character they belong to, refills; SkeeveTV's frames):");
        ServerTime.UseDatesAsServerTimes(); // the dates below are read as the game's own clock
        try
        {
            byte[] list = Convert.FromHexString(SampleLogin);
            bool parsed = Aion2Tickets.TryParseList(list, out List<TicketReading> entries);
            TicketValue odyle = entries.FirstOrDefault(e => e.Id == Aion2Tickets.OdyleId).Value;
            bool listOk = parsed && entries.Count == 73
                && odyle.Count == 540 && odyle.Bonus == 0
                && entries.First(e => e.Id == 7).Value.Count == 28
                && entries.First(e => e.Id == 60000007).Value.Count == 35
                && entries.First(e => e.Id == 3).Value.TimeMs == 25_200_000
                && entries.First(e => e.Id == 1).Value is { Count: 10, Bonus: 1 };
            bool truncated = !Aion2Tickets.TryParseList(list.AsSpan(0, list.Length - 1), out _);
            Console.WriteLine($"  -> login list: {entries.Count} entries, Odyle {odyle.Count}+{odyle.Bonus}, keys 28, boss kills 35, 7 h access time: {listOk}; a frame cut short is refused: {truncated}");

            bool gain = Aion2Tickets.TryParseUpdate(Convert.FromHexString(SampleGain), out TicketReading g, out int reasonG, out int gained)
                && g.Id == Aion2Tickets.OdyleId && g.Value.Bonus == 640 && g.Value.Count == 0 && reasonG == 1 && gained == 10;
            bool used = Aion2Tickets.TryParseUpdate(Convert.FromHexString(SampleUse), out TicketReading u, out int reasonU, out _)
                && u.Id == Aion2Tickets.OdyleId && u.Value.Count == 25 && u.Value.Bonus == 520 && reasonU == 3;
            bool refill = Aion2Tickets.TryParseUpdate(Convert.FromHexString(SampleRefill), out TicketReading r, out int reasonR, out _) && r.Id == 10 && r.Value.Count == 14 && reasonR == 2;
            Console.WriteLine($"  -> changes: gain with +10={gain}, use={used}, refill={refill}");

            // which character: the login frames between the list and the character record announce the OLD character again
            Aion2Tickets.Reset();
            var t0 = new DateTime(2026, 10, 13, 17, 11, 54);
            Aion2Tickets.NoteCharacter("Chumley", 1304, t0.AddSeconds(-600));
            Aion2Tickets.ReceiveList(entries, t0);
            Aion2Tickets.NoteCharacter("Chumley", 1304, t0.AddSeconds(-600));
            bool notYet = Aion2Tickets.Snapshot().Count == 0;
            Aion2Tickets.ReceiveUpdate(new TicketReading(Aion2Tickets.OdyleId, new TicketValue(500, 0, 0)), t0.AddSeconds(1));
            Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(4));
            TicketCharacter? aahz = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == "Aahz");
            bool bound = aahz is not null && Aion2Tickets.CurrentName == "Aahz" && aahz.Tickets[Aion2Tickets.OdyleId].Count == 500 && aahz.Tickets[7].Count == 28;
            Aion2Tickets.ReceiveUpdate(new TicketReading(7, new TicketValue(27, 0, 0)), t0.AddSeconds(60));
            bool changed = Aion2Tickets.Snapshot().First(c => c.Name == "Aahz").Tickets[7].Count == 27;
            Console.WriteLine($"  -> old announcement takes nothing={notYet}, list bound to the new character with the change that followed it={bound}, later change applied={changed}");

            // refills since the last reading: daily +2 up to 14 (16:00), weekly 35 on Wednesday 16:00
            TicketDef? nightmare = Aion2Tickets.Find(4);
            TicketDef? kills = Aion2Tickets.Find(60000007);
            var seen = new DateTime(2026, 10, 12, 10, 0, 0); // a Monday
            bool refills = nightmare is not null && kills is not null
                && Aion2Tickets.Expected(nightmare, 3, seen, new DateTime(2026, 10, 14, 17, 0, 0)) == 9
                && Aion2Tickets.Expected(nightmare, 13, seen, new DateTime(2026, 10, 14, 17, 0, 0)) == 14
                && Aion2Tickets.Expected(kills, 2, new DateTime(2026, 10, 14, 15, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 35
                && Aion2Tickets.Expected(kills, 2, new DateTime(2026, 10, 14, 17, 0, 0), new DateTime(2026, 10, 15, 17, 0, 0)) == 2
                && Aion2Tickets.NextReset(kills, new DateTime(2026, 10, 14, 17, 0, 0)) == new DateTime(2026, 10, 21, 16, 0, 0)
                && Aion2Tickets.NextReset(nightmare, new DateTime(2026, 10, 14, 17, 0, 0)) == new DateTime(2026, 10, 15, 16, 0, 0);
            TicketDef? dungeon = Aion2Tickets.Find(101);
            bool dungeonRefill = dungeon is not null
                && Aion2Tickets.Expected(dungeon, 3, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 14
                && Aion2Tickets.Expected(dungeon, 5, new DateTime(2026, 10, 7, 17, 0, 0), new DateTime(2026, 10, 14, 15, 0, 0)) == 5
                && Aion2Tickets.Expected(dungeon, 3, new DateTime(2026, 10, 14, 17, 0, 0), new DateTime(2026, 10, 20, 17, 0, 0)) == 3;
            TicketDef? shugo = Aion2Tickets.Find(1);
            TicketDef? awaken = Aion2Tickets.Find(80000001);
            bool rules = shugo is not null && awaken is not null && nightmare is not null
                && Aion2Tickets.Expected(shugo, 6, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 9
                && Aion2Tickets.Expected(shugo, 19, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 21
                && Aion2Tickets.Expected(nightmare, 1, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 3
                && Aion2Tickets.Expected(awaken, 1, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 3
                && Aion2Tickets.Expected(awaken, 1, new DateTime(2026, 10, 14, 17, 0, 0), new DateTime(2026, 10, 20, 17, 0, 0)) == 1;

            // the membership frame: eight effects with one end
            bool membership = Aion2Tickets.TryParseMembership(Convert.FromHexString(SampleMembership), out TicketReading member)
                && member.Id == Aion2Tickets.PremiumId && member.Value.TimeMs > 1_700_000_000_000;
            var withPremium = new TicketCharacter("Member", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue> { [Aion2Tickets.PremiumId] = member.Value }, 45);
            long endMs = member.Value.TimeMs;
            bool premiumKnown = Aion2Tickets.HasPremium(withPremium, DateTimeOffset.FromUnixTimeMilliseconds(endMs - 1000).UtcDateTime) == true
                && Aion2Tickets.HasPremium(withPremium, DateTimeOffset.FromUnixTimeMilliseconds(endMs + 1000).UtcDateTime) == false
                && Aion2Tickets.HasPremium(new TicketCharacter("Old", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue>()), DateTime.UtcNow) is null
                && !Aion2Tickets.TryParseMembership(Convert.FromHexString("3be30800"), out _);
            bool plainCaps = shugo is not null && Aion2Tickets.Expected(shugo, 11, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0), premium: false) == 12;
            Console.WriteLine($"  -> membership frame read (eight effects, one end)={membership}, premium known / over / unknown={premiumKnown}, caps without it (12 keys)={plainCaps}");

            // game time is Korean time: 16:00 there is 07:00 UTC
            ServerTime.Reset();
            bool korean = ServerTime.FromLocal(new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc)) == new DateTime(2026, 10, 14, 16, 0, 0)
                && ServerTime.ToLocal(new DateTime(2026, 10, 14, 16, 0, 0)).ToUniversalTime() == new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc);
            ServerTime.UseDatesAsServerTimes();
            Console.WriteLine($"  -> Shugo +3 / Nightmare +2 daily and Ascension 3 on Wednesdays={rules}, daily dungeon back to 14 every Wednesday={dungeonRefill}, game time is Korean time={korean}");
            Console.WriteLine($"  -> refills (daily up to the cap, weekly set on Wednesday 16:00): {refills}; the table has {Aion2Tickets.Catalog.Count} tickets, French name of Odyle energy: {Aion2Tickets.Find(Aion2Tickets.OdyleId)?.NameIn("fr")}");

            // the hand-ticked part: the weekly counters stop at their caps and clear on Wednesday 16:00
            string tmp = Path.Combine(Path.GetTempPath(), $"recap-{Guid.NewGuid():N}.json");
            ChecklistManual.PathOverride = tmp;
            ChecklistManual.Reset();
            bool ticks, keptUntilReset, dailyKept, weeklyCleared, resetTimes;
            try
            {
                var wed = new DateTime(2026, 10, 14, 17, 0, 0); // a Wednesday after the reset
                ChecklistManual.Step("Aahz", ChecklistManual.Counter.Verteron, 5, wed);
                ChecklistManual.Step("Aahz", ChecklistManual.Counter.Verteron, 10, wed);   // stops at 12
                ChecklistManual.Step("Aahz", ChecklistManual.Counter.Abyss, 3, wed);
                ChecklistManual.Step("Aahz", ChecklistManual.Counter.Abyss, -9, wed);      // stops at 0
                ChecklistManual.State same = ChecklistManual.Get("Aahz", wed.AddHours(1));
                ticks = same.Verteron == 12 && same.Abyss == 0;
                ChecklistManual.Reset(); // read back from the file
                keptUntilReset = ChecklistManual.Get("Aahz", new DateTime(2026, 10, 15, 15, 0, 0)).Verteron == 12;
                dailyKept = ChecklistManual.Get("Aahz", new DateTime(2026, 10, 15, 16, 1, 0)).Verteron == 12;
                ChecklistManual.State weekly = ChecklistManual.Get("Aahz", new DateTime(2026, 10, 21, 16, 1, 0));
                weeklyCleared = weekly.Verteron == 0 && weekly.Abyss == 0;
                resetTimes = ChecklistManual.LastDailyReset(new DateTime(2026, 10, 14, 15, 59, 0)) == new DateTime(2026, 10, 13, 16, 0, 0)
                    && ChecklistManual.LastWeeklyReset(new DateTime(2026, 10, 14, 15, 59, 0)) == new DateTime(2026, 10, 7, 16, 0, 0)
                    && ChecklistManual.LastWeeklyReset(new DateTime(2026, 10, 14, 16, 0, 0)) == new DateTime(2026, 10, 14, 16, 0, 0);
            }
            finally
            {
                ChecklistManual.PathOverride = null;
                ChecklistManual.Reset();
                try { File.Delete(tmp); } catch (IOException) { }
            }

            Console.WriteLine($"  -> hand-ticked part: counters stop at 12 and 0={ticks}, read back from the file={keptUntilReset}, kept over the daily reset={dailyKept}, cleared next Wednesday={weeklyCleared}, reset times={resetTimes}");

            bool missionsRead = Aion2Tickets.TryParseMissions(Convert.FromHexString(SampleMissions), out TicketReading missions)
                && missions.Id == Aion2Tickets.MissionsId && missions.Value.Count == 5 && missions.Value.Bonus == 3
                && Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d090000000500000011150200"), out _) // a first number above 5 is still a head (SkeeveTV 1.3.1)
                && !Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d150000000500000011150200"), out _)
                && !Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d0300"), out _);
            bool shopRead = Aion2Tickets.TryParseShopCounters(Convert.FromHexString(SampleShop), out TicketReading shop)
                && shop.Id == Aion2Tickets.ShopOdyleId && shop.Value.Count == 20 && shop.Value.TimeMs == 1792566000000
                && !Aion2Tickets.TryParseShopCounters(Convert.FromHexString("8e5600000104870100" + "0a0000008079425da1010000"), out _)
                && !Aion2Tickets.TryParseShopCounters(Convert.FromHexString("8e560000"), out _);
            Console.WriteLine($"  -> mission counter from the quest list 0x1C8D: {missionsRead}; Odyle purchase counter 0x8E56: {shopRead}");

            var low = new TicketCharacter("Low", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue>(), 28);
            var high = low with { Level = 45 };
            var unknown = low with { Level = 0 };
            bool locks = Aion2Tickets.IsLocked(low, 101) && Aion2Tickets.IsLocked(low, 4) && Aion2Tickets.IsLocked(low, 80000001) && Aion2Tickets.IsLocked(low, 8)
                && !Aion2Tickets.IsLocked(low, Aion2Tickets.OdyleId) && !Aion2Tickets.IsLocked(low, 1) && !Aion2Tickets.IsLocked(high, 4) && !Aion2Tickets.IsLocked(unknown, 4)
                && Aion2Tickets.UnlockLevel(101) == 30 && Aion2Tickets.UnlockLevel(Aion2Tickets.OdyleId) == 22;
            Console.WriteLine($"  -> counters locked by level (daily dungeon 30, Nightmare/Ascension/Invasion 45, Odyle 22, Shugo 13)={locks}");

            bool ok = listOk && truncated && gain && used && refill && notYet && bound && changed && refills && dungeonRefill && rules && membership && premiumKnown
                && plainCaps && korean && ticks && keptUntilReset && dailyKept && weeklyCleared && resetTimes && missionsRead && shopRead && locks && Aion2Tickets.Catalog.Count == 70;
            Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
            return ok;
        }
        finally
        {
            Aion2Tickets.Reset();
            ServerTime.Reset();
        }
    }

    /// <summary>The five frames go through this fork's decoder by their opcodes.json families and end up on the character named after the list.</summary>
    private static bool RunRoutingScenario()
    {
        Console.WriteLine("[selftest] Recap du jour: frames routed by the opcodes.json families into the tickets:");
        try
        {
            Aion2Protocol protocol = Aion2Protocol.Load();
            bool families = protocol.FamilyOf(0x0B61) == OpcodeFamily.TicketList && protocol.FamilyOf(0x0C61) == OpcodeFamily.TicketChange
                && protocol.FamilyOf(0x8E56) == OpcodeFamily.ShopCounters && protocol.FamilyOf(0x1C8D) == OpcodeFamily.MissionList
                && protocol.FamilyOf(0x3BE3) == OpcodeFamily.Membership;
            Aion2Tickets.Reset();
            var decoder = new Aion2FrameDecoder(protocol, new Aion2EntityDirectory());
            var t0 = new DateTime(2026, 10, 14, 12, 38, 0, DateTimeKind.Local);
            bool silent = true;
            foreach (string hex in new[] { SampleLogin, SampleMembership, SampleShop, SampleMissions, SampleUse })
            {
                silent &= !decoder.Decode(Convert.FromHexString(hex), t0.AddSeconds(1)).Any();
            }

            Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(4), level: 45);
            TicketCharacter? aahz = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == "Aahz");
            bool landed = aahz is not null
                && aahz.Tickets[Aion2Tickets.OdyleId] is { Count: 25, Bonus: 520 }  // the use came after the list and overrides it
                && aahz.Tickets[Aion2Tickets.ShopOdyleId].Count == 20
                && aahz.Tickets[Aion2Tickets.MissionsId].Count == 5
                && aahz.Tickets[Aion2Tickets.PremiumId].TimeMs > 1_700_000_000_000
                && aahz.Tickets[7].Count == 28 && aahz.Level == 45;
            decoder.Decode(Convert.FromHexString(SampleRefill), t0.AddSeconds(30));
            bool later = Aion2Tickets.Snapshot().First(c => c.Name == "Aahz").Tickets[10].Count == 14;
            // The class of a character is kept for the Recap's list (icon, colour, class name): a later record carrying it fills it in.
            Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(40), level: 45, classCode: 21);
            later &= Aion2Tickets.Snapshot().First(c => c.Name == "Aahz").ClassCode == 21 && Aion2.Protocol.Aion2SkillNames.ClassFromCode(21) is not null;
            // A relog: the new login list replaces the counters it holds and keeps the others (the Odyle purchases come only when
            // the shop is opened: a relog wiped Stacy's 20/20, 2026-10-17).
            int shopBefore = Aion2Tickets.Snapshot().First(c => c.Name == "Aahz").Tickets[Aion2Tickets.ShopOdyleId].Count;
            decoder.Decode(Convert.FromHexString(SampleLogin), t0.AddSeconds(45));
            Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(46), level: 45);
            TicketCharacter relogged = Aion2Tickets.Snapshot().First(c => c.Name == "Aahz");
            later &= shopBefore == 20 && relogged.Tickets[Aion2Tickets.ShopOdyleId].Count == 20 && relogged.ClassCode == 21;

            // The meter started while a character was already in game (no login list): the mission handed in still lands, on the
            // character of the last saved record. Head of a real 0x1c8d (2026-10-17 18:48:37 recording, the third hand-in of the day).
            Aion2Tickets.Reset();
            Aion2Tickets.NoteLikelyCharacter("Zed");
            decoder.Decode(Convert.FromHexString("1C8D00000000030000000FA600000090"), t0.AddSeconds(50));
            TicketValue zedMissions = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == "Zed")?.Tickets[Aion2Tickets.MissionsId] ?? default;
            // the count carries the time it was read, so a relog the next day does not pass yesterday's 5/5 for today's
            bool midSession = zedMissions.Count == 3
                && zedMissions.TimeMs == new DateTimeOffset(t0.AddSeconds(50).ToUniversalTime()).ToUnixTimeMilliseconds();
            Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(60));
            midSession &= Aion2Tickets.CurrentName == "Aahz";
            // A count with no time (stored before 2026-10-19) or read before the last daily reset is yesterday's, so the Recap shows ~0; one read since the reset is
            // today's (Stacy, 2026-10-27: a count with no time fell back to the character's last save, refreshed at every login, and stayed 5/5).
            DateTime nowLocal = DateTime.Now;
            DateTime lastReset = ChecklistManual.LastDailyReset(nowLocal);
            long LocalMs(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();
            midSession &= Aion2Tickets.MissionsStale(new TicketValue(5, 7, 0), nowLocal)
                && Aion2Tickets.MissionsStale(new TicketValue(5, 7, LocalMs(lastReset.AddHours(-1))), nowLocal)
                && !Aion2Tickets.MissionsStale(new TicketValue(5, 7, LocalMs(lastReset.AddMinutes(1))), lastReset.AddHours(2));
            // An alt's login list with a mask bit 2 entry (ticket 3: time, then one more u64) and a quest list head whose first number is 7
            // (2026-10-17 18:57:26 recording): both read, so that character is not missing from the Recap.
            bool altList = Aion2Tickets.TryParseList(Convert.FromHexString("0B61490801000000050303000000E8D815010000000080EE360000000000080400000002000600000004070000002004080000000201090000008085800100000000040A0000000E040B00000002040C0000000E010D0000008085800100000000040E0000000E040F0000000E006500000004660000000704670000000701C9000000808580010000000001CA000000808580010000000001CB000000808580010000000001CC000000808580010000000001CD000000808580010000000001CE0000008085800100000000008196980000829698000083969800008496980000859698000086969800008796980000889698000089969800008A969800008B969800008C9698000C0187930319BE0104028793030E04038793030E04048793030E04058793030E04068793031504078793032304088793031C040987930309040A8793030704658793030E04C987930307044D8B93030704B18B93030E04358F93030704998F93030E041D9393030704819393030E04059793030704699793030704B59B930304049D9F93030404BDA29303070421A393030E048DAA93030704F1AA93030E0445B693030704A9B6930307042DBA9303070415BE93030704FDC193030704811D2C04030001B4C40404814A5D050404824A5D050104834A5D050404844A5D050204854A5D050404864A5D0502"), out List<TicketReading> altEntries)
                && altEntries.Count == 73 && altEntries.Any(e => e.Id == 3 && e.Value.TimeMs == 18_209_000);
            bool altMissions = Aion2Tickets.TryParseMissions(Convert.FromHexString("1C8D070000000500000012080400008DDE13"), out TicketReading altHead)
                && altHead.Value.Count == 5;
            Console.WriteLine($"  -> families from opcodes.json={families}, no damage events={silent}, list and changes on the named character={landed}, a later change applied={later}, started mid-session={midSession}, alt list with bit 2={altList}, head 7/5={altMissions}");
            bool ok = families && silent && landed && later && midSession && altList && altMissions;
            Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
            return ok;
        }
        finally
        {
            Aion2Tickets.Reset();
        }
    }
}
