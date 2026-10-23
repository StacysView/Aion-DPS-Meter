using AionDPS.Combat;

namespace AionDPS.History;

public enum DummyState
{
    /// <summary>No test is waiting or running.</summary>
    Idle,

    /// <summary>Armed: the clock waits for the first direct hit or skill use of the player.</summary>
    Armed,

    Running,

    /// <summary>The time is up (or the run was stopped): the overlay says so for a while. The run stays open for <see cref="DummyTest.Grace"/> so late packets still land.</summary>
    Finished,
}

/// <summary>What the caller knows about the player at the moment the clock starts: the skill levels as the game last listed them (and when), whether the
/// player confirmed them, the stat table, and the effects already on the player. Taken by <see cref="DummyTest.Setup"/>.</summary>
public sealed record DummySetup(
    IReadOnlyList<SkillLevel>? Levels, DateTime? LevelsReadAt, bool LevelsConfirmed, StatSnapshot? Stats, IReadOnlyList<int> ActiveEffectIds);

/// <summary>A running run in numbers: damage so far, non-tick hits, the time elapsed (never more than the run's length) and damage over that time (at least one second, so the first hit is no spike).</summary>
public readonly record struct DummyLive(long Damage, int Hits, TimeSpan Elapsed, double Dps);

/// <summary>
/// The dummy test: the player stands at a training dummy for a set time. Arm waits for the first own direct hit or accepted skill use; from that
/// moment, in capture time (the packet timestamps, never the UI clock), the run window is exactly [Start, Start + Planned] and nothing outside it is
/// ever added, however late it arrives. Pure: the caller feeds the local player's events and the capture time, so a SelfCheck can drive it.
/// Shape and state names after SkeeveTV's History/DummyTest.cs (SkeeveAN/Aion-DPS-Meter, MIT, (c) 2026 SkeeveTV); the window rules, the setup snapshot and
/// the grace handling are ours.
/// <para>Window edges: both ends are inclusive. An event exactly at Start + Planned is kept, one a millisecond later is dropped (the same rule
/// <c>DummyAnalysis</c> applies when it reads a stored run, so the two never disagree).</para>
/// </summary>
public sealed class DummyTest
{
    /// <summary>A run stays open this long after its time is up: the hits of the last second reach the poll a moment late.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2.5);

    /// <summary>How long "Finished" stays on the overlay once the run is closed.</summary>
    public static readonly TimeSpan FinishedShown = TimeSpan.FromSeconds(20);

    /// <summary>A use this much before the first hit moves the start back to it (the cast comes before its hit but is acknowledged later).
    /// Never earlier than Arm.</summary>
    public static readonly TimeSpan StartLead = TimeSpan.FromSeconds(3);

    // The target is the most-hit one of the hits in this much of the run's start.
    private static readonly TimeSpan TargetWindow = TimeSpan.FromSeconds(10);

    private TimeSpan _planned = TimeSpan.FromMinutes(2);
    private string _label = "";
    private string _note = "";
    private DateTime _armedAt;
    private DateTime _finishedAt;

    // How far back the start may move: StartLead before the first thing seen, never before Arm. Fixed at Begin so a chain of earlier uses cannot walk it back.
    private DateTime _floor;
    private DummyRun? _current;
    private bool _open;

    // The effects already on at Start are put at the front of the run's list; they follow Start when it moves back.
    private int _carriedEffects;

    public DummyState State { get; private set; }

    /// <summary>The run being recorded or just finished; null while armed or idle.</summary>
    public DummyRun? Current => _current;

    /// <summary>True while the current run still takes late packets (Finished, within the grace).</summary>
    public bool IsOpen => _open;

    public TimeSpan Planned => _planned;

    /// <summary>When Arm was called, capture time: nothing earlier counts.</summary>
    public DateTime ArmedAt => _armedAt;

    /// <summary>The number the next run gets; the caller sets it (the store continues the numbering across sessions).</summary>
    public int NextNumber { get; set; } = 1;

    /// <summary>Reads the player's levels, stats and active effects at the moment the clock starts; set by the caller, who holds the capture. May return null.</summary>
    public Func<DummySetup?>? Setup { get; set; }

    /// <summary>Resolves an entity id to a name (the dummy's, the player's); set by the caller. May return null.</summary>
    public Func<int, string?>? NameOf { get; set; }

    /// <summary>Raised when the state changes or a run starts, ends or closes (not for every event).</summary>
    public event Action? Changed;

    /// <summary>A run is over and closed (its time is up and the grace has passed, or it was stopped): the run is complete data from here on.</summary>
    public event Action<DummyRun>? RunFinished;

    /// <summary>Waits for the first own direct hit (no tick, no summon) or accepted skill use at or after <paramref name="now"/>; from then on
    /// <paramref name="planned"/> counts down. False while a run is going.</summary>
    public bool Arm(TimeSpan planned, string label, string note, DateTime now)
    {
        if (State == DummyState.Running || planned <= TimeSpan.Zero)
        {
            return false;
        }

        if (State == DummyState.Finished && _open && _current is { } open)
        {
            Close(open);
        }

        _planned = planned;
        _label = label;
        _note = note;
        _armedAt = now;
        _current = null;
        _open = false;
        _carriedEffects = 0;
        State = DummyState.Armed;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Takes back an armed test that never started.</summary>
    public void Cancel()
    {
        if (State == DummyState.Armed)
        {
            State = DummyState.Idle;
            Changed?.Invoke();
        }
    }

    /// <summary>Leaves "Finished" before its time (the run is closed first).</summary>
    public void Dismiss()
    {
        if (State != DummyState.Finished)
        {
            return;
        }

        if (_open && _current is { } run)
        {
            Close(run);
        }

        State = DummyState.Idle;
        Changed?.Invoke();
    }

    /// <summary>Ends a running test early: the run keeps what happened until <paramref name="now"/> and is marked incomplete (Length &lt; Planned).
    /// Armed: same as Cancel. When the time was up already the run is complete as usual.</summary>
    public void Stop(DateTime now)
    {
        if (State == DummyState.Armed)
        {
            Cancel();
            return;
        }

        if (State != DummyState.Running || _current is not { } run)
        {
            return;
        }

        TimeSpan length = now > run.Start ? now - run.Start : TimeSpan.Zero;
        if (length < run.Planned)
        {
            run.Length = length;
        }

        Finish(now);
    }

    /// <summary>The time left: the planned time while waiting, counting down while running, nothing after it.</summary>
    public TimeSpan Remaining(DateTime now) => State switch
    {
        DummyState.Armed => _planned,
        DummyState.Running when _current is { } run => run.End > now ? run.End - now : TimeSpan.Zero,
        _ => TimeSpan.Zero,
    };

    /// <summary>The current run in numbers at <paramref name="now"/>; null when there is none or it has no hit yet.</summary>
    public DummyLive? Live(DateTime now)
    {
        if (_current is not { } run || run.Events.Count == 0)
        {
            return null;
        }

        long damage = 0;
        int hits = 0;
        foreach (DamageEvent ev in run.Events)
        {
            damage += ev.Amount;
            if (!ev.IsTick)
            {
                hits++;
            }
        }

        TimeSpan elapsed = now - run.Start;
        if (elapsed > run.Length)
        {
            elapsed = run.Length;
        }

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return new DummyLive(damage, hits, elapsed, damage / Math.Max(1.0, elapsed.TotalSeconds));
    }

    /// <summary>
    /// Takes what the local player did since the last call: damage hits (heals are ignored), skill uses, effect changes, and the capture time of the
    /// poll. Armed: the earliest of an own direct hit or accepted use, at or after Arm, starts the clock. Running: a use up to
    /// <see cref="StartLead"/> before the start moves it back; then everything inside [Start, Start + Length] is kept and the rest dropped.
    /// The caller feeds each item once.
    /// </summary>
    public void Feed(IReadOnlyList<DamageEvent> hits, IReadOnlyList<SkillUse> uses, IReadOnlyList<EffectChange> effects, DateTime now, int playerId = 0)
    {
        if (State == DummyState.Idle || (State == DummyState.Finished && !_open))
        {
            return;
        }

        if (State == DummyState.Armed)
        {
            DateTime? first = null;
            int hitter = 0;
            foreach (DamageEvent ev in hits)
            {
                // Only something the player just did starts the clock: not a heal, not a damage-over-time tick still running from the last test, and not
                // a summon's blow (the spirit keeps striking the dummy by itself). Those still count once the clock runs.
                if (!ev.IsHeal && !ev.IsTick && !ev.FromSummon && ev.Timestamp >= _armedAt && (first is null || ev.Timestamp < first))
                {
                    first = ev.Timestamp;
                    hitter = ev.SourceObjectId;
                }
            }

            foreach (SkillUse use in uses)
            {
                if (use.Accepted && use.At >= _armedAt && (first is null || use.At < first))
                {
                    first = use.At;
                }
            }

            // An effect put on never starts the clock: buffs from other players and food renew by themselves (Stacy, 2026-10-23: the chrono started alone).

            if (first is not DateTime start)
            {
                return;
            }

            Begin(start, effects, playerId != 0 ? playerId : hitter);
        }
        else if (State == DummyState.Running && _current is { } running)
        {
            DateTime earliest = running.Start;
            DateTime floor = _floor;
            foreach (SkillUse use in uses)
            {
                if (use.Accepted && use.At >= floor && use.At < earliest)
                {
                    earliest = use.At;
                }
            }

            if (earliest < running.Start)
            {
                MoveStart(running, earliest);
            }
        }

        if (_current is not { } run)
        {
            return;
        }

        if (run.PlayerId == 0 && playerId != 0)
        {
            run.PlayerId = playerId;
        }

        DateTime from = run.Start;
        DateTime end = run.End;
        foreach (DamageEvent ev in hits.OrderBy(e => e.Timestamp))
        {
            if (!ev.IsHeal && ev.Timestamp >= from && ev.Timestamp <= end)
            {
                run.Events.Add(ev);
            }
        }

        run.Uses.AddRange(uses.Where(u => u.At >= from && u.At <= end).OrderBy(u => u.At));
        run.Effects.AddRange(effects.Where(e => e.At >= from && e.At <= end).OrderBy(e => e.At));

        // The dummy's name is read while the run is young; it settles when the run closes.
        if (State == DummyState.Running && now - run.Start <= TargetWindow + TimeSpan.FromSeconds(1))
        {
            UpdateTarget(run);
        }
    }

    /// <summary>Moves the clock on: ends a run whose time is up, closes it once the grace has passed, and clears "Finished" after a while.</summary>
    public void Tick(DateTime now)
    {
        if (State == DummyState.Running && _current is { } run && now >= run.End)
        {
            Finish(now);
        }

        if (State == DummyState.Finished && _open && _current is { } open && now >= open.End + Grace)
        {
            Close(open);
        }

        if (State == DummyState.Finished && !_open && now - _finishedAt > FinishedShown)
        {
            State = DummyState.Idle;
            Changed?.Invoke();
        }
    }

    private void Begin(DateTime start, IReadOnlyList<EffectChange> fedEffects, int playerId)
    {
        var run = new DummyRun
        {
            Number = NextNumber++,
            Label = _label,
            Note = _note,
            Start = start,
            Planned = _planned,
            Length = _planned,
            PlayerId = playerId,
        };

        if (Setup?.Invoke() is { } setup)
        {
            run.Levels = setup.Levels?.ToList();
            run.LevelsReadAt = setup.LevelsReadAt;
            run.LevelsConfirmed = setup.LevelsConfirmed;
            run.Stats = setup.Stats;

            // What was already on at Start counts as on from Start, so the comparison sees a buff that was running before the clock did.
            // One put on in this very feed is a change of its own and is added with the feed.
            foreach (int id in setup.ActiveEffectIds.Where(id => !fedEffects.Any(e => e.On && e.EffectId == id)))
            {
                run.Effects.Add(new EffectChange(start, id, On: true));
            }

            _carriedEffects = run.Effects.Count;
        }

        _floor = start - StartLead > _armedAt ? start - StartLead : _armedAt;
        _current = run;
        _open = true;
        State = DummyState.Running;
        Changed?.Invoke();
    }

    private void MoveStart(DummyRun run, DateTime start)
    {
        run.Start = start;
        for (int i = 0; i < _carriedEffects && i < run.Effects.Count; i++)
        {
            run.Effects[i] = run.Effects[i] with { At = start };
        }

        Changed?.Invoke();
    }

    private void Finish(DateTime now)
    {
        _finishedAt = now;
        State = DummyState.Finished;
        Changed?.Invoke();
    }

    /// <summary>Cuts the run to its window, settles the target and the names, and hands it out.</summary>
    private void Close(DummyRun run)
    {
        _open = false;
        DateTime start = run.Start;
        DateTime end = run.End;
        run.Events = run.Events.Where(e => !e.IsHeal && e.Timestamp >= start && e.Timestamp <= end).OrderBy(e => e.Timestamp).ToList();
        run.Uses = run.Uses.Where(u => u.At >= start && u.At <= end).OrderBy(u => u.At).ToList();
        run.Effects = run.Effects.Where(e => e.At >= start && e.At <= end).OrderBy(e => e.At).ToList();
        UpdateTarget(run);
        RunFinished?.Invoke(run);
        Changed?.Invoke();
    }

    /// <summary>The target is the one most hit by the run's first hits; the names of the targets and the player are kept with the run.</summary>
    private void UpdateTarget(DummyRun run)
    {
        List<DamageEvent> early = run.Events.Where(e => e.Timestamp - run.Start <= TargetWindow).ToList();
        if (early.Count == 0)
        {
            early = run.Events;
        }

        int? target = early.GroupBy(e => e.TargetObjectId).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
        if (target is not int id)
        {
            return;
        }

        run.TargetName = NameOf?.Invoke(id) ?? run.TargetName;
        foreach (int known in run.Events.Select(e => e.TargetObjectId).Append(run.PlayerId).Distinct())
        {
            if (NameOf?.Invoke(known) is { Length: > 0 } name)
            {
                run.Names[known] = name;
            }
        }
    }
}
