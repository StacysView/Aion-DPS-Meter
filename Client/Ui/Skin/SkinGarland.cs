using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The strings of little Christmas lights along the frame (Stacy, 2026-11-02: "les guirlandes à l'extérieur du cadre, plus d'espace pour les infos" and "un peu pixelisées de loin"):
/// drawn here as vectors, so they stay sharp at any size of the overlay, laid on the frame's bar instead of hanging inside the panel. Every other bulb is in the same group; the two
/// groups light up in turn, sharply, as the bulbs of a real string do. A canvas in the skin's host, as big as the panel, redone when the panel changes size; never takes a click.
/// </summary>
internal static class SkinGarland
{
    private const string CanvasTag = "skin-garland";

    private static readonly Color[] Colours =
    {
        Color.FromRgb(0xFF, 0x4A, 0x3D), Color.FromRgb(0xFF, 0xD2, 0x4A), Color.FromRgb(0x4C, 0xE0, 0x74), Color.FromRgb(0x5C, 0xC8, 0xFF), Color.FromRgb(0xFF, 0x8A, 0x2E),
    };

    /// <summary>Puts the strings in the host (one canvas, whatever the number of calls); the corners' size says where each string starts and ends.</summary>
    public static void Start(Grid host, SkinPack skin)
    {
        Stop(host);
        if (skin.Garland is not SkinGarlandInfo info || skin.Frame is not SkinFrameInfo frame)
        {
            return;
        }

        var canvas = new Canvas { IsHitTestVisible = false, Tag = CanvasTag };
        host.Children.Add(canvas);
        canvas.SizeChanged += (_, _) => Build(canvas, info, frame);
        Build(canvas, info, frame);
    }

    /// <summary>Takes the strings out of the host.</summary>
    public static void Stop(Grid host)
    {
        foreach (Canvas canvas in host.Children.OfType<Canvas>().Where(c => c.Tag as string == CanvasTag).ToList())
        {
            canvas.Children.Clear();
            host.Children.Remove(canvas);
        }
    }

    /// <summary>How many bulbs are drawn in the host now (the tests).</summary>
    internal static int BulbsIn(Grid host) =>
        host.Children.OfType<Canvas>().FirstOrDefault(c => c.Tag as string == CanvasTag)?.Children.OfType<Canvas>().Count(c => c.Tag as string == "bulb") ?? 0;

    private static void Build(Canvas canvas, SkinGarlandInfo info, SkinFrameInfo frame)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 120 || height < 90)
        {
            return;
        }

        double cornerW = frame.Corner.Width * 0.92, cornerH = frame.Corner.Height * 0.95;
        // the two groups' blink: group 0 lit during the first half of the cycle, group 1 during the second
        DoubleAnimationUsingKeyFrames Blink(int group)
        {
            double cycle = info.Period;
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(cycle), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(group == 0 ? 1 : 0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(group == 0 ? 0 : 1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle / 2))));
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(group == 0 ? 1 : 0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle))));
            blink.Freeze();
            return blink;
        }

        DoubleAnimationUsingKeyFrames[] blinks = { Blink(0), Blink(1) };
        var wire = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x2A));
        wire.Freeze();

        // one string: from (x0, y0) to (x1, y1) along the bar, the bulbs hanging to the side `down` (a unit vector pointing into the panel)
        void String(double x0, double y0, double x1, double y1, Vector down, int seed)
        {
            double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int n = Math.Max(2, (int)Math.Round(length / info.Spacing));
            Vector along = new Vector((x1 - x0) / length, (y1 - y0) / length);
            // the wire sags a little between two bulbs
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x0, y0), false, false);
                for (int i = 0; i < n; i++)
                {
                    Point a = new Point(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n);
                    Point b = new Point(x0 + (x1 - x0) * (i + 1) / n, y0 + (y1 - y0) * (i + 1) / n);
                    Point mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2) + down * info.Bulb * 0.22;
                    ctx.QuadraticBezierTo(mid, b, true, false);
                }
            }

            geometry.Freeze();
            canvas.Children.Add(new Path { Data = geometry, Stroke = wire, StrokeThickness = Math.Max(0.7, info.Bulb * 0.14), IsHitTestVisible = false });
            for (int i = 0; i <= n; i++)
            {
                Point at = new Point(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n);
                Color colour = Colours[(i + seed) % Colours.Length];
                canvas.Children.Add(Bulb(at + down * info.Bulb * 0.2, down, colour, i % 2, info.Bulb, blinks));
            }
        }

        double inset = 0; // the bar's centre is the panel's edge
        String(cornerW, inset, width - cornerW, inset, new Vector(0, 1), 0);
        String(cornerW, height - inset, width - cornerW, height - inset, new Vector(0, -1), 2);
        String(inset, cornerH, inset, height - cornerH, new Vector(1, 0), 1);
        String(width - inset, cornerH, width - inset, height - cornerH, new Vector(-1, 0), 3);
    }

    /// <summary>One bulb at <paramref name="at"/>: a socket on the wire, the glass pointing along <paramref name="down"/>, dark when off, bright with a halo when on.</summary>
    private static Canvas Bulb(Point at, Vector down, Color colour, int group, double size, DoubleAnimationUsingKeyFrames[] blinks)
    {
        double glass = size, halo = size * 3.4;
        var holder = new Canvas { Tag = "bulb", IsHitTestVisible = false, Width = 0, Height = 0 };
        Canvas.SetLeft(holder, at.X);
        Canvas.SetTop(holder, at.Y);
        // everything is drawn pointing down, then turned to `down`
        double angle = Math.Atan2(down.X * -1, down.Y) * 180 / Math.PI;
        holder.RenderTransform = new RotateTransform(angle);

        Ellipse Glass(Color fill, Color edge, double radiusScale = 1)
        {
            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.38, 0.32), Center = new Point(0.5, 0.5), RadiusX = 0.62, RadiusY = 0.62,
                GradientStops = { new GradientStop(Color.FromArgb(0xFF, 255, 255, 255), 0), new GradientStop(fill, 0.3 * radiusScale), new GradientStop(edge, 1) },
            };
            brush.Freeze();
            var e = new Ellipse { Width = glass * 0.86, Height = glass, Fill = brush, Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x18)), StrokeThickness = 0.5, IsHitTestVisible = false };
            Canvas.SetLeft(e, -glass * 0.43);
            Canvas.SetTop(e, size * 0.32);
            return e;
        }

        var socket = new Rectangle { Width = size * 0.42, Height = size * 0.34, Fill = new SolidColorBrush(Color.FromRgb(0xB8, 0x92, 0x3A)), Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0x30, 0x24, 0x10)), StrokeThickness = 0.4, IsHitTestVisible = false };
        Canvas.SetLeft(socket, -size * 0.21);
        Canvas.SetTop(socket, size * 0.0);
        holder.Children.Add(socket);
        // off: dark glass, a hint of its colour
        Color dark = Color.FromRgb((byte)(colour.R * 0.34), (byte)(colour.G * 0.34), (byte)(colour.B * 0.34));
        var off = Glass(Color.FromRgb((byte)(colour.R * 0.42), (byte)(colour.G * 0.42), (byte)(colour.B * 0.42)), dark);
        holder.Children.Add(off);
        // on: the halo and the bright glass, together, shown by the group's blink
        var on = new Canvas { IsHitTestVisible = false, Width = 0, Height = 0 };
        var glow = new RadialGradientBrush(Color.FromArgb(0x90, colour.R, colour.G, colour.B), Color.FromArgb(0x00, colour.R, colour.G, colour.B));
        glow.Freeze();
        var halo1 = new Ellipse { Width = halo, Height = halo, Fill = glow, IsHitTestVisible = false };
        Canvas.SetLeft(halo1, -halo / 2);
        Canvas.SetTop(halo1, size * 0.32 + glass / 2 - halo / 2);
        on.Children.Add(halo1);
        on.Children.Add(Glass(colour, Color.FromRgb((byte)(colour.R * 0.72), (byte)(colour.G * 0.72), (byte)(colour.B * 0.72)), 1.25));
        on.Opacity = group == 0 ? 1 : 0;
        on.BeginAnimation(UIElement.OpacityProperty, blinks[group]);
        holder.Children.Add(on);
        return holder;
    }
}
