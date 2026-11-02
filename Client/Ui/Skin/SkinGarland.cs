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

    /// <summary>A bulb's lit half, with where it stands on its string and the numbers that make each bulb twinkle at its own pace.</summary>
    private sealed class Led
    {
        public Led(UIElement on, int strand, int index, double a, double b)
        {
            On = on;
            Strand = strand;
            Index = index;
            A = a;
            B = b;
        }

        public UIElement On { get; }

        public int Strand { get; }

        public int Index { get; }

        public int Total { get; set; }

        public double A { get; }

        public double B { get; }
    }

    private static readonly List<Led> Leds = new();
    private static SkinGarlandInfo? _info;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly string[] Cycle = { "alternate", "chase", "wave", "twinkle", "fill", "flash" };
    private static bool _hooked;
    private static double _started;

    /// <summary>How lit a bulb is (0 off, 1 on) at <paramref name="t"/> seconds in a pattern.</summary>
    internal static double Level(string pattern, int index, int total, int strand, double a, double b, double t)
    {
        double position = strand % 2 == 0 ? index : total - index; // the top and the bottom run opposite ways
        switch (pattern)
        {
            case "chase":
                return ((position - t * 9) % 6 + 6) % 6 < 2.2 ? 1 : 0;
            case "twinkle":
            {
                double phase = t / (0.55 + a * 0.9) + b;
                return phase - Math.Floor(phase) < 0.55 ? 1 : 0;
            }

            case "wave":
                return 0.1 + 0.9 * (0.5 + 0.5 * Math.Sin(position * 0.55 - t * 4.2));
            case "fill":
            {
                double cycle = 4.2, local = t % cycle;
                return local > 3.4 ? (local < 3.8 ? 1 : 0) : (position < local / 3.4 * (total + 1) ? 1 : 0);
            }

            case "flash":
                return (int)(t / 0.32) % 2 == 0 ? 1 : 0;
            default: // alternate: every other bulb, then the others
                return ((int)(t / 0.6) + index) % 2 == 0 ? 1 : 0;
        }
    }

    private static void OnRender(object? sender, EventArgs e)
    {
        if (_info is not SkinGarlandInfo info || Leds.Count == 0)
        {
            return;
        }

        double t = Clock.Elapsed.TotalSeconds;
        string pattern = info.Pattern;
        double local = t;
        if (pattern == "mix")
        {
            double seconds = Math.Max(1, info.PatternSeconds);
            pattern = Cycle[(int)(t / seconds) % Cycle.Length];
            local = t % seconds;
        }

        else if (info.ShowEveryMinutes > 0)
        {
            // the main pattern, and once in a while a round of all the others: the last stretch of each period
            double seconds = Math.Max(1, info.PatternSeconds), period = info.ShowEveryMinutes * 60, round = (Cycle.Length - 1) * seconds;
            double inPeriod = (t - _started) % period;
            if (period > round && inPeriod >= period - round)
            {
                double into = inPeriod - (period - round);
                pattern = Cycle.Where(c => c != pattern).ElementAt(Math.Min(Cycle.Length - 2, (int)(into / seconds)));
                local = into % seconds;
            }
        }

        foreach (Led led in Leds)
        {
            led.On.Opacity = Level(pattern, led.Index, led.Total, led.Strand, led.A, led.B, local);
        }
    }

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

        _info = info;
        _started = Clock.Elapsed.TotalSeconds;
        if (!_hooked)
        {
            CompositionTarget.Rendering += OnRender;
            _hooked = true;
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

        Leds.Clear();
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRender;
            _hooked = false;
        }
    }

    /// <summary>How many bulbs are drawn in the host now (the tests).</summary>
    internal static int BulbsIn(Grid host) =>
        host.Children.OfType<Canvas>().FirstOrDefault(c => c.Tag as string == CanvasTag)?.Children.OfType<Canvas>().Count(c => c.Tag as string == "bulb") ?? 0;

    private static void Build(Canvas canvas, SkinGarlandInfo info, SkinFrameInfo frame)
    {
        canvas.Children.Clear();
        Leds.Clear();
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 120 || height < 90)
        {
            return;
        }

        double cornerW = frame.Corner.Width * 0.92, cornerH = frame.Corner.Height * 0.95;
        var wire = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x2A));
        wire.Freeze();

        // the strings hang from hooks in swags of different lengths and depths, the bulbs hanging from the wire a little crooked, as on a real string (Stacy, 2026-11-02: "plus naturel,
        // pas d'office en ligne droite"); `bow` is where a swag sags, `hang` where the bulbs point
        var random = new Random(31);
        int seed = 0, strands = 0;
        void Strand(Point from, Point to, Vector bow, Vector hang)
        {
            double length = (to - from).Length;
            if (length < 24)
            {
                return;
            }

            Vector unit = (to - from) / length;
            Point previous = from;
            double travelled = 0;
            int index = 0;
            while (travelled < length - 8)
            {
                double span = Math.Min(length - travelled, 46 + random.NextDouble() * 40);
                if (length - travelled - span < 18)
                {
                    span = length - travelled;
                }

                Point end = previous + unit * span;
                double sag = Math.Min(span * 0.16, 3 + random.NextDouble() * 5);
                Point control = new Point((previous.X + end.X) / 2, (previous.Y + end.Y) / 2) + bow * sag * 2;
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(previous, false, false);
                    ctx.QuadraticBezierTo(control, end, true, false);
                }

                geometry.Freeze();
                canvas.Children.Add(new Path { Data = geometry, Stroke = wire, StrokeThickness = Math.Max(0.7, info.Bulb * 0.14), IsHitTestVisible = false });
                int n = Math.Max(1, (int)Math.Round(span / info.Spacing));
                for (int i = 0; i <= n; i++)
                {
                    double t = (double)i / n;
                    Point on = new Point((1 - t) * (1 - t) * previous.X + 2 * (1 - t) * t * control.X + t * t * end.X, (1 - t) * (1 - t) * previous.Y + 2 * (1 - t) * t * control.Y + t * t * end.Y);
                    double tilt = (random.NextDouble() - 0.5) * 0.5; // radians
                    Vector crooked = new Vector(hang.X * Math.Cos(tilt) - hang.Y * Math.Sin(tilt), hang.X * Math.Sin(tilt) + hang.Y * Math.Cos(tilt));
                    Canvas holder = Bulb(on + crooked * info.Bulb * 0.2, crooked, Colours[(index + seed) % Colours.Length], info.Bulb, info.Shape == "pointed");
                    canvas.Children.Add(holder);
                    Leds.Add(new Led((UIElement)holder.Children[^1], strands, index, random.NextDouble(), random.NextDouble()));
                    index++;
                }

                previous = end;
                travelled += span;
            }

            foreach (Led led in Leds.Where(l => l.Strand == strands))
            {
                led.Total = index - 1;
            }

            strands++;
            seed += 2;
        }

        if (info.Layout != "partial")
        {
            // outside the frame's bar: the swags hang from its outer edge, all around or (topbottom) only along the top and the bottom
            double o = 3.5;
            Strand(new Point(cornerW, -o), new Point(width - cornerW, -o), new Vector(0, 1), new Vector(0, 1));
            Strand(new Point(cornerW, height + o), new Point(width - cornerW, height + o), new Vector(0, 1), new Vector(0, 1));
            if (info.Layout != "topbottom")
            {
                Strand(new Point(-o, cornerH), new Point(-o, height - cornerH), new Vector(-1, 0), new Vector(-1, 0));
                Strand(new Point(width + o, cornerH), new Point(width + o, height - cornerH), new Vector(1, 0), new Vector(1, 0));
            }
        }
        else
        {
            // inside, a few swags only, the snow left bare in between
            double i = 4.5;
            double span = width - 2 * cornerW;
            Strand(new Point(cornerW + span * 0.04, i), new Point(cornerW + span * 0.40, i), new Vector(0, 1), new Vector(0, 1));
            Strand(new Point(cornerW + span * 0.58, i), new Point(cornerW + span * 0.93, i), new Vector(0, 1), new Vector(0, 1));
            Strand(new Point(cornerW + span * 0.10, height - i), new Point(cornerW + span * 0.44, height - i), new Vector(0, -1), new Vector(0, -1));
            Strand(new Point(cornerW + span * 0.60, height - i), new Point(cornerW + span * 0.90, height - i), new Vector(0, -1), new Vector(0, -1));
            double side = height - 2 * cornerH;
            Strand(new Point(i, cornerH + side * 0.08), new Point(i, cornerH + side * 0.40), new Vector(1, 0), new Vector(1, 0));
            Strand(new Point(width - i, cornerH + side * 0.55), new Point(width - i, cornerH + side * 0.92), new Vector(-1, 0), new Vector(-1, 0));
        }
    }

    /// <summary>One bulb at <paramref name="at"/>: a socket on the wire, the glass pointing along <paramref name="down"/>, dark when off, bright with a halo when on.</summary>
    private static Canvas Bulb(Point at, Vector down, Color colour, double size, bool pointed = false)
    {
        double glass = size, halo = size * 3.4;
        var holder = new Canvas { Tag = "bulb", IsHitTestVisible = false, Width = 0, Height = 0 };
        Canvas.SetLeft(holder, at.X);
        Canvas.SetTop(holder, at.Y);
        // everything is drawn pointing down, then turned to `down`
        double angle = Math.Atan2(down.X * -1, down.Y) * 180 / Math.PI;
        holder.RenderTransform = new RotateTransform(angle);

        // the glass: a round bulb, or a pointed one (a flame, a teardrop with its tip away from the socket) which is longer and a little narrower
        Geometry teardrop = Geometry.Parse("M -0.30,0 C -0.62,0.26 -0.46,0.74 0,1 C 0.46,0.74 0.62,0.26 0.30,0 Z");
        Shape Glass(Color fill, Color edge, double radiusScale = 1)
        {
            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.38, 0.32), Center = new Point(0.5, 0.5), RadiusX = 0.62, RadiusY = 0.62,
                GradientStops = { new GradientStop(Color.FromArgb(0xFF, 255, 255, 255), 0), new GradientStop(fill, 0.3 * radiusScale), new GradientStop(edge, 1) },
            };
            brush.Freeze();
            var outline = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x18));
            if (pointed)
            {
                double w = glass * 0.95, h = glass * 1.55;
                var drop = new Path { Data = teardrop, Fill = brush, Stroke = outline, StrokeThickness = 0.5, Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false };
                Canvas.SetLeft(drop, -w / 2);
                Canvas.SetTop(drop, size * 0.3);
                return drop;
            }

            var e = new Ellipse { Width = glass * 0.86, Height = glass, Fill = brush, Stroke = outline, StrokeThickness = 0.5, IsHitTestVisible = false };
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
        Canvas.SetTop(halo1, size * 0.32 + (pointed ? glass * 0.7 : glass / 2) - halo / 2);
        on.Children.Add(halo1);
        on.Children.Add(Glass(colour, Color.FromRgb((byte)(colour.R * 0.72), (byte)(colour.G * 0.72), (byte)(colour.B * 0.72)), 1.25));
        holder.Children.Add(on); // the last child: the pattern drives its opacity
        return holder;
    }
}
