namespace AionDPS.Ui;

/// <summary>A window's place on the screen: left, top, width, height (any unit, as long as all rects of one call share it).</summary>
public readonly record struct SnapRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>
/// The math of the magnetic windows (<see cref="WindowMagnet"/>), free of WPF so the self-test can run it: while a window is
/// dragged, each of its edges is pulled onto a nearby edge of another window - outer edges touching side by side (its left
/// on the other's right...) or lined up (left with left, top with top...) - and onto the edges of its screen's work area.
/// Only a neighbour that lies beside it (its span on the other axis overlaps or is within reach) pulls. The closest pull
/// within reach wins, each axis on its own; the size never changes. Idea from SkeeveTV's OverlayDocking (1.1.0, MIT):
/// the same "within reach, flush against the edge, then line up" rule, without his docking (the windows stay free).
/// </summary>
public static class WindowSnap
{
    /// <summary>How close an edge has to come to be pulled in, in DIPs (scaled to the screen's DPI by the caller).</summary>
    public const double ThresholdDips = 10;

    /// <summary>Whether this drag snaps: the "Snap windows together" switch is on and Alt is not held.</summary>
    public static bool ShouldSnap(bool switchOn, bool altHeld) => switchOn && !altHeld;

    /// <summary>
    /// Where the dragged window goes: <paramref name="moving"/> shifted so its closest edge within <paramref name="threshold"/>
    /// lies on an edge of one of <paramref name="others"/> or of <paramref name="workArea"/>, per axis. Nothing within reach
    /// (or <paramref name="enabled"/> false): <paramref name="moving"/> unchanged.
    /// </summary>
    public static SnapRect Snap(SnapRect moving, IEnumerable<SnapRect> others, SnapRect? workArea, double threshold, bool enabled = true)
    {
        if (!enabled || !(threshold > 0))
        {
            return moving;
        }

        double? dx = null, dy = null;
        foreach (SnapRect o in others)
        {
            // Beside each other on the other axis (overlapping, or a gap within reach): the edges of this axis may pull.
            bool besideY = moving.Top <= o.Bottom + threshold && moving.Bottom >= o.Top - threshold;
            bool besideX = moving.Left <= o.Right + threshold && moving.Right >= o.Left - threshold;
            if (besideY)
            {
                Consider(ref dx, moving.Left, o.Right, threshold);   // touching: its left on the other's right
                Consider(ref dx, moving.Right, o.Left, threshold);   // touching: its right on the other's left
                Consider(ref dx, moving.Left, o.Left, threshold);    // lined up
                Consider(ref dx, moving.Right, o.Right, threshold);
            }

            if (besideX)
            {
                Consider(ref dy, moving.Top, o.Bottom, threshold);
                Consider(ref dy, moving.Bottom, o.Top, threshold);
                Consider(ref dy, moving.Top, o.Top, threshold);
                Consider(ref dy, moving.Bottom, o.Bottom, threshold);
            }
        }

        if (workArea is { } area)
        {
            Consider(ref dx, moving.Left, area.Left, threshold);
            Consider(ref dx, moving.Right, area.Right, threshold);
            Consider(ref dy, moving.Top, area.Top, threshold);
            Consider(ref dy, moving.Bottom, area.Bottom, threshold);
        }

        return moving with { Left = moving.Left + (dx ?? 0), Top = moving.Top + (dy ?? 0) };
    }

    /// <summary>Keeps the shift that moves <paramref name="edge"/> onto <paramref name="target"/> if it is within reach and
    /// shorter than the best one so far.</summary>
    private static void Consider(ref double? best, double edge, double target, double threshold)
    {
        double shift = target - edge;
        if (Math.Abs(shift) <= threshold && (best is null || Math.Abs(shift) < Math.Abs(best.Value)))
        {
            best = shift;
        }
    }
}
