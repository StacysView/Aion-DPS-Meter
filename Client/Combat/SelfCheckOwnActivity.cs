using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// Self-checks for the frames the timed dummy test reads (layouts by SkeeveTV, SkeeveAN/Aion-DPS-Meter, MIT: docs/protocol/hit-kinds.md, skill-frames.md,
/// combat-frames.md): the hit kinds of a damage frame, the local player's skill uses and refusals, skill entries that update one level, the stat table,
/// the effects on the player, and the log of which opcodes arrived. The frames are built by hand from his documented examples (his example bytes where
/// the docs give them); no real recording of these frames exists in this repository yet. Run from SelfCheck.Run().
/// </summary>
public static class SelfCheckOwnActivity
{
    // The local player of these frames is entity 3279 (varint CF 19), as in the Urugugu capture of the other self-checks.
    private const int Self = 3279;
    private const string SelfId = "CF19";
    private const string OtherId = "A40E";
    private const int KeenStrike = 0x00a826fe; // fe26a800
    private const int RendingBlow = 11010340;
    private const int RendingBlowStone = 11010350;
    private const int IdentifyWeakness = 11740000;

    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    public static bool Run()
    {
        Console.WriteLine("[selftest] Dummy test frames (hit kinds, own skill uses, skill entries, stats, effects, opcode log):");
        bool ok = true;
        ok &= RunHitKinds();
        ok &= RunSkillUses();
        ok &= RunSkillEntries();
        ok &= RunStats();
        ok &= RunEffects();
        ok &= RunOpcodeLog();
        ok &= RunBuffers();
        return ok;
    }

    private static Aion2FrameDecoder NewDecoder(out Aion2EntityDirectory directory)
    {
        directory = new Aion2EntityDirectory { LocalPlayerId = Self };
        return new Aion2FrameDecoder(Aion2Protocol.Load(), directory);
    }

    // A hit of Keen Strike for 381 (the first frame of the real 2026-09-30 capture, see SelfCheckAion2): target | flags | 00 | actor | skill | follow | type | bits | 00 | direction | 8 bytes.
    private static string Hit(string flags, string type, string bits) =>
        "0438 848403 " + flags + " 00 be08 fe26a800 4f " + type + " " + bits + " 00 02 433baf41 01000000 ba4f fd02 0100";

    private static bool RunHitKinds()
    {
        var decoder = NewDecoder(out _);
        DamageEvent One(string hex) => decoder.Decode(Hex(hex), T0).Single();

        // Art 4 (flags 04): a plain hit, 8 bytes after the type and nothing more.
        DamageEvent plain4 = One("0438 848403 04 00 be08 fe26a800 4f 02 433baf41 01000000 ba4f fd02 0100");
        DamageEvent plain = One(Hit("06", "02", "00"));
        DamageEvent crit = One(Hit("26", "03", "00"));
        DamageEvent dbl = One(Hit("06", "02", "08"));
        DamageEvent perfect = One(Hit("06", "02", "04"));
        DamageEvent parry = One(Hit("06", "02", "02"));
        DamageEvent mixed = One(Hit("06", "03", "0c"));
        DamageEvent unknownBits = One(Hit("06", "02", "20"));

        bool numbers = new[] { plain4, plain, crit, dbl, perfect, parry, mixed, unknownBits }.All(e => e.Amount == 381 && e.SkillId == KeenStrike && !e.IsHeal);
        bool none = plain4.Kind == HitKind.None && plain.Kind == HitKind.None && !plain.IsCritical && unknownBits.Kind == HitKind.None;
        bool kinds = crit.Kind == HitKind.Crit && crit.IsCritical
            && dbl.Kind == HitKind.Double && !dbl.IsCritical
            && perfect.Kind == HitKind.Perfect
            && parry.Kind == HitKind.Parry
            && mixed.Kind == (HitKind.Crit | HitKind.Double | HitKind.Perfect) && mixed.IsCritical;
        return Report("a hit of 381 keeps its number and crit flag; plain, crit, double, perfect, parry, a mix and unknown bits read as their kinds", numbers && none && kinds);
    }

    private static bool RunSkillUses()
    {
        var decoder = NewDecoder(out var directory);
        // SkeeveTV's examples: 0138 00 00 00 | skill = accepted; 0138 00 cc 18 | skill = refused with code cc (0x13 "in another skill" is the commonest).
        decoder.Decode(Hex("0138 000000 2401a800"), T0).ToArray();
        decoder.Decode(Hex("0138 001318 2401a800"), T0.AddMilliseconds(50)).ToArray();
        decoder.Decode(Hex("0138 000005 2401a800"), T0.AddMilliseconds(60)).ToArray(); // not a known shape
        decoder.Decode(Hex("0238 " + SelfId + " 00 2401a800 0b 02 cb8602"), T0.AddMilliseconds(5)).ToArray();
        decoder.Decode(Hex("0238 " + OtherId + " 00 2e01a800 04 02 cb8602"), T0.AddMilliseconds(6)).ToArray(); // somebody else's cast

        var uses = directory.Own.UsesSince(T0);
        bool accepted = uses.Count == 2 && uses[0] == new SkillUse(T0, RendingBlow, true, 0);
        bool refused = uses.Count == 2 && uses[1] == new SkillUse(T0.AddMilliseconds(50), RendingBlow, false, 0x13);
        bool since = directory.Own.UsesSince(T0.AddMilliseconds(10)).Count == 1;
        bool cast = directory.Own.LastCast == new OwnCast(T0.AddMilliseconds(5), RendingBlow, 33611);
        return Report("an accepted and a refused (code 0x13) use of Rending Blow, an odd receipt ignored, the cast and its target kept for the player only", accepted && refused && since && cast);
    }

    private static bool RunSkillEntries()
    {
        var decoder = NewDecoder(out var directory);
        directory.SetLocalSkills(new[] { new Aion2SkillEntry(11010000, 12, 10), new Aion2SkillEntry(IdentifyWeakness, 17, 10) });
        int changed = 0;
        directory.CharacterChanged += _ => changed++;
        directory.SetLocalCharacter(new Aion2CharacterInfo(Self, "Aahz", 6, 33, Array.Empty<Aion2EquippedItem>(), T0));
        changed = 0;
        bool before = directory.LastSkillFrame is null;

        // SkeeveTV's login example: Identify Weakness 11740000, total 18, base 10 (4+2+2 bonuses) - the level went up from 17.
        DateTime at = T0.AddSeconds(3);
        decoder.Decode(Hex("1338 01 6023b300 01 6023b300 12 0a 04 00 02 02 00 0a"), at).ToArray();
        var list = directory.LocalSkills.ToDictionary(s => s.SkillId);
        bool updated = list.Count == 2 && list[IdentifyWeakness] == new Aion2SkillEntry(IdentifyWeakness, 18, 10) && list[11010000] == new Aion2SkillEntry(11010000, 12, 10);
        bool frame = directory.LastSkillFrame == new Aion2SkillFrame(at, 0x1338, 2) && before;
        bool notified = changed == 1;
        decoder.Decode(Hex("1338 01 6023b300 01 6023b300 12 0a 04 00 02 02 00 0a"), at.AddSeconds(1)).ToArray(); // the same again
        bool quiet = changed == 1 && directory.LastSkillFrame is { At: var again } && again == at.AddSeconds(1);

        // A variant (0f38: Rending Blow with another stone, total 17, base 10) is kept apart from the list the window and the profile use.
        decoder.Decode(Hex("0f38 01 2e01a800 11 0a 04 00 01 02 00 06"), at.AddSeconds(2)).ToArray();
        bool variant = directory.LocalSkillVariants.Count == 1 && directory.LocalSkillVariants[0] == new Aion2SkillEntry(RendingBlowStone, 17, 10)
            && directory.LocalSkills.Count == 2 && directory.LocalSkillLevels.Count == 3 && directory.LastSkillFrame is { Opcode: 0x0f38, Count: 3 };
        // the preset swap: 1038 takes the variant away
        decoder.Decode(Hex("1038 2e01a800"), at.AddSeconds(3)).ToArray();
        bool removed = directory.LocalSkillVariants.Count == 0 && directory.LocalSkillLevels.Count == 2 && directory.LastSkillFrame is { Opcode: 0x1038 };
        // too short, or a level that cannot be: nothing changes
        decoder.Decode(Hex("1338 01 6023b300 01 6023b300 12"), at.AddSeconds(4)).ToArray();
        decoder.Decode(Hex("1338 01 6023b300 01 6023b300 00 0a 04 00 02 02 00 0a"), at.AddSeconds(4)).ToArray();
        bool guarded = directory.LocalSkills.Single(s => s.SkillId == IdentifyWeakness).Level == 18 && directory.LastSkillFrame is { Opcode: 0x1038 };
        // the saved character still carries the base list only
        bool saved = directory.ToSaved() is { } snapshot && snapshot.Skills.Count == 2;

        return Report("a skill entry frame updates one level and leaves the list (and the save) alone otherwise; a variant is kept apart and removed again; the last frame's time and opcode are known",
            updated && frame && notified && quiet && variant && removed && guarded && saved);
    }

    private static bool RunStats()
    {
        var decoder = NewDecoder(out var directory);
        // The whole table (stat 0x13 = 239, 0x34 = 2488), then an update of one value (0x13 to 274, as SkeeveTV saw a Speed Scroll do), a heartbeat, another entity's table.
        decoder.Decode(Hex("4a36 " + SelfId + " 02 1300 ef000000 3400 b8090000 0000000000000000"), T0).ToArray();
        StatSnapshot? first = directory.LocalStats;
        decoder.Decode(Hex("4a36 " + SelfId + " 01 1300 12010000 0000000000000000"), T0.AddSeconds(5)).ToArray();
        StatSnapshot? merged = directory.LocalStats;
        decoder.Decode(Hex("4a36 " + SelfId + " 00 0000000000000000"), T0.AddSeconds(6)).ToArray();
        bool heartbeat = ReferenceEquals(directory.LocalStats, merged);
        decoder.Decode(Hex("4a36 " + OtherId + " 01 1300 01000000 0000000000000000"), T0.AddSeconds(7)).ToArray();
        decoder.Decode(Hex("4a36 " + SelfId + " 02 1300 01000000 0000000000000000"), T0.AddSeconds(8)).ToArray(); // one entry short of its count: ignored
        bool untouched = ReferenceEquals(directory.LocalStats, merged);

        bool table = first is { Values.Count: 2 } && first.Values[0x13] == 239 && first.Values[0x34] == 2488 && first.At == T0;
        bool update = merged is { Values.Count: 2 } && merged.Values[0x13] == 274 && merged.Values[0x34] == 2488 && merged.At == T0.AddSeconds(5)
            && first!.Values[0x13] == 239; // the earlier snapshot is not edited
        return Report("the stat table is read, an update merges one value into it, a heartbeat, a short frame and another entity's table change nothing", table && update && heartbeat && untouched);
    }

    private static bool RunEffects()
    {
        var decoder = NewDecoder(out var directory);
        string tail = "0000000000000000 " + SelfId + " 00 000000000000000000000000";
        // 2a38: target | 01 | type | instance 0x37 | effect 200 | 300 ms | stays-on 0 | 8 bytes | caster | ... (layout by SkeeveTV; the first example of combat-frames.md).
        decoder.Decode(Hex("2a38 " + SelfId + " 01 00 37 c8000000 2c010000 00000000 " + tail), T0).ToArray();
        decoder.Decode(Hex("2b38 " + SelfId + " 00 37 c8000000 2c010000 00000000 " + tail), T0.AddSeconds(1)).ToArray();      // renewed, known: no change
        decoder.Decode(Hex("2b38 " + SelfId + " 00 26 2c010000 2c010000 00000000 " + tail), T0.AddSeconds(2)).ToArray();      // renewed, first sight of effect 300
        decoder.Decode(Hex("2a38 " + OtherId + " 01 00 05 c8000000 2c010000 00000000 " + tail), T0.AddSeconds(2)).ToArray();  // on somebody else
        decoder.Decode(Hex("2c38 " + SelfId + " 01 00 37 01"), T0.AddSeconds(3)).ToArray();                                  // instance 0x37 ended
        decoder.Decode(Hex("2c38 " + SelfId + " 01 00 55 01"), T0.AddSeconds(3)).ToArray();                                  // one never seen: dropped
        decoder.Decode(Hex("2c38 " + OtherId + " 01 00 26 01"), T0.AddSeconds(3)).ToArray();                                 // somebody else's end

        var changes = directory.Own.EffectsSince(T0);
        bool list = changes.Count == 3 && changes[0] == new EffectChange(T0, 200, true) && changes[1] == new EffectChange(T0.AddSeconds(2), 300, true)
            && changes[2] == new EffectChange(T0.AddSeconds(3), 200, false);
        bool active = directory.Own.ActiveEffectIds().SequenceEqual(new[] { 300 });
        return Report("effects on the player: on, a renewal that is no change, first sight by renewal, the end by instance; others' effects and ends ignored", list && active);
    }

    private static bool RunOpcodeLog()
    {
        var decoder = NewDecoder(out var directory);
        // Before the moment asked about: a skill entry and a hit.
        decoder.Decode(Hex("1338 01 6023b300 01 6023b300 12 0a 04 00 02 02 00 0a"), T0).ToArray();
        decoder.Decode(Hex(Hit("06", "02", "00")), T0).ToArray();
        DateTime since = T0.AddSeconds(10);
        // After it: a variant entry, and hits - two in one burst, one 1.8 s later.
        decoder.Decode(Hex("0f38 01 2e01a800 11 0a 04 00 01 02 00 06"), T0.AddSeconds(11)).ToArray();
        decoder.Decode(Hex(Hit("06", "02", "00")), T0.AddSeconds(11)).ToArray();
        decoder.Decode(Hex(Hit("06", "02", "00")), T0.AddSeconds(11.2)).ToArray();
        decoder.Decode(Hex(Hit("06", "02", "00")), T0.AddSeconds(13)).ToArray();

        var seen = directory.Own.OpcodesSince(since).ToDictionary(s => s.Opcode);
        bool skill = seen.TryGetValue(0x0f38, out var variant) && variant is { Bursts: 1, SeenBefore: false };
        bool hits = seen.TryGetValue(0x0438, out var hit) && hit is { Bursts: 2, SeenBefore: true };
        bool earlier = !seen.ContainsKey(0x1338);
        bool all = directory.Own.OpcodesSince(T0).Count == 3 && directory.Own.OpcodesSince(T0.AddDays(1)).Count == 0;
        return Report("the opcode log tells what arrived since a moment: the new skill frame (never seen before), the hits in two bursts, not the earlier entry", skill && hits && earlier && all);
    }

    private static bool RunBuffers()
    {
        var own = new Aion2OwnActivity();
        for (int i = 0; i < 10_000; i++)
        {
            own.NoteUse(T0.AddMilliseconds(i), RendingBlow, accepted: true, rejectCode: 0);
            own.NoteEffectOn(T0.AddMilliseconds(i), i, 200 + i % 7, renewed: false);
        }

        var uses = own.UsesSince(DateTime.MinValue);
        bool bounded = uses.Count is > 0 and <= 8192 && uses[^1].At == T0.AddMilliseconds(9_999) && own.EffectsSince(DateTime.MinValue).Count <= 8192;

        // The capture thread writes while the UI reads.
        var live = new Aion2OwnActivity();
        bool reads = true;
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 20_000; i++)
            {
                live.NoteUse(T0.AddMilliseconds(i), RendingBlow, i % 3 != 0, 0x13);
                live.ObserveOpcode(0x0438 + i % 5, T0.AddMilliseconds(i * 7));
            }
        });
        for (int i = 0; i < 200; i++)
        {
            try
            {
                reads &= live.UsesSince(T0).Count <= 8192 && live.OpcodesSince(T0).Count <= 5;
            }
            catch (Exception)
            {
                reads = false;
            }
        }

        writer.Wait();
        return Report("the buffers stay bounded (newest kept), and a reader thread sees consistent copies while another writes", bounded && reads);
    }

    private static bool Report(string label, bool ok)
    {
        Console.WriteLine($"  -> {label}: {ok}");
        return ok;
    }
}
