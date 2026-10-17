namespace AionDPS.Aion2;

/// <summary>A skill a monster (or a player) has begun to wind up: the game's windup frame (0x0938). It names the caster,
/// the skill, whom it is aimed at and how long the cast takes; the damage follows a moment after the cast has ended.
/// Layout learnt by SkeeveTV (Aion-DPS-Meter, MIT), see Aion2FrameDecoder.DecodeWindup.</summary>
public readonly record struct CastNotice(int Actor, int SkillId, int Target, DateTime Start, int DurationMs)
{
    public DateTime End => Start.AddMilliseconds(DurationMs);
}

/// <summary>
/// The skills being wound up right now, for the boss cast bar (Ui/BossCastBar.cs; SkeeveTV's cast bar, MIT). Written on
/// the capture thread by the decoder, read by the bar's timer on the UI thread: everything sits behind one lock. Only
/// what the windup frame says is kept - no learning of areas or landing times as upstream does.
/// </summary>
public sealed class Aion2BossCasts
{
    /// <summary>A cast is forgotten this long after it ended.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(4);

    private const int MaxActive = 200;

    private readonly object _gate = new();
    private readonly List<(CastNotice Notice, bool Stopped)> _active = new();

    public void Clear()
    {
        lock (_gate)
        {
            _active.Clear();
        }
    }

    public void NoteWindup(CastNotice cast)
    {
        lock (_gate)
        {
            Prune(cast.Start);
            // a boss can wind up two skills at once (Ultimate Berk: 1800150 and 1800050 50 ms apart, Krao Cave 2026-10-02):
            // only the same frame twice is dropped
            _active.RemoveAll(a => a.Notice.Actor == cast.Actor && a.Notice.SkillId == cast.SkillId && a.Notice.Start == cast.Start);
            if (_active.Count >= MaxActive)
            {
                _active.RemoveAt(0);
            }

            _active.Add((cast, false));
        }
    }

    /// <summary>The entity died or left (0x4236): its cast is over, it does not run on to the end.</summary>
    public void NoteGone(int entity)
    {
        lock (_gate)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Notice.Actor == entity)
                {
                    _active[i] = (_active[i].Notice, true);
                }
            }
        }
    }

    /// <summary>The casts running at <paramref name="now"/> (begun, not ended for more than <paramref name="grace"/>, the caster
    /// still there), soonest to end first.</summary>
    public IReadOnlyList<CastNotice> Running(DateTime now, TimeSpan grace)
    {
        lock (_gate)
        {
            Prune(now);
            return _active.Where(a => !a.Stopped && now >= a.Notice.Start.AddMilliseconds(-500) && now <= a.Notice.End + grace)
                .Select(a => a.Notice).OrderBy(c => c.End).ToList();
        }
    }

    private void Prune(DateTime now) => _active.RemoveAll(a => now > a.Notice.End + Linger);
}
