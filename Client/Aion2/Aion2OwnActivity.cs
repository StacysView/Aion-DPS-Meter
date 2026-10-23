using AionDPS.Combat;

namespace AionDPS.Aion2;

/// <summary>An opcode that arrived since a moment (see <see cref="Aion2OwnActivity.OpcodesSince"/>). A "burst" is a run of frames of one
/// opcode with less than a second between them, so a frame sent fifty times a second is one burst and one sent now and then is many.
/// <see cref="SeenBefore"/> is false for an opcode that had never arrived before the moment asked about - the interesting ones.</summary>
public readonly record struct OpcodeSighting(int Opcode, int Bursts, DateTime FirstAt, DateTime LastAt, bool SeenBefore);

/// <summary>The local player's last cast (frame 0x0238): the skill and what it was aimed at. Times are capture time.</summary>
public readonly record struct OwnCast(DateTime At, int SkillId, int TargetId);

/// <summary>
/// What the local player did, as the game's own frames say it, for the timed dummy test: skill uses accepted or refused (frame
/// 0x0138), effects put on or taken off the player (0x2A38 / 0x2B38 / 0x2C38), the last cast (0x0238), and a cheap record of which
/// opcodes arrived when. Layouts are SkeeveTV's (SkeeveAN/Aion-DPS-Meter, MIT: docs/protocol/skill-frames.md and combat-frames.md).
/// <para>Every time is capture time, the packet timestamps, the same clock as <see cref="DamageEvent.Timestamp"/>. The capture thread
/// writes, the UI thread reads; each list is bounded and a reader gets a copy of what it asked for.</para>
/// </summary>
public sealed class Aion2OwnActivity
{
    private const int MaxEntries = 8192;
    private const int MaxActiveEffects = 1024;
    private const int MaxBursts = 4096;
    private static readonly long BurstGapTicks = TimeSpan.FromSeconds(1).Ticks;

    private readonly object _gate = new();
    private readonly List<SkillUse> _uses = new();
    private readonly List<EffectChange> _effects = new();

    // Instance id -> effect id of the effects now on the player: an "ended" frame names only the instance.
    private readonly Dictionary<int, int> _active = new();
    private OwnCast? _lastCast;

    // Opcode diagnostic. The two tables hold ticks per opcode and are written by the capture thread alone (one array write per frame,
    // no lock); a new burst, which is rare, is also logged under the lock. Allocated by the first frame: 64 K longs each.
    private volatile long[]? _firstTicks;
    private volatile long[]? _lastTicks;
    private readonly List<(int Opcode, long Ticks)> _bursts = new();

    /// <summary>A skill use acknowledged by the game: accepted, or refused with its code.</summary>
    public void NoteUse(DateTime at, int skillId, bool accepted, int rejectCode)
    {
        lock (_gate)
        {
            _uses.Add(new SkillUse(at, skillId, accepted, accepted ? 0 : rejectCode));
            Trim(_uses);
        }
    }

    /// <summary>The local player's cast (frame 0x0238). Not a use: the use is the game's acknowledgement (0x0138), this adds what was aimed at.</summary>
    public void NoteCast(DateTime at, int skillId, int targetId)
    {
        lock (_gate)
        {
            _lastCast = new OwnCast(at, skillId, targetId);
        }
    }

    /// <summary>An effect put on the local player (0x2A38 "on"; also the first sight of one a renewal (0x2B38) shows, see <paramref name="renewed"/>).
    /// A renewal of an effect already known is not a change.</summary>
    public void NoteEffectOn(DateTime at, int instance, int effectId, bool renewed)
    {
        lock (_gate)
        {
            bool known = _active.ContainsKey(instance);
            if (_active.Count >= MaxActiveEffects && !known)
            {
                // Ends that never came; start the table again rather than grow it.
                _active.Clear();
            }

            _active[instance] = effectId;
            if (renewed && known)
            {
                return;
            }

            _effects.Add(new EffectChange(at, effectId, On: true));
            Trim(_effects);
        }
    }

    /// <summary>An effect taken off the local player, by its instance. One never seen put on (it was there before the meter started) cannot be named and is dropped.</summary>
    public void NoteEffectEnd(DateTime at, int instance)
    {
        lock (_gate)
        {
            if (_active.Remove(instance, out int effectId))
            {
                _effects.Add(new EffectChange(at, effectId, On: false));
                Trim(_effects);
            }
        }
    }

    private static void Trim<T>(List<T> list)
    {
        if (list.Count > MaxEntries)
        {
            list.RemoveRange(0, list.Count - MaxEntries * 3 / 4);
        }
    }

    /// <summary>The local player's last cast, or null before one.</summary>
    public OwnCast? LastCast
    {
        get
        {
            lock (_gate)
            {
                return _lastCast;
            }
        }
    }

    /// <summary>Every skill use (accepted or refused) at or after <paramref name="since"/>, oldest first (the newest <c>8192</c> are kept).</summary>
    public IReadOnlyList<SkillUse> UsesSince(DateTime since)
    {
        lock (_gate)
        {
            return _uses.Where(u => u.At >= since).ToList();
        }
    }

    /// <summary>Every effect change on the local player at or after <paramref name="since"/>, oldest first.</summary>
    public IReadOnlyList<EffectChange> EffectsSince(DateTime since)
    {
        lock (_gate)
        {
            return _effects.Where(e => e.At >= since).ToList();
        }
    }

    /// <summary>The effect ids on the local player now (those seen put on and not yet seen ended), each once.</summary>
    public IReadOnlyList<int> ActiveEffectIds()
    {
        lock (_gate)
        {
            return _active.Values.Distinct().Order().ToList();
        }
    }

    /// <summary>Forgets the effects on the player, as when a new character record says the player was placed afresh. The lists of uses and changes stay.</summary>
    public void ClearActiveEffects()
    {
        lock (_gate)
        {
            _active.Clear();
        }
    }

    /// <summary>Notes that a frame of this opcode arrived. Called for every frame, so it only does array writes unless a burst begins (the opcode
    /// was quiet for a second): that one is logged. Capture thread only.</summary>
    public void ObserveOpcode(int opcode, DateTime at)
    {
        if ((uint)opcode > 0xFFFF)
        {
            return;
        }

        long[]? last = _lastTicks;
        if (last is null)
        {
            lock (_gate)
            {
                _firstTicks ??= new long[65536];
                _lastTicks ??= new long[65536];
                last = _lastTicks;
            }
        }

        long ticks = at.Ticks;
        long before = last[opcode];
        last[opcode] = ticks;
        if (before != 0 && ticks - before <= BurstGapTicks)
        {
            return;
        }

        lock (_gate)
        {
            if (_firstTicks![opcode] == 0)
            {
                _firstTicks[opcode] = ticks;
            }

            _bursts.Add((opcode, ticks));
            if (_bursts.Count > MaxBursts)
            {
                _bursts.RemoveRange(0, MaxBursts / 4);
            }
        }
    }

    /// <summary>
    /// The opcodes that arrived at or after <paramref name="since"/>, with how many bursts and when. The question it answers: the player opens the
    /// skills tab in game - does a skill frame come? Ask with the moment of opening and look for the opcodes with
    /// <see cref="OpcodeSighting.SeenBefore"/> false, or for the skill ones (0x1338, 0x0F38, 0x1038, the login list 0x0051) among the rest. Only the
    /// newest 4096 bursts are kept, so ask within minutes, not hours. Oldest first-seen first.
    /// </summary>
    public IReadOnlyList<OpcodeSighting> OpcodesSince(DateTime since)
    {
        long[]? last = _lastTicks;
        if (last is null)
        {
            return Array.Empty<OpcodeSighting>();
        }

        lock (_gate)
        {
            long from = since.Ticks;
            var seen = new Dictionary<int, (int Bursts, long First)>();
            foreach ((int opcode, long ticks) in _bursts)
            {
                if (ticks < from)
                {
                    continue;
                }

                seen[opcode] = seen.TryGetValue(opcode, out var known) ? (known.Bursts + 1, Math.Min(known.First, ticks)) : (1, ticks);
            }

            // An opcode already running at the moment asked about has its burst before it; its later frames still count as seen.
            for (int opcode = 0; opcode < last.Length; opcode++)
            {
                if (last[opcode] >= from && !seen.ContainsKey(opcode))
                {
                    seen[opcode] = (0, last[opcode]);
                }
            }

            return seen
                .Select(kv => new OpcodeSighting(kv.Key, kv.Value.Bursts, new DateTime(kv.Value.First, DateTimeKind.Unspecified), new DateTime(last[kv.Key], DateTimeKind.Unspecified),
                    _firstTicks![kv.Key] != 0 && _firstTicks[kv.Key] < from))
                .OrderBy(s => s.FirstAt)
                .ToList();
        }
    }
}
