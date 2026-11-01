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

        var random = new Random(17);
        for (int i = 0; i < 26; i++)
        {
            bool top = i < 18;
            double size = 3 + random.NextDouble() * 4;
            double x = 6 + random.NextDouble() * (width - 12);
            double y = top ? -overhang.Top * (0.15 + random.NextDouble() * 0.6) : height + overhang.Bottom * (0.1 + random.NextDouble() * 0.5);
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
