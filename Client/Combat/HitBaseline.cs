namespace AionDPS.Combat;

/// <summary>
/// What counts as a "super effective" hit in the Monsters theme (Stacy, 2026-11-24: "plus intelligent, pas arbitraire 100K"): not a fixed number, but a hit that stands out from the
/// player's own hits on bosses. The meter keeps a running average of those hits (the recent ones weigh more, so a new gear or a new build is followed) and a hit is super effective when it
/// is several times above it. It needs a few dozen hits first to know the player, and ignores damage-over-time ticks.
/// </summary>
public sealed class HitBaseline
{
    /// <summary>How many times above the average a hit must be.</summary>
    public const double Factor = 4.0;

    /// <summary>The hits needed before the average means something.</summary>
    public const int MinHits = 30;

    private const double Window = 200; // the average follows about the last 200 hits
    private int _count;
    private double _mean;

    /// <summary>The running average of the hits so far (0 before the first).</summary>
    public double Mean => _mean;

    /// <summary>Takes a hit into account; true when it was super effective (judged against the average BEFORE it).</summary>
    public bool Add(long amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        bool strong = _count >= MinHits && amount >= _mean * Factor;
        _count++;
        _mean += (amount - _mean) / Math.Min(_count, Window);
        return strong;
    }
}
