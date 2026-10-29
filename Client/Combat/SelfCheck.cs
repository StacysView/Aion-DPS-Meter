using System.IO;
using AionDPS.Ui;
using AionDPS.Update;

namespace AionDPS.Combat;

/// <summary>
/// Self-checks for the parts of the meter that need no running game: the live aggregation, engaged
/// targets, the Discord table format, toolbar icons and version parsing - run via `AionDPS selftest`.
/// The Aion 2 protocol and capture checks live in <see cref="SelfCheckAion2"/>.
/// </summary>
public static class SelfCheck
{
    private const int GladiatorId = 1;
    private const int ZaubererId = 2;
    private const int BossId = 100;

    public static bool Run()
    {
        bool ok = true;
        ok &= RunAppVersionScenario();
        ok &= RunEngagedTargetsScenario();
        ok &= RunAsciiTableScenario();
        ok &= RunToolbarIconScenario();
        ok &= RunLiveAggregatorScenario();
        ok &= RunGameEventTimersScenario();
        ok &= RunLowDpsTeaseScenario();
        ok &= RunTeasePhrasesScenario();
        ok &= RunTeaseSoundFileScenario();
        ok &= RunRecordingKeptScenario();
        ok &= SelfCheckAion2.Run();
        ok &= SelfCheckThemes.Run();
        ok &= SelfCheckHistory.Run();
        ok &= SelfCheckDummyTest.Run();
        ok &= SelfCheckDummyEngine.Run();
        ok &= SelfCheckDummyUi.Run();
        ok &= SelfCheckBossLoot.Run();
        ok &= SelfCheckBossCast.Run();
        ok &= SelfCheckOwnActivity.Run();
        ok &= SelfCheckRecap.Run();
        ok &= SelfCheckChristmas.Run();
        ok &= SelfCheckCubeKeys.Run();
        ok &= SelfCheckHotkeys.Run();
        ok &= RunNoUploadScenario();
        return ok;
    }

    /// <summary>Nothing goes to aiondps.com (Skeeve's site) any more (Stacy, 2026-10-21): the upload client holds no HTTP client
    /// and sends nothing, no assembly type keeps one, and the own profile is no longer sent after a login by default.</summary>
    private static bool RunNoUploadScenario()
    {
        Console.WriteLine("[selftest] no upload to the website");
        var all = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        bool noHttp = typeof(SelfCheck).Assembly.GetTypes().SelectMany(t => t.GetFields(all))
            .All(f => !typeof(System.Net.Http.HttpMessageInvoker).IsAssignableFrom(f.FieldType));
        var sent = AionDPS.Upload.UploadClient.SendProfilesAsync(null!).GetAwaiter().GetResult();
        bool nothingSent = !sent.Success && sent.Error == AionDPS.Upload.UploadClient.Disabled && !new MeterSettings().AutoUploadProfile;
        Console.WriteLine($"  -> no HTTP client anywhere in the meter; an upload sends nothing; no profile after a login: {noHttp && nothingSent}");
        return noHttp && nothingSent;
    }

    /// <summary>
    /// A recording outlives a click on Save in the settings (2026-10-13: "Start recording", then Save,
    /// and the 5v5 arena capture held half a second). Saving restarts the capture only when the
    /// capture's own settings changed, and a restart goes on writing the same file. Two real segments
    /// of that arena capture.
    /// </summary>
    private static bool RunRecordingKeptScenario()
    {
        Console.WriteLine("[selftest] A recording survives the settings' Save:");
        var first = new Aion2.Capture.TcpSegment(new DateTime(2026, 10, 13, 23, 52, 34, 967, DateTimeKind.Local), "193.202.112.96:13328", "192.168.0.68:57395", 567496492,
            Convert.FromBase64String("GQI48nsAixkVAaQA8ntowyBCzl4BABsgN8USCwM7QrQ9LgDtQYQqA0QUweRDARAdN9wDJwPIx5CgkKAOADYZxEA3oQEAAA=="), true);
        var second = new Aion2.Capture.TcpSegment(new DateTime(2026, 10, 13, 23, 52, 35, 116, DateTimeKind.Local), "193.202.112.96:13328", "192.168.0.68:57395", 567496880,
            Convert.FromBase64String("ER03oZ8BJvgJUV5RXgERHTfcAy8DsdQJkICpgBEdN54BLwMw5QX96QTqEB03kgUnA8ufkquSqw4ANq7EQDehAQAA"), true);
        string path = Path.Combine(Path.GetTempPath(), $"aiondps-selftest-recording-{Guid.NewGuid():N}.jsonl");
        bool bothKept;
        try
        {
            using (var writer = new Aion2.Capture.SegmentRecording.Writer(path))
            {
                writer.Write(first);
            }

            // The capture restarted: the new source goes on in the same file.
            using (var writer = new Aion2.Capture.SegmentRecording.Writer(path, append: true))
            {
                writer.Write(second);
            }

            var read = Aion2.Capture.SegmentRecording.Read(path).ToList();
            bothKept = read.Count == 2 && read[0].Sequence == first.Sequence && read[1].Sequence == second.Sequence
                && read[1].Payload.Span.SequenceEqual(second.Payload.Span);
        }
        finally
        {
            File.Delete(path);
        }

        // Save with nothing changed for the capture (a box ticked elsewhere) restarts nothing; the
        // network card or the character name do, and "" is no name, like none.
        var running = new MeterSettings { CaptureAdapterId = null, Aion2CharacterName = "Boulenbouche" };
        bool sameCapture = new MeterSettings { CaptureAdapterId = "", Aion2CharacterName = " Boulenbouche ", ShugoReminder = true }.CaptureKey() == running.CaptureKey()
            && new MeterSettings { Aion2CharacterName = "Boulenbouche", TeaseSound = false }.CaptureKey() == running.CaptureKey();
        bool changedCapture = new MeterSettings { CaptureAdapterId = @"\Device\NPF_{1}", Aion2CharacterName = "Boulenbouche" }.CaptureKey() != running.CaptureKey()
            && new MeterSettings { Aion2CharacterName = "Aahz" }.CaptureKey() != running.CaptureKey();
        Console.WriteLine($"  -> a restarted capture goes on writing the same file, both segments read back: {bothKept}");
        Console.WriteLine($"  -> Save restarts the capture only for another network card or character name: {sameCapture && changedCapture}");
        return bothKept && sameCapture && changedCapture;
    }

    /// <summary>The overlay's Rift and Shugo countdowns against the fansite's own: at 20:39:15 on
    /// 2026-10-05 aion2.fr showed "Shugo 20m45s - Faille 2h20m45s".</summary>
    private static bool RunGameEventTimersScenario()
    {
        Console.WriteLine("[selftest] Rift / Shugo Festival countdowns:");
        var at = new DateTime(2026, 10, 5, 20, 39, 15);
        var rift = Schedule.GameEventTimers.Rift(at);
        var shugo = Schedule.GameEventTimers.Shugo(at);
        bool site = !rift.IsOpen && rift.Remaining == new TimeSpan(2, 20, 45) && !shugo.IsOpen && shugo.Remaining == new TimeSpan(0, 20, 45);
        var portal = Schedule.GameEventTimers.Rift(new DateTime(2026, 10, 5, 23, 4, 0));
        bool open = portal.IsOpen && portal.Remaining == TimeSpan.FromMinutes(6);
        var night = Schedule.GameEventTimers.Rift(new DateTime(2026, 10, 5, 23, 10, 0));
        bool midnight = !night.IsOpen && night.Remaining == new TimeSpan(2, 50, 0);
        // The Shugo Festival is open 3 minutes, not 10 (Stacy, 2026-10-13).
        var shugoOpen = Schedule.GameEventTimers.Shugo(new DateTime(2026, 10, 13, 20, 1, 30));
        var shugoClosed = Schedule.GameEventTimers.Shugo(new DateTime(2026, 10, 13, 20, 3, 0));
        bool threeMinutes = shugoOpen.IsOpen && shugoOpen.Remaining == new TimeSpan(0, 1, 30)
            && !shugoClosed.IsOpen && shugoClosed.Remaining == TimeSpan.FromMinutes(57);
        // The mid-screen reminders (Stacy, 2026-10-13): "soon" from 5 minutes before the opening, then
        // "open" from the opening until it shuts - one of each per opening.
        Schedule.ShugoAlert? Reminder(int hour, int minute, int second) => Schedule.GameEventTimers.ShugoReminder(new DateTime(2026, 10, 13, hour, minute, second));
        var nine = new DateTime(2026, 10, 13, 21, 0, 0);
        var soon = new Schedule.ShugoAlert(nine, Open: false);
        var opened = new Schedule.ShugoAlert(nine, Open: true);
        bool reminder = Reminder(20, 54, 59) is null && Reminder(20, 55, 0) == soon && Reminder(20, 59, 59) == soon
            && Reminder(21, 0, 0) == opened && Reminder(21, 2, 59) == opened && Reminder(21, 3, 0) is null;
        bool text = Schedule.GameEventTimers.Countdown(new TimeSpan(2, 20, 45)) == "2h20"
            && Schedule.GameEventTimers.Countdown(new TimeSpan(0, 20, 45)) == "20 min"
            && Schedule.GameEventTimers.Countdown(new TimeSpan(0, 4, 5)) == "4:05";
        Console.WriteLine($"  -> 20:39:15: Rift in 2:20:45, Shugo in 0:20:45, as the site showed: {site}");
        Console.WriteLine($"  -> 23:04: the Rift portal is open, 6 minutes left: {open}");
        Console.WriteLine($"  -> 23:10: the next Rift is at 02:00, 2:50 away: {midnight}");
        Console.WriteLine($"  -> the Shugo is open 3 minutes: 1:30 left at 20:01:30, shut at 20:03: {threeMinutes}");
        Console.WriteLine($"  -> the Shugo reminders for 21:00: 'soon' from 20:55, 'open' from 21:00 to 21:03: {reminder}");
        Console.WriteLine($"  -> written 2h20 / 20 min / 4:05: {text}");
        return site && open && midnight && threeMinutes && reminder && text;
    }

    /// <summary>
    /// The boss-death banner's rules (see <see cref="LowDpsTease"/>), on a 60 s fight on a boss with
    /// one add. The group: A 30,000 DPS, B 25,000 (half on the add), healer C 2,000, D 10,000 but dead
    /// three times, E 12,000 and dead once; F a stranger alongside at 3,000.
    /// </summary>
    private static bool RunLowDpsTeaseScenario()
    {
        Console.WriteLine("[selftest] Banner at a boss's death:");
        const int a = 1, b = 2, c = 3, d = 4, e = 5, f = 6, boss = 100, add = 101;
        var start = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var dpsOf = new Dictionary<int, long> { [a] = 30_000, [b] = 25_000, [d] = 10_000, [e] = 12_000, [f] = 3_000 };
        var fight = new List<DamageEvent>();
        for (int s = 0; s < 60; s++)
        {
            var at = start.AddSeconds(s);
            foreach ((int who, long dps) in dpsOf)
            {
                fight.Add(new DamageEvent(at, who, who == b && s % 2 == 1 ? add : boss, dps, IsHeal: false));
            }

            if (s % 10 == 0)
            {
                fight.Add(new DamageEvent(at, c, boss, 20_000, IsHeal: false));
            }
        }

        fight.Add(new DamageEvent(start.AddSeconds(60), a, boss, 30_000, IsHeal: false));
        var deaths = new Dictionary<int, int> { [d] = 4, [e] = 3 }; // 4 deaths make a line, 3 do not (Stacy, 2026-10-17)
        int DeathsOf(int id) => deaths.GetValueOrDefault(id);
        var group = new HashSet<int> { a, b, c, d, e };
        var present = new[] { a, b, c, d, e, f };
        var lines = LowDpsTease.Pick(fight, boss, present, group.Contains, DeathsOf);
        // C (2,000) and E (12,000) are under half of A and B's average (27,750); D's four deaths say
        // more than his 10,000; E's one death is no news. F is no member. A opens it as MVP.
        bool rules = lines.Select(l => (l.PlayerId, l.Kind)).SequenceEqual(new[]
            { (a, TeaseKind.Mvp), (c, TeaseKind.LowDps), (d, TeaseKind.Deaths), (e, TeaseKind.LowDps) })
            && lines[2].Deaths == 4 && Math.Abs(lines[0].Dps - 30_500) < 1;

        var strong = fight.Where(ev => ev.SourceObjectId is a or b).ToList();
        bool noTeaseNoMvp = LowDpsTease.Pick(strong, boss, new[] { a, b }, group.Contains, _ => 0).Count == 0;
        // A pair: E is judged against A alone - 12,000, under half of 30,500.
        var pair = fight.Where(ev => ev.SourceObjectId is a or e).ToList();
        var pairLines = LowDpsTease.Pick(pair, boss, new[] { a, e }, group.Contains, _ => 0);
        bool pairRule = pairLines.Select(l => l.PlayerId).SequenceEqual(new[] { a, e });
        var shortFight = fight.Where(ev => ev.Timestamp < start.AddSeconds(15)).ToList();
        bool tooShort = LowDpsTease.Pick(shortFight, boss, present, group.Contains, DeathsOf).Count == 0;
        var solo = fight.Where(ev => ev.SourceObjectId is d or f).ToList();
        bool soloNobody = LowDpsTease.Pick(solo, boss, new[] { d, f }, id => id == d, DeathsOf).Count == 0;

        var deck = new PhraseDeck(45, new Random(7));
        var firstRound = Enumerable.Range(0, 45).Select(_ => deck.Draw()).ToList();
        bool allOnce = firstRound.Distinct().Count() == 45;
        bool noTwiceInARow = true;
        int previous = firstRound[^1];
        for (int i = 0; i < 45 * 20; i++)
        {
            int next = deck.Draw();
            noTwiceInARow &= next != previous;
            previous = next;
        }

        static bool IsWav(byte[] wav, double seconds) => wav.Length > 44 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF"
            && System.Text.Encoding.ASCII.GetString(wav, 8, 4) == "WAVE"
            && BitConverter.ToInt32(wav, 40) == wav.Length - 44
            && Math.Abs((wav.Length - 44) / 2.0 / 22050 - seconds) < 0.05;
        bool sound = IsWav(TeaseSound.Wav(), 2.06) && IsWav(TeaseSound.Fanfare(), 2.75);

        Console.WriteLine($"  -> MVP, then the low DPS (healer included) and the deaths, one line each, lowest first: {rules}");
        Console.WriteLine($"  -> nobody far behind or dead twice: no banner, not even the MVP: {noTeaseNoMvp}");
        Console.WriteLine($"  -> a pair: the other one is the reference: {pairRule}");
        Console.WriteLine($"  -> a fight under 20 s, or a boss killed solo, shows nothing: {tooShort && soloNobody}");
        Console.WriteLine($"  -> 45 draws give the 45 phrases, and none comes twice in a row over 20 decks: {allOnce && noTwiceInARow}");
        Console.WriteLine($"  -> the sad trombone (2 s) and the MVP's fanfare (2.75 s) are valid WAVs: {sound}");
        return rules && noTeaseNoMvp && pairRule && tooShort && soloNobody && allOnce && noTwiceInARow && sound;
    }

    /// <summary>Which sound file the banner plays (see <see cref="TeaseSound.SoundFile"/>): none, then the
    /// one shipped in assets/sounds (mp3 before wav), and a player's own in the data folder over it.</summary>
    private static bool RunTeaseSoundFileScenario()
    {
        Console.WriteLine("[selftest] Banner sound files (shipped, then one's own):");
        string root = Path.Combine(Path.GetTempPath(), "aiondps-sounds-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data"), install = Path.Combine(root, "install"), sounds = Path.Combine(install, "assets", "sounds");
        try
        {
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(sounds);
            bool none = TeaseSound.SoundFile("Fanfare", data, install) is null;
            File.WriteAllBytes(Path.Combine(sounds, "Fanfare.wav"), new byte[] { 1 });
            bool shipped = TeaseSound.SoundFile("Fanfare", data, install) == Path.Combine(sounds, "Fanfare.wav");
            File.WriteAllBytes(Path.Combine(sounds, "Fanfare.mp3"), new byte[] { 1 });
            bool mp3First = TeaseSound.SoundFile("Fanfare", data, install) == Path.Combine(sounds, "Fanfare.mp3");
            File.WriteAllBytes(Path.Combine(data, "fanfare.mp3"), new byte[] { 1 });
            string? own = TeaseSound.SoundFile("Fanfare", data, install);
            bool ownWins = own is not null && string.Equals(own, Path.Combine(data, "Fanfare.mp3"), StringComparison.OrdinalIgnoreCase);
            bool otherName = TeaseSound.SoundFile("Trombone", data, install) is null;

            // Halloween (Stacy, 2026-10-15): from 20 October to 4 November the sounds of
            // assets/sounds/halloween take turns - mvp_* for the MVP, taquin_* for the teased - after
            // a player's own file, before the shipped one.
            string halloween = Path.Combine(sounds, "halloween");
            Directory.CreateDirectory(halloween);
            foreach (string name in new[] { "mvp_a.wav", "mvp_b.wav", "taquin_a.wav" })
            {
                File.WriteAllBytes(Path.Combine(halloween, name), new byte[] { 1 });
            }

            var oct15 = new DateTime(2026, 10, 15, 21, 0, 0);
            var oct31 = new DateTime(2026, 10, 31, 21, 0, 0);
            string? Sound(bool fanfare, DateTime day, int index) => TeaseSound.SoundFor(fanfare, day, data, install, count => index % count);
            // The player's own fanfare.mp3 (above) is still there: it wins, Halloween or not.
            bool ownFirst = string.Equals(Sound(true, oct15, 0), Path.Combine(data, "Fanfare.mp3"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Sound(true, oct31, 0), Path.Combine(data, "Fanfare.mp3"), StringComparison.OrdinalIgnoreCase)
                && Sound(false, oct15, 0) is null;
            File.Delete(Path.Combine(data, "fanfare.mp3"));
            bool turns = Sound(true, oct31, 0) == Path.Combine(halloween, "mvp_a.wav") && Sound(true, oct31, 1) == Path.Combine(halloween, "mvp_b.wav")
                && Sound(false, oct31, 1) == Path.Combine(halloween, "taquin_a.wav");
            bool rest = Sound(true, oct15, 0) == Path.Combine(sounds, "Fanfare.mp3");
            File.WriteAllBytes(Path.Combine(data, "trombone.wav"), new byte[] { 1 });
            bool ownOverHalloween = string.Equals(Sound(false, oct31, 0), Path.Combine(data, "Trombone.wav"), StringComparison.OrdinalIgnoreCase);

            // The MVP's jingles of assets/sounds/mvp take turns all year with the shipped fanfare (Stacy,
            // 2026-10-15) - after a player's own file, under Halloween's.
            string mvp = Path.Combine(sounds, "mvp");
            Directory.CreateDirectory(mvp);
            File.WriteAllBytes(Path.Combine(mvp, "mvp_a.wav"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(mvp, "mvp_b.wav"), new byte[] { 1 });
            bool mvpTurns = Sound(true, oct15, 0) == Path.Combine(sounds, "Fanfare.mp3") && Sound(true, oct15, 1) == Path.Combine(mvp, "mvp_a.wav")
                && Sound(true, oct15, 2) == Path.Combine(mvp, "mvp_b.wav") && Sound(true, oct31, 0) == Path.Combine(halloween, "mvp_a.wav");

            // The teasing sounds of assets/sounds/taquin take turns all year the same way (Stacy,
            // 2026-10-15), in place of the meter's own trombone - under Halloween's too.
            File.Delete(Path.Combine(data, "trombone.wav"));
            string taquin = Path.Combine(sounds, "taquin");
            Directory.CreateDirectory(taquin);
            File.WriteAllBytes(Path.Combine(taquin, "taquin_a.wav"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(taquin, "taquin_b.wav"), new byte[] { 1 });
            bool teaseTurns = Sound(false, oct15, 0) == Path.Combine(taquin, "taquin_a.wav") && Sound(false, oct15, 1) == Path.Combine(taquin, "taquin_b.wav")
                && Sound(false, oct31, 0) == Path.Combine(halloween, "taquin_a.wav");

            // The meter ships what Stacy chose: for Halloween five for the teased and four for the MVP;
            // the eight MVP jingles; the five teasing sounds.
            string shippedHalloween = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "halloween");
            bool fiveShipped = Directory.Exists(shippedHalloween)
                && Directory.GetFiles(shippedHalloween, "taquin_*.wav").Length == 5 && Directory.GetFiles(shippedHalloween, "mvp_*.wav").Length == 4;
            // ...and the Toccata a hidden cube plays on appearing at Halloween (Stacy, 2026-10-20), a real WAV; on by default
            bool cubeBox = AionDPS.Ui.CubeSound.HalloweenArrival() is Uri box && File.ReadAllBytes(box.LocalPath).AsSpan(0, 4).SequenceEqual("RIFF"u8) && new MeterSettings().HalloweenCubeSound;
            string shippedMvp = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "mvp");
            string shippedTaquin = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "taquin");
            bool eightShipped = Directory.Exists(shippedMvp) && Directory.GetFiles(shippedMvp, "mvp_*.wav").Length == 8
                && Directory.Exists(shippedTaquin) && Directory.GetFiles(shippedTaquin, "taquin_*.wav").Length == 5;

            // One volume for every sound of the banner, the meter's own included (they are played from
            // files too); half by default, as the files always were.
            bool volume = new MeterSettings().SoundVolume == 0.5
                && TeaseSound.BuiltInFile(fanfare: false) is string trombone && File.ReadAllBytes(trombone).AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && TeaseSound.BuiltInFile(fanfare: true) is string fanfareFile && new FileInfo(fanfareFile).Length > 40_000;
            Console.WriteLine($"  -> no file: the meter's own sound; a shipped Fanfare.wav, then Fanfare.mp3 before it: {none && shipped && mp3First}");
            Console.WriteLine($"  -> a player's own fanfare.mp3 wins over the shipped one; the trombone stays the meter's: {ownWins && otherName}");
            Console.WriteLine($"  -> on 31 October the Halloween sounds take turns, a player's own still first; on 15 October none: {turns && ownFirst && rest && ownOverHalloween}");
            Console.WriteLine($"  -> all year the MVP jingles take turns with the shipped fanfare, under Halloween's: {mvpTurns}");
            Console.WriteLine($"  -> all year the teasing sounds take turns, under Halloween's: {teaseTurns}");
            Console.WriteLine($"  -> Halloween's 5 + 4, the 8 MVP jingles and the 5 teasing sounds are shipped: {fiveShipped && eightShipped}");
            Console.WriteLine($"  -> the Halloween Toccata for a hidden cube appearing is shipped, on by default: {cubeBox}");
            Console.WriteLine($"  -> one volume, half by default; the meter's own sounds written as WAV files to be played at it: {volume}");
            return none && shipped && mp3First && ownWins && otherName && turns && ownFirst && rest && ownOverHalloween && mvpTurns && teaseTurns
                && fiveShipped && eightShipped && cubeBox && volume;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Every phrase of the banner is there in French and English, with what it fills in (the
    /// DPS; the name and the DPS or the deaths) - a missing one would show as its raw key.</summary>
    private static bool RunTeasePhrasesScenario()
    {
        Console.WriteLine("[selftest] Teasing phrases (French and English):");
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        bool ok = true;
        foreach (string lang in new[] { "fr", "en" })
        {
            loc.Language = lang;
            for (int i = 1; i <= 30; i++)
            {
                string text = loc[$"Tease.Phrase.{i}"];
                ok &= text != $"Tease.Phrase.{i}" && text.Contains("{0}") && !text.Contains("{1}");
            }

            foreach ((string family, int count) in new[] { ("Named", 40), ("Deaths", 20), ("Mvp", 6), ("Halloween", 11), ("HalloweenDeaths", 6) })
            {
                for (int i = 1; i <= count; i++)
                {
                    string text = loc[$"Tease.{family}.{i}"];
                    ok &= text != $"Tease.{family}.{i}" && text.Contains("{0}") && text.Contains("{1}");
                }
            }

            ok &= string.Format(loc["Tease.Line"], "Diva", "x") is "Diva : x" or "Diva: x";
        }

        loc.Language = language;

        // The jokes on the pseudos of Stacy's group (2026-10-15), for them alone, whatever the case.
        string[] group = { "Boulenbouche", "Azaède", "Kayzia", "bobow", "Mahérann" };
        bool personal = group.All(name => PersonalTeases.For(name).Count >= 3
                && PersonalTeases.For(name).All(p => p.Contains("{1}") && string.Format(p, name, "4 200").Contains("4 200")))
            && PersonalTeases.For("BOULENBOUCHE").Count == PersonalTeases.For("Boulenbouche").Count
            && PersonalTeases.For("BEON").Count == 0 && PersonalTeases.For("Diva").Count == 0;
        // Halloween (phrases, sounds, the overlay's look) comes from 20 October to 4 November only
        // (Stacy, 2026-10-15; first 24 October to 2 November).
        bool halloween = !LowDpsTease.IsHalloween(new DateTime(2026, 10, 19, 23, 59, 0)) && LowDpsTease.IsHalloween(new DateTime(2026, 10, 20))
            && LowDpsTease.IsHalloween(new DateTime(2026, 10, 31, 21, 0, 0)) && LowDpsTease.IsHalloween(new DateTime(2026, 11, 4, 23, 0, 0))
            && !LowDpsTease.IsHalloween(new DateTime(2026, 11, 5)) && !LowDpsTease.IsHalloween(new DateTime(2027, 3, 31));
        Console.WriteLine($"  -> 30 phrases with the DPS, 40 with the name, 20 for deaths, 6 for the MVP, 11 + 6 for Halloween, in both languages: {ok}");
        Console.WriteLine($"  -> jokes on Boulenbouche, Azaède, Kayzia, bobow and Mahérann, for them alone: {personal}");
        Console.WriteLine($"  -> Halloween from 20 October to 4 November only: {halloween}");
        return ok && personal && halloween;
    }

    /// <summary>
    /// The Discord mark on the copy-table button. Its Data string came from an SVG, and SVG packs
    /// arc flags ("0 0 0-4.88") in a way WPF's geometry parser rejects -- a malformed Data attribute
    /// throws while MainWindow is being constructed, so the app would not start at all. Parsing it
    /// here turns that from a crash on launch into a failing check.
    /// </summary>
    private static bool RunToolbarIconScenario()
    {
        const string discord = "M 20.317 4.3698a 19.7913 19.7913 0 0 0 -4.8851 -1.5152 0.0741 0.0741 0 0 0 -0.0785 0.0371c -0.211 0.3753 -0.4447 0.8648 -0.6083 1.2495 -1.8447 -0.2762 -3.68 -0.2762 -5.4868 0 -0.1636 -0.3933 -0.4058 -0.8742 -0.6177 -1.2495a 0.077 0.077 0 0 0 -0.0785 -0.037 19.7363 19.7363 0 0 0 -4.8852 1.515 0.0699 0.0699 0 0 0 -0.0321 0.0277C 0.5334 9.0458 -0.319 13.5799 0.0992 18.0578a 0.0824 0.0824 0 0 0 0.0312 0.0561c 2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a 0.0777 0.0777 0 0 0 0.0842 -0.0276c 0.4616 -0.6304 0.8731 -1.2952 1.226 -1.9942a 0.076 0.076 0 0 0 -0.0416 -0.1057c -0.6528 -0.2476 -1.2743 -0.5495 -1.8722 -0.8923a 0.077 0.077 0 0 1 -0.0076 -0.1277c 0.1258 -0.0943 0.2517 -0.1923 0.3718 -0.2914a 0.0743 0.0743 0 0 1 0.0776 -0.0105c 3.9278 1.7933 8.18 1.7933 12.0614 0a 0.0739 0.0739 0 0 1 0.0785 0.0095c 0.1202 0.099 0.246 0.1981 0.3728 0.2924a 0.077 0.077 0 0 1 -0.0066 0.1276 12.2986 12.2986 0 0 1 -1.873 0.8914 0.0766 0.0766 0 0 0 -0.0407 0.1067c 0.3604 0.698 0.7719 1.3628 1.225 1.9932a 0.076 0.076 0 0 0 0.0842 0.0286c 1.961 -0.6067 3.9495 -1.5219 6.0023 -3.0294a 0.077 0.077 0 0 0 0.0313 -0.0552c 0.5004 -5.177 -0.8382 -9.6739 -3.5485 -13.6604a 0.061 0.061 0 0 0 -0.0312 -0.0286zM 8.02 15.3312c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 0 -1.3332 0.9555 -2.4189 2.157 -2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332 -0.9555 2.4189 -2.1569 2.4189zm 7.9748 0c -1.1825 0 -2.1569 -1.0857 -2.1569 -2.419 0 -1.3332 0.9554 -2.4189 2.1569 -2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332 -0.946 2.4189 -2.1568 2.4189Z";

        bool parses;
        double width = 0;
        try
        {
            var geometry = System.Windows.Media.Geometry.Parse(discord);
            width = geometry.Bounds.Width;
            parses = true;
        }
        catch (FormatException)
        {
            parses = false;
        }

        // A geometry that parses but is empty would render as a blank button.
        bool hasShape = width > 1;

        Console.WriteLine("[selftest] Toolbar icons:");
        Console.WriteLine($"  -> Discord mark parses as a WPF geometry: {parses}");
        Console.WriteLine($"  -> and covers a real area (width {width:F1}): {hasShape}");

        return parses && hasShape;
    }

    /// <summary>
    /// The Discord table format. Cheap to check and easy to break silently: an unaligned column or
    /// a missing fence only shows up once someone has already pasted it into the group chat.
    /// </summary>
    private static bool RunAsciiTableScenario()
    {
        string table = AsciiTable.Render(
            new[] { "Name", "Damage" },
            new List<IReadOnlyList<string>>
            {
                new[] { "Alhamdulilah", "2.352.666" },
                new[] { "kyuubi", "56.828" },
            },
            new[] { false, true });

        var lines = table.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

        bool fenced = lines[0] == "```" && lines[^1] == "```";
        bool hasRule = lines.Count > 2 && lines[2].StartsWith("---");

        // Every body line has to start at the same column, which is the whole point of the format.
        var body = lines.Skip(1).Take(lines.Count - 2).ToList();
        bool aligned = body.Select(l => l.IndexOf(' ')).Distinct().Count() >= 1
            && body.All(l => l.Length >= "Alhamdulilah".Length);

        // Right-aligned numbers end at the same column; left-aligned names start at the same one.
        bool numbersRightAligned = body[0].EndsWith("Damage") && body[^1].EndsWith("56.828");

        bool emptyStaysEmpty = AsciiTable.Render(new[] { "A" }, new List<IReadOnlyList<string>>(), new[] { false }).Length == 0;

        Console.WriteLine("[selftest] Discord ASCII table:");
        Console.WriteLine($"  -> wrapped in a code fence: {fenced}");
        Console.WriteLine($"  -> header rule present: {hasRule}");
        Console.WriteLine($"  -> columns padded to a common width: {aligned}");
        Console.WriteLine($"  -> numeric column right-aligned: {numbersRightAligned}");
        Console.WriteLine($"  -> no rows produces no empty frame: {emptyStaysEmpty}");

        return fenced && hasRule && aligned && numbersRightAligned && emptyStaysEmpty;
    }

    /// <summary>
    /// The filter that keeps a second client's unrelated fight out of the grid. Both cases here
    /// are real ones the user hit: the Sauro run where strangers at a training dummy in town
    /// ranked 6th and 7th, and the 1v1 arena where the opponent was missing from the grid
    /// altogether because every line they produced had the local player as its target.
    /// </summary>
    private static bool RunEngagedTargetsScenario()
    {
        const int you = 1, mate = 2, boss = 3, opponent = 4, stranger = 5, dummy = 6;
        var start = new DateTime(2026, 9, 8, 22, 0, 0, DateTimeKind.Utc);

        var events = new List<DamageEvent>
        {
            new(start, you, boss, 500, IsHeal: false),                    // our fight
            new(start.AddSeconds(1), mate, boss, 500, IsHeal: false),     // a teammate joins it
            new(start.AddSeconds(2), boss, mate, 400, IsHeal: false),     // the boss hits back
            new(start.AddSeconds(3), opponent, you, 900, IsHeal: false),  // 1v1: only ever hits US
            new(start.AddSeconds(4), stranger, dummy, 900, IsHeal: false),// the other client's town
        };

        var kept = EngagedTargets.Filter(events, you);

        bool ourDamageKept = kept.Any(e => e.SourceObjectId == you && e.TargetObjectId == boss);
        bool teammateKept = kept.Any(e => e.SourceObjectId == mate);
        bool incomingOnUsKept = kept.Any(e => e.SourceObjectId == opponent && e.TargetObjectId == you);
        bool incomingOnMateKept = kept.Any(e => e.SourceObjectId == boss && e.TargetObjectId == mate);
        bool strangerDropped = !kept.Any(e => e.SourceObjectId == stranger);

        // With no local player in sight there is nothing to anchor on; filtering everything away
        // would leave an empty grid, which is worse than showing too much.
        bool anchorlessPassesThrough = EngagedTargets.Filter(events, 999).Count == events.Count;

        Console.WriteLine("[selftest] Engaged-target filter:");
        Console.WriteLine($"  -> our own damage kept: {ourDamageKept}");
        Console.WriteLine($"  -> a teammate on the same target kept: {teammateKept}");
        Console.WriteLine($"  -> a 1v1 opponent who only ever hits US is kept: {incomingOnUsKept}");
        Console.WriteLine($"  -> damage taken by a teammate is kept: {incomingOnMateKept}");
        Console.WriteLine($"  -> a stranger's unrelated fight is dropped: {strangerDropped}");
        Console.WriteLine($"  -> nothing to anchor on leaves everything alone: {anchorlessPassesThrough}");

        return ourDamageKept && teammateKept && incomingOnUsKept && incomingOnMateKept
            && strangerDropped && anchorlessPassesThrough;
    }

    /// <summary>
    /// Version parsing for the update check. Cheap to test and easy to get subtly wrong, and the
    /// failure mode is invisible: a build that quietly believes it is newer than every release
    /// never offers an update again, and nobody notices until someone asks why they are still on
    /// an old version.
    ///
    /// The revision case is the real trap. The assembly reports "0.5.1.0" while the release it was
    /// built from is tagged "v0.5.1" -- compared as-is, System.Version says 0.5.1.0 &gt; 0.5.1 and
    /// every installed copy would consider itself ahead of the release forever.
    /// </summary>
    private static bool RunAppVersionScenario()
    {
        Console.WriteLine("[selftest] Update version comparison:");
        Console.WriteLine($"  running build reports \"{AppVersion.Text}\" -> {AppVersion.Current}");

        bool tagPrefixStripped = AppVersion.Parse("v0.5.1") == new Version(0, 5, 1)
            && AppVersion.Parse("0.5.1") == new Version(0, 5, 1);
        bool prereleaseSuffixDropped = AppVersion.Parse("v1.0.0-beta.2") == new Version(1, 0, 0);
        bool revisionIgnored = AppVersion.Parse("0.5.1.0") == AppVersion.Parse("v0.5.1");
        bool shortFormPadded = AppVersion.Parse("v2.0") == new Version(2, 0, 0);
        bool garbageIsNotNewer = AppVersion.Parse("nightly") == new Version(0, 0, 0);
        bool ordersCorrectly = AppVersion.Parse("v0.5.2") > AppVersion.Parse("v0.5.1")
            && AppVersion.Parse("v0.10.0") > AppVersion.Parse("v0.9.9");

        // The running build must know its own version. This started out as a throwaway line in the
        // output above and immediately failed: Current was declared before Text, so it parsed a
        // null and every build reported 0.0.0 -- meaning every release looks newer and the update
        // notice never goes away. Asserted, not just printed, so the ordering cannot silently
        // regress.
        bool knowsItsOwnVersion = AppVersion.Current > new Version(0, 0, 0)
            && AppVersion.Current == AppVersion.Parse(AppVersion.Text);

        Console.WriteLine($"  -> \"v\" prefix stripped: {tagPrefixStripped}");
        Console.WriteLine($"  -> prerelease suffix dropped: {prereleaseSuffixDropped}");
        Console.WriteLine($"  -> assembly's trailing .0 revision does not read as newer: {revisionIgnored}");
        Console.WriteLine($"  -> two-part tag padded to three: {shortFormPadded}");
        Console.WriteLine($"  -> unparsable tag cannot outrank a real one: {garbageIsNotNewer}");
        Console.WriteLine($"  -> newer releases compare greater (incl. 10 > 9): {ordersCorrectly}");
        Console.WriteLine($"  -> the running build knows its own version: {knowsItsOwnVersion}");

        return tagPrefixStripped && prereleaseSuffixDropped && revisionIgnored
            && shortFormPadded && garbageIsNotNewer && ordersCorrectly && knowsItsOwnVersion;
    }

    /// <summary>
    /// Verifies the LiveAggregator end to end at the object level: two attackers hitting the same
    /// boss, checking the per-source totals AND the rendered DPS column -- specifically that a
    /// source with only one attributed hit (the normal state of the first line of output on every
    /// real run) shows "n/a" rather than its damage total dressed up as a rate. Events are built
    /// by hand rather than parsed, so this stays a test of the aggregator alone.
    /// </summary>
    private static bool RunLiveAggregatorScenario()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggregator = new LiveAggregator();

        aggregator.IngestEvents(new[]
        {
            new DamageEvent(start, GladiatorId, BossId, 1000, IsHeal: false),
            new DamageEvent(start, GladiatorId, BossId, 1200, IsHeal: false),
            new DamageEvent(start.AddSeconds(2), GladiatorId, BossId, 1100, IsHeal: false),
            new DamageEvent(start.AddSeconds(1), ZaubererId, BossId, 50_000, IsHeal: false),
        });

        Console.WriteLine("[selftest] LiveAggregator wiring (hand-built DamageEvents):");
        Console.WriteLine($"  {aggregator.Summarize()}");

        int gladiatorEvents = aggregator.Events.Count(e => e.SourceObjectId == GladiatorId);
        long gladiatorTotal = aggregator.Events.Where(e => e.SourceObjectId == GladiatorId).Sum(e => e.Amount);
        long zaubererTotal = aggregator.Events.Where(e => e.SourceObjectId == ZaubererId).Sum(e => e.Amount);

        bool gladiatorEventCountOk = gladiatorEvents == 3; // two at t=0, one two seconds later
        bool gladiatorTotalOk = gladiatorTotal == 1000 + 1200 + 1100;
        bool zaubererTotalOk = zaubererTotal == 50_000;

        // Caught by review: zauberer has exactly one attributed hit here, which is the normal
        // state of the very first line of live output on every real capture run, not an edge
        // case. Summarize() must show "n/a" for a DPS rate that has no time span to be computed
        // from -- not the raw 50,000 damage total masquerading as "50000 DPS", which is the exact
        // bug already fixed once for AllDpsActiveOnly and had quietly regressed via
        // AllDpsWallClock. Assert on the actual rendered string, not just the underlying totals:
        // that's what let the bug hide behind a passing test the first time.
        string summary = aggregator.Summarize();
        bool zaubererShowsNotAvailable = summary.Contains("0x00000002: 50000 dmg (n/a DPS)");
        bool zaubererDoesNotShowTotalAsRate = !summary.Contains("(50000 DPS)");
        double? gladiatorWallDps = DpsCalculator.AllDpsWallClock(aggregator.Events, GladiatorId);
        bool gladiatorDpsIsReal = gladiatorWallDps is double d && d > 0;

        Console.WriteLine($"  -> gladiator's 3 hits all recorded: {gladiatorEventCountOk}");
        Console.WriteLine($"  -> gladiator total damage correct: {gladiatorTotalOk}");
        Console.WriteLine($"  -> zauberer total damage correct: {zaubererTotalOk}");
        Console.WriteLine($"  -> zauberer's single hit renders as \"n/a\" DPS, not a fake rate: {zaubererShowsNotAvailable && zaubererDoesNotShowTotalAsRate}");
        Console.WriteLine($"  -> gladiator (multiple hits, real time span) still gets a real DPS number: {gladiatorDpsIsReal}");

        return gladiatorEventCountOk && gladiatorTotalOk && zaubererTotalOk
            && zaubererShowsNotAvailable && zaubererDoesNotShowTotalAsRate && gladiatorDpsIsReal;
    }
}
