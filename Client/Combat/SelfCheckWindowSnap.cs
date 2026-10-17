using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// The magnetic windows' math (Ui/WindowSnap): a dragged window's edges snap onto a neighbour's (touching side by side or
/// lined up) and onto the work area's within reach, nothing moves out of reach, and the switch off or Alt held leave the
/// drag alone.
/// </summary>
public static class SelfCheckWindowSnap
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Magnetic windows:");
        bool ok = true;
        const double reach = WindowSnap.ThresholdDips;
        var screen = new SnapRect(0, 0, 1920, 1040);
        var overlay = new SnapRect(400, 300, 320, 200); // right 720, bottom 500
        SnapRect[] others = [overlay];

        // Its left 6 px right of the overlay's right, tops 4 px apart: flush against it and lined up at the top.
        var beside = WindowSnap.Snap(new SnapRect(726, 304, 250, 150), others, screen, reach);
        // Its bottom 7 px above the overlay's top, lefts 3 px apart: on top of it and lined up at the left.
        var above = WindowSnap.Snap(new SnapRect(397, 43, 200, 250), others, screen, reach);
        bool edges = beside == new SnapRect(720, 300, 250, 150) && above == new SnapRect(400, 50, 200, 250);
        Console.WriteLine($"  -> 6 px beside the overlay it touches it, 4 px off its top lines up with it; 7 px above it sits on it: {edges}");
        ok &= edges;

        // Right with right and bottom with bottom (overlapping, aligned), and the left side of a window touching from the left.
        var aligned = WindowSnap.Snap(new SnapRect(475, 352, 240, 150), others, screen, reach);
        var leftOf = WindowSnap.Snap(new SnapRect(150, 315, 255, 100), others, screen, reach);
        bool lined = aligned == new SnapRect(480, 350, 240, 150) && leftOf == new SnapRect(145, 315, 255, 100);
        Console.WriteLine($"  -> right on right and bottom on bottom, and its right on the overlay's left: {lined}");
        ok &= lined;

        // The work area: 8 px from the left and the bottom (taskbar) edges goes into the corner; far from everything nothing moves.
        var corner = WindowSnap.Snap(new SnapRect(8, 832, 200, 200), [], screen, reach);
        bool area = corner == new SnapRect(0, 840, 200, 200);
        Console.WriteLine($"  -> 8 px from the screen's left and bottom edges it goes into the corner: {area}");
        ok &= area;

        var free = new SnapRect(1000, 600, 200, 150);
        var away = new SnapRect(731, 300, 250, 150); // 11 px off the overlay's right: just out of reach
        var far = new SnapRect(405, 800, 320, 100);  // 5 px off the overlay's left but far below: no neighbour, no pull
        bool unchanged = WindowSnap.Snap(free, others, screen, reach) == free && WindowSnap.Snap(away, others, screen, reach) == away
            && WindowSnap.Snap(far, others, screen, reach) == far;
        Console.WriteLine($"  -> out of reach (11 px, or lined up but far away) it stays where it is: {unchanged}");
        ok &= unchanged;

        // Between the overlay and a second window 12 px further: the closer edge wins (3 px to one beats 9 px to the other).
        SnapRect[] two = [overlay, new SnapRect(832, 300, 100, 100)];
        bool nearest = WindowSnap.Snap(new SnapRect(723, 300, 100, 50), two, screen, reach).Left == 720
            && WindowSnap.Snap(new SnapRect(729, 300, 100, 50), two, screen, reach).Left == 732;
        var vsArea = WindowSnap.Snap(new SnapRect(1700, 100, 214, 80), [new SnapRect(1680, 300, 100, 100)], screen, reach);
        bool both = nearest && vsArea.Left == 1706; // 6 px from the screen's right edge, nothing beside it on that row
        Console.WriteLine($"  -> each axis takes its closest edge: {both}");
        ok &= both;

        var near = new SnapRect(726, 304, 250, 150);
        bool off = WindowSnap.Snap(near, others, screen, reach, enabled: WindowSnap.ShouldSnap(false, false)) == near
            && WindowSnap.Snap(near, others, screen, reach, enabled: WindowSnap.ShouldSnap(true, true)) == near
            && WindowSnap.ShouldSnap(true, false) && !WindowSnap.ShouldSnap(false, true)
            && WindowSnap.Snap(near, others, screen, 0) == near;
        Console.WriteLine($"  -> switch off or Alt held: no snapping at all: {off}");
        ok &= off;

        // DPI: at 150 % the reach is 15 screen pixels, so 13 px still pulls.
        bool dpi = WindowSnap.Snap(new SnapRect(733, 300, 100, 50), others, screen, reach * 1.5).Left == 720
            && WindowSnap.Snap(new SnapRect(733, 300, 100, 50), others, screen, reach).Left == 733;
        Console.WriteLine($"  -> the reach follows the screen's scale (13 px pulls at 150 %, not at 100 %): {dpi}");
        ok &= dpi;

        return ok;
    }
}
