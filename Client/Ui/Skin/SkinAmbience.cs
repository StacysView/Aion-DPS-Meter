using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The small touches of a skin's mock-up that are not in its pictures (Stacy, 2026-11-01, the "Vraiment Noël" mock-up: "la petite neige qui a un petit effet de poudre, les petits flocons
/// au fond, les belles couleurs chaudes"): a warm light under the top snow, two big faint snowflakes in the panel's background, and a glitter of powder snow that twinkles along the
/// snow of the top and bottom edges. A canvas in the skin's host, as big as the panel, redone when the panel changes size; never takes a click.
/// </summary>
internal static class SkinAmbience
{
    private const string CanvasTag = "skin-ambience";

    /// <summary>Puts the touches in the host (one canvas, whatever the number of calls); <paramref name="overhang"/> is how far the frame's snow goes out of the panel.</summary>
    public static void Start(Grid host, SkinPack skin, Thickness overhang)
    {
        Stop(host);
        var canvas = new Canvas { IsHitTestVisible = false, Tag = CanvasTag };
        host.Children.Add(canvas);
        canvas.SizeChanged += (_, _) => Build(canvas, skin, overhang);
        Build(canvas, skin, overhang);
    }

    /// <summary>Takes the touches out of the host.</summary>
    public static void Stop(Grid host)
    {
        foreach (Canvas canvas in host.Children.OfType<Canvas>().Where(c => c.Tag as string == CanvasTag).ToList())
        {
            canvas.Children.Clear();
            host.Children.Remove(canvas);
        }
    }

    /// <summary>How many sparkles twinkle in the host now (the tests).</summary>
    internal static int SparklesIn(Grid host) =>
        host.Children.OfType<Canvas>().FirstOrDefault(c => c.Tag as string == CanvasTag)?.Children.OfType<Image>().Count(i => i.Tag as string == "spark") ?? 0;

    private static void Build(Canvas canvas, SkinPack skin, Thickness overhang)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 60 || height < 40)
        {
            return;
        }

        // a warm light under the snow of the top edge, as if the garland's lights fell on the panel
        var warm = new RadialGradientBrush(Color.FromArgb(0x26, 0xFF, 0xB8, 0x60), Color.FromArgb(0x00, 0xFF, 0xB8, 0x60));
        warm.Freeze();
        var light = new Ellipse { Width = width * 0.9, Height = height * 0.8, Fill = warm, IsHitTestVisible = false };
        Canvas.SetLeft(light, width * 0.05);
        Canvas.SetTop(light, -height * 0.35);
        canvas.Children.Add(light);

        // two big faint snowflakes in the background
        SkinImage[] flakes = skin.Flakes.ToArray();
        (double X, double Y, double Size)[] places = { (0.80, 0.42, 74), (0.16, 0.70, 52) };
        for (int i = 0; i < places.Length && flakes.Length > 0; i++)
        {
            if (skin.Bitmap(flakes[i % flakes.Length].File) is BitmapSource flake)
            {
                var image = new Image { Source = flake, Width = places[i].Size, Height = places[i].Size, Stretch = Stretch.Uniform, Opacity = 0.07, IsHitTestVisible = false };
                Canvas.SetLeft(image, places[i].X * width - places[i].Size / 2);
                Canvas.SetTop(image, places[i].Y * height - places[i].Size / 2);
                canvas.Children.Add(image);
            }
        }

        // the powder: tiny glints that come and go on the snow of the top edge, and a few on the bottom one
        if (skin.Get("etincelle") is not (SkinImage, BitmapSource spark))
        {
            return;
        }

        // warm bokeh: big soft lights of the tree's garlands out of focus behind the panel (Stacy, 2026-11-02: "plus chaleureux, plus flashy")
        var random = new Random(17);
        Color[] bokeh = { Color.FromRgb(0xFF, 0xB0, 0x4A), Color.FromRgb(0xFF, 0x5A, 0x5A), Color.FromRgb(0xFF, 0xD8, 0x7A), Color.FromRgb(0x6B, 0xE0, 0x8F) };
        for (int i = 0; i < 9; i++)
        {
            Color c = bokeh[i % bokeh.Length];
            var soft = new RadialGradientBrush(Color.FromArgb(0x40, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B));
            soft.Freeze();
            double d = 26 + random.NextDouble() * 34;
            var disc = new Ellipse { Width = d, Height = d, Fill = soft, Opacity = 0.5, IsHitTestVisible = false };
            Canvas.SetLeft(disc, random.NextDouble() * (width - d));
            Canvas.SetTop(disc, random.NextDouble() * (height - d));
            canvas.Children.Add(disc);
            disc.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 0.8, TimeSpan.FromSeconds(2.5 + random.NextDouble() * 3))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(random.NextDouble() * 3),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }

        // a warm light from below too, for the bottom drifts
        var floor = new Ellipse { Width = width * 0.9, Height = height * 0.5, Fill = warm, IsHitTestVisible = false };
        Canvas.SetLeft(floor, width * 0.05);
        Canvas.SetTop(floor, height * 0.78);
        canvas.Children.Add(floor);

        for (int i = 0; i < 64; i++)
        {
            // 0-21 on the top snow, 22-33 on the bottom one, 34-45 along the sides, the rest scattered over the panel
            bool top = i < 22, bottom = i is >= 22 and < 34, side = i is >= 34 and < 46;
            double size = 3 + random.NextDouble() * (i >= 46 ? 3 : 4);
            double x = side ? (i % 2 == 0 ? -overhang.Left * (0.2 + random.NextDouble() * 0.5) : width + overhang.Right * (0.2 + random.NextDouble() * 0.5)) : 6 + random.NextDouble() * (width - 12);
            double y = top ? -overhang.Top * (0.15 + random.NextDouble() * 0.6)
                : bottom ? height + overhang.Bottom * (0.1 + random.NextDouble() * 0.5)
                : 4 + random.NextDouble() * (height - 8);
            var glint = new Image { Source = spark, Width = size, Height = size, Opacity = 0, IsHitTestVisible = false, Tag = "spark", RenderTransformOrigin = new Point(0.5, 0.5) };
            var grow = new ScaleTransform(1, 1);
            glint.RenderTransform = grow;
            Canvas.SetLeft(glint, x - size / 2);
            Canvas.SetTop(glint, y - size / 2);
            canvas.Children.Add(glint);
            double cycle = 2.4 + random.NextDouble() * 3.2;
            var begin = TimeSpan.FromSeconds(random.NextDouble() * cycle);
            var twinkle = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, Duration = TimeSpan.FromSeconds(cycle) };
            twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
            twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))));
            glint.BeginAnimation(UIElement.OpacityProperty, twinkle);
            var pulse = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, Duration = TimeSpan.FromSeconds(cycle) };
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1.1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))));
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
    }
}
