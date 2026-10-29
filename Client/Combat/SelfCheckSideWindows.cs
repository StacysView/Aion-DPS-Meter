using System.Windows;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The optional small windows (Soins, Degats recus; Ui/ModeWindow): off by default, and a saved place is used only when it is still on the screen.</summary>
public static class SelfCheckSideWindows
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Small windows (Soins, Degats recus):");
        var defaults = new MeterSettings();
        bool off = !defaults.HealWindow && !defaults.TakenWindow;
        Console.WriteLine($"  -> both are off by default: {off}");

        double left = SystemParameters.VirtualScreenLeft, top = SystemParameters.VirtualScreenTop;
        bool good = ModeWindow.TryParseBounds($"{left + 100:0},{top + 100:0},300,160", out Rect place) && place.Width == 300 && place.Height == 160;
        bool away = !ModeWindow.TryParseBounds("-90000,-90000,300,160", out _);
        bool tiny = !ModeWindow.TryParseBounds($"{left + 100:0},{top + 100:0},10,10", out _);
        bool junk = !ModeWindow.TryParseBounds("a,b,c", out _) && !ModeWindow.TryParseBounds(null, out _);
        Console.WriteLine($"  -> a saved place on the screen is used, one off every screen, too small or garbled is not: {good && away && tiny && junk}");
        return off && good && away && tiny && junk;
    }
}
