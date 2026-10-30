using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The falling snow behind a skin's frame (Stacy, 2026-10-30: "la neige de fond peut être dynamique ?"): small pale flakes fall slowly over the panel, each at its own pace with a slow
/// sway. A canvas in the skin's host, as big as the panel, redone when the panel changes size; never takes a click.
/// </summary>
internal static class SkinSnow
{
    private const string CanvasTag = "skin-snow";

    /// <summary>Puts the snow in the host (one canvas, whatever the number of calls).</summary>
    public static void Start(Grid host, int count)
    {
        Stop(host);
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Tag = CanvasTag };
        host.Children.Insert(0, canvas);
        canvas.SizeChanged += (_, _) => Rebuild(canvas, count);
        Rebuild(canvas, count);
    }

    /// <summary>Takes the snow out of the host.</summary>
    public static void Stop(Grid host)
    {
        foreach (Canvas canvas in host.Children.OfType<Canvas>().Where(c => c.Tag as string == CanvasTag).ToList())
        {
            canvas.Children.Clear();
            host.Children.Remove(canvas);
        }
    }

    /// <summary>How many flakes fall in the host now (the tests).</summary>
    internal static int FlakesIn(Grid host) => host.Children.OfType<Canvas>().FirstOrDefault(c => c.Tag as string == CanvasTag)?.Children.Count ?? 0;

    private static void Rebuild(Canvas canvas, int count)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 60 || height < 40)
        {
            return;
        }

        var random = new Random(23);
        var sizes = new[] { 1.4, 1.9, 2.5, 3.1 };
        var white = new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFF));
        white.Freeze();
        for (int i = 0; i < count; i++)
        {
            var move = new TranslateTransform(0, -6);
            double size = sizes[random.Next(sizes.Length)];
            var flake = new Ellipse { Width = size, Height = size, Fill = white, Opacity = 0.2 + random.NextDouble() * 0.35, RenderTransform = move, IsHitTestVisible = false };
            Canvas.SetLeft(flake, 3 + random.NextDouble() * Math.Max(1, width - 6));
            canvas.Children.Add(flake);
            double seconds = 7 + random.NextDouble() * 8;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, height + 4, TimeSpan.FromSeconds(seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(random.NextDouble() * seconds),
            });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(3 + random.NextDouble() * 3))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }
}
