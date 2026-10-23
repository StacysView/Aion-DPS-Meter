using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>When the last skill frame arrived and what it covered, with the state of the player's confirmation (see
/// <see cref="DummyTestController.RequestSkillConfirmation"/>). <see cref="At"/> is capture time, null when no skill frame came this session
/// (the saved list of an earlier session does not count).</summary>
public sealed record SkillsReadInfo(
    DateTime? At, int Count, int Opcode, TimeSpan? Age, bool Requested, bool PlayerConfirmed, bool FrameAfterRequest)
{
    /// <summary>True when a run started now would store its levels as confirmed.</summary>
    public bool Confirmed => PlayerConfirmed && FrameAfterRequest;
}

/// <summary>
/// The dummy test as the meter wires it: owns the <see cref="DummyTest"/> state machine and the <see cref="DummyRunStore"/>, is fed from the meter's
/// poll (<see cref="Poll"/>), picks the local player's hits, skill uses and effects out of what the capture saw, files each finished run, and offers
/// everything a window needs as plain properties, methods and the <see cref="Changed"/> event. No WPF types: the window and the overlay are another file's.
/// The idea of one controller between the poll, the state machine and the windows is SkeeveTV's (Ui/DummyTestOverlay.cs, SkeeveAN/Aion-DPS-Meter, MIT, (c) 2026 SkeeveTV);
/// reading the player's own frames from the directory, the setup snapshot and the store are ours.
/// <para>Every time here is capture time (the packet timestamps, local). The clock is <see cref="Clock"/>, the wall clock by default, which is the same
/// scale for a live capture; a test passes its own. Calls and events are all on the thread that calls <see cref="Poll"/> (the UI dispatcher in the meter);
/// <see cref="Changed"/> is raised synchronously from it, so a window that wants another thread marshals itself.</para>
/// </summary>
public sealed class DummyTestController : IDisposable
{
    // Used and effect lists are asked a little before the last one seen, and de-duplicated: frames reach the directory slightly out of order.
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(2);

    private readonly DummyTest _test = new();
    private readonly DummyRunStore? _store;
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Func<DateTime> _clock;
    private readonly HashSet<SkillUse> _seenUses = new();
    private readonly HashSet<EffectChange> _seenEffects = new();
    private readonly Dictionary<long, DummyRun> _loaded = new();
    private DateTime _lastUse;
    private DateTime _lastEffect;
    private List<DummyRunRow> _runs = new();
    private DateTime? _confirmRequestedAt;
    private bool _playerConfirmed;
    private bool _confirmedShown;

    /// <summary>Takes the store (or null: runs are then not kept) and owns it from here: <see cref="Dispose"/> closes it. <paramref name="clock"/>
    /// is the capture clock; null means <see cref="DateTime.Now"/>.</summary>
    public DummyTestController(Func<Aion2EntityDirectory?> directory, DummyRunStore? store, Func<DateTime>? clock = null)
    {
        _directory = directory;
        _store = store;
        _clock = clock ?? (() => DateTime.Now);
        _test.Setup = TakeSetup;
        _test.NameOf = id => _directory()?.NameFor(id);
        _test.Changed += () => Changed?.Invoke();
        _test.RunFinished += OnRunFinished;
        RefreshRuns();
    }

    /// <summary>The meter's own: dummy-runs.db in the app data folder. A locked or damaged file never keeps the meter from running: the controller then works without a
    /// store and says why in <see cref="StoreError"/>.</summary>
    public static DummyTestController Open(Func<Aion2EntityDirectory?> directory)
    {
        DummyRunStore? store = null;
        string? error = null;
        try
        {
            store = new DummyRunStore(DummyRunStore.DefaultPath);
            store.Prune();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            store?.Dispose();
            store = null;
        }

        return new DummyTestController(directory, store) { StoreError = error };
    }

    // ---------------------------------------------------------------- state for the window and the overlay

    /// <summary>Raised when anything the window shows may have changed: state, a run started or filed, the list, the skills line. Not for every hit.</summary>
    public event Action? Changed;

    /// <summary>A run was closed and (when there is a store) filed; its <see cref="DummyRun.Id"/> is then set. Raised before <see cref="Changed"/>.</summary>
    public event Action<DummyRun>? RunFinished;

    /// <summary>Why the runs are not kept (the store could not be opened or the last save failed); null when all is well.</summary>
    public string? StoreError { get; private set; }

    public bool HasStore => _store is not null;

    /// <summary>The capture clock now: what <see cref="Remaining"/> and <see cref="Live"/> are measured against.</summary>
    public DateTime Clock => _clock();

    public DummyState State => _test.State;

    public TimeSpan Planned => _test.Planned;

    /// <summary>The time left at <paramref name="now"/> (the planned time while waiting, counting down while running, zero after).</summary>
    public TimeSpan Remaining(DateTime now) => _test.Remaining(now);

    public TimeSpan Remaining() => _test.Remaining(_clock());

    /// <summary>The run being recorded or just finished (its lists grow while running; do not keep it); null while armed or idle.</summary>
    public DummyRun? Current => _test.Current;

    /// <summary>Damage so far, hits, elapsed time and DPS so far (damage / elapsed, at least one second) of the current run; null before its first hit.</summary>
    public DummyLive? Live(DateTime now) => _test.Live(now);

    public DummyLive? Live() => _test.Live(_clock());

    /// <summary>The dummy the current run hits (the most-hit target of its first seconds); empty before one is known.</summary>
    public string TargetName => _test.Current?.TargetName ?? "";

    /// <summary>Whether <see cref="TargetName"/> is a training dummy by name (<see cref="DummyTargets"/>). A hint, not a block: other targets are allowed.</summary>
    public bool TargetIsDummy => DummyTargets.IsDummy(TargetName);

    // ---------------------------------------------------------------- start and stop

    /// <summary>Waits for the player's first hit, accepted skill use or effect; then <paramref name="planned"/> counts down. False while a run is going.</summary>
    public bool Arm(TimeSpan planned, string label, string note)
    {
        if (_store is not null)
        {
            try
            {
                _test.NextNumber = _store.NextNumber();
            }
            catch (Exception ex)
            {
                StoreError = ex.Message;
            }
        }

        DateTime now = _clock();
        _seenUses.Clear();
        _seenEffects.Clear();
        _lastUse = now;
        _lastEffect = now;
        return _test.Arm(planned, label, note, now);
    }

    /// <summary>Takes back an armed test that never started.</summary>
    public void Cancel() => _test.Cancel();

    /// <summary>Ends a running test early; the run is kept and marked incomplete (never compared). Armed: same as <see cref="Cancel"/>.</summary>
    public void Stop() => _test.Stop(_clock());

    /// <summary>Leaves "Finished" before its 20 seconds are over.</summary>
    public void Dismiss() => _test.Dismiss();

    /// <summary>Moves the clock on without new data (a 100 ms UI timer may call it; <see cref="Poll"/> does it too). Idempotent.</summary>
    public void Tick() => _test.Tick(_clock());

    // ---------------------------------------------------------------- the feed

    /// <summary>
    /// The meter's poll: <paramref name="events"/> is the batch the source just returned. Picks the local player's damage hits (a summon's, attributed to its
    /// owner as the meter does), reads the player's skill uses and effect changes since the last poll from the directory, feeds the state machine and
    /// moves its clock. Cheap while idle.
    /// </summary>
    public void Poll(IReadOnlyList<DamageEvent> events)
    {
        if (_test.State == DummyState.Idle)
        {
            return;
        }

        DateTime now = _clock();
        Aion2EntityDirectory? directory = _directory();
        var hits = new List<DamageEvent>();
        var uses = new List<SkillUse>();
        var changes = new List<EffectChange>();
        int player = 0;
        if (directory is not null)
        {
            player = directory.LocalPlayerId >= 0 ? directory.LocalPlayerId : directory.InferLocalPlayer() ?? 0;
            foreach (DamageEvent ev in events)
            {
                if (ev.IsHeal || ev.TargetObjectId == ev.SourceObjectId)
                {
                    continue;
                }

                if (IsLocal(directory, ev.SourceObjectId))
                {
                    hits.Add(ev);
                }
                else if (directory.SummonOwnerOf(ev.SourceObjectId) is int owner && IsLocal(directory, owner))
                {
                    // A summon whose owner was found after the decoder had named its hit.
                    hits.Add(ev with { SourceObjectId = owner, FromSummon = true });
                }
            }

            uses = Fresh(directory.Own.UsesSince(_lastUse - Overlap), _seenUses, u => u.At, ref _lastUse);
            changes = Fresh(directory.Own.EffectsSince(_lastEffect - Overlap), _seenEffects, e => e.At, ref _lastEffect);
        }

        _test.Feed(hits, uses, changes, now, player);
        _test.Tick(now);
        RaiseIfSkillsConfirmationChanged(now);
    }

    private static bool IsLocal(Aion2EntityDirectory directory, int id) => directory.IsLocalPlayer(id) || directory.InferLocalPlayer() == id;

    /// <summary>The items not fed before, and the "seen" set cut to the overlap window so it stays small.</summary>
    private static List<T> Fresh<T>(IReadOnlyList<T> asked, HashSet<T> seen, Func<T, DateTime> at, ref DateTime last)
    {
        var fresh = new List<T>();
        foreach (T item in asked)
        {
            if (seen.Add(item))
            {
                fresh.Add(item);
            }

            if (at(item) > last)
            {
                last = at(item);
            }
        }

        DateTime floor = last - Overlap - Overlap;
        seen.RemoveWhere(item => at(item) < floor);
        return fresh;
    }

    // ---------------------------------------------------------------- the setup snapshot

    private DummySetup? TakeSetup()
    {
        if (_directory() is not { } directory)
        {
            return null;
        }

        IReadOnlyList<Aion2SkillEntry> known = directory.LocalSkillLevels;
        Aion2SkillFrame? frame = directory.LastSkillFrame;
        List<SkillLevel>? levels = known.Count > 0 ? known.Select(k => new SkillLevel(k.SkillId, k.Level, k.BaseLevel)).ToList() : null;
        return new DummySetup(levels, frame?.At, SkillsReadInfoAt(frame).Confirmed, directory.LocalStats, directory.Own.ActiveEffectIds());
    }

    // ---------------------------------------------------------------- skills read

    /// <summary>The last skill frame, its size and age, and where the player's confirmation stands.</summary>
    public SkillsReadInfo SkillsReadInfo => SkillsReadInfoAt(_directory()?.LastSkillFrame);

    private SkillsReadInfo SkillsReadInfoAt(Aion2SkillFrame? frame) => new(
        frame?.At, frame?.Count ?? 0, frame?.Opcode ?? 0,
        frame is { } f ? _clock() - f.At : (TimeSpan?)null,
        Requested: _confirmRequestedAt is not null,
        PlayerConfirmed: _playerConfirmed,
        FrameAfterRequest: frame is { } g && _confirmRequestedAt is { } asked && g.At >= asked);

    /// <summary>Step one: the player is about to read the skills in game (open the skills tab, or change a level). Notes the moment; the earlier confirmation is void.
    /// Only a skill frame that arrives after this counts as a reading.</summary>
    public void RequestSkillConfirmation()
    {
        _confirmRequestedAt = _clock();
        _playerConfirmed = false;
        _confirmedShown = false;
        Changed?.Invoke();
    }

    /// <summary>Step two: the player says "I read the skills in game". Returns whether a skill frame has arrived since the request (otherwise the levels stay
    /// unconfirmed, and <see cref="SkillsReadInfo"/> shows why); false too without a request.</summary>
    public bool ConfirmSkillsRead()
    {
        if (_confirmRequestedAt is null)
        {
            return false;
        }

        _playerConfirmed = true;
        bool confirmed = SkillsReadInfo.Confirmed;
        _confirmedShown = confirmed;
        Changed?.Invoke();
        return confirmed;
    }

    // The frame can arrive after the player pressed confirm: the line then turns green by itself.
    private void RaiseIfSkillsConfirmationChanged(DateTime now)
    {
        if (_playerConfirmed && SkillsReadInfo.Confirmed != _confirmedShown)
        {
            _confirmedShown = !_confirmedShown;
            Changed?.Invoke();
        }
    }

    /// <summary>The player's stat table (frame 0x4a36) as last received this session, or null before one arrived. A run stores it to check the gear did not change.</summary>
    public StatSnapshot? LocalStats => _directory()?.LocalStats;

    /// <summary>How many effects (buffs, potions, food) are on the player now; a run records them.</summary>
    public int ActiveEffectCount => _directory()?.Own.ActiveEffectIds().Count ?? 0;

    /// <summary>The opcode question: which frames arrive when the player opens the skills tab? Opcodes that arrived at or after <paramref name="since"/> (capture time)
    /// and had never been seen before it - plain data, oldest first. Ask within minutes (the capture keeps the last 4096 bursts).</summary>
    public IReadOnlyList<OpcodeSighting> NewOpcodesSince(DateTime since) =>
        _directory()?.Own.OpcodesSince(since).Where(s => !s.SeenBefore).ToList() ?? new List<OpcodeSighting>();

    /// <summary>The same list with the opcodes already known too, for when the skills frame was seen before (the login list).</summary>
    public IReadOnlyList<OpcodeSighting> OpcodesSince(DateTime since) =>
        _directory()?.Own.OpcodesSince(since) ?? Array.Empty<OpcodeSighting>();

    // ---------------------------------------------------------------- the stored runs

    /// <summary>The stored runs, newest first, as of the last change (refreshed when a run is filed, deleted or edited; <see cref="Refresh"/> forces it).</summary>
    public IReadOnlyList<DummyRunRow> Runs => _runs;

    /// <summary>Reads the list from the store again.</summary>
    public void Refresh()
    {
        RefreshRuns();
        Changed?.Invoke();
    }

    private void RefreshRuns()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _runs = _store.Query(DummyRunStore.Keep);
        }
        catch (Exception ex)
        {
            StoreError = ex.Message;
        }
    }

    /// <summary>A stored run in full (events included), or null. Kept in memory afterwards: do not change the returned run.</summary>
    public DummyRun? LoadRun(long id)
    {
        if (_loaded.TryGetValue(id, out DummyRun? known))
        {
            return known;
        }

        try
        {
            if (_store?.Load(id) is { } run)
            {
                return _loaded[id] = run;
            }
        }
        catch (Exception ex)
        {
            StoreError = ex.Message;
        }

        return null;
    }

    public DummyRunSummary? Summarize(long id) => LoadRun(id) is { } run ? DummyAnalysis.Summarize(run) : null;

    public void UpdateNote(long id, string note)
    {
        Edit(() => _store!.UpdateNote(id, note));
        if (_loaded.TryGetValue(id, out DummyRun? run))
        {
            run.Note = note;
        }
    }

    public void UpdateLabel(long id, string label)
    {
        Edit(() => _store!.UpdateLabel(id, label));
        if (_loaded.TryGetValue(id, out DummyRun? run))
        {
            run.Label = label;
        }
    }

    public void Delete(long id)
    {
        Edit(() => _store!.Delete(id));
        _loaded.Remove(id);
    }

    private void Edit(Action write)
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            write();
        }
        catch (Exception ex)
        {
            StoreError = ex.Message;
        }

        RefreshRuns();
        Changed?.Invoke();
    }

    private void OnRunFinished(DummyRun run)
    {
        // Nothing hit, nothing to keep (a stop at once).
        if (_store is not null && run.Events.Count > 0)
        {
            try
            {
                run.SavedAt = DateTime.Now;
                _store.Insert(run);
                _store.Prune();
                _loaded[run.Id] = run;
                StoreError = null;
            }
            catch (Exception ex)
            {
                // The run stays in memory for this session's window; the meter itself never stops for a history file.
                StoreError = ex.Message;
            }

            RefreshRuns();
        }

        RunFinished?.Invoke(run);
    }

    // ---------------------------------------------------------------- analysis

    /// <summary>Compares configuration A with B, each a list of stored run ids (several runs pool). Incomplete runs are left out, runs of different planned lengths
    /// are refused (<see cref="DummyTestComparison.Comparable"/> false).</summary>
    public DummyTestComparison Compare(IReadOnlyList<long> idsA, IReadOnlyList<long> idsB) => Compare(Load(idsA), Load(idsB));

    public static DummyTestComparison Compare(IReadOnlyList<DummyRun> runsA, IReadOnlyList<DummyRun> runsB) => DummyAnalysis.Compare(runsA, runsB);

    /// <summary>Up to four configurations (A, B, C, D), each a list of stored run ids: every one is compared with the first, and each gets its rotation summary.</summary>
    public DummyMultiComparison CompareMany(IReadOnlyList<IReadOnlyList<long>> ids) =>
        DummyAnalysis.CompareSides(ids.Select(side => (IReadOnlyList<DummyRun>)Load(side)).ToList(), id => Aion2.Protocol.Aion2SkillNames.NameOf(id));

    /// <summary>The level curves over every stored run that is complete and has confirmed levels.</summary>
    public IReadOnlyList<DummyLevelCurve> LevelCurves()
    {
        var runs = new List<DummyRun>();
        foreach (DummyRunRow row in _runs.Where(r => r.Complete && r.LevelsConfirmed))
        {
            if (LoadRun(row.Id) is { } run)
            {
                runs.Add(run);
            }
        }

        return DummyAnalysis.LevelCurves(runs);
    }

    private List<DummyRun> Load(IReadOnlyList<long> ids)
    {
        var runs = new List<DummyRun>();
        foreach (long id in ids)
        {
            if (LoadRun(id) is { } run)
            {
                runs.Add(run);
            }
        }

        return runs;
    }

    public void Dispose()
    {
        _test.RunFinished -= OnRunFinished;
        _store?.Dispose();
    }
}
