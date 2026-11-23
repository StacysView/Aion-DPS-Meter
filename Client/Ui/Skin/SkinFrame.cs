using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui.Skin;

/// <summary>
/// The frame of a skin, drawn from its 9 pieces: a 3 x 3 grid, the corners at their own size, the four edges stretching, the centre left empty (the panel's own background shows
/// through). The grid is placed so the frame's outer edge lies on the panel's edge, the ornaments overhanging outside it (negative margins by the skin's overhang): put it in a
/// grid that has the panel's size. Never takes a click.
/// </summary>
public static class SkinFrame
{
    /// <summary>The frame's grid, or null when the skin has no frame or a piece cannot be read.</summary>
    public static Grid? Build(SkinPack skin)
    {
        if (skin.Frame is not SkinFrameInfo frame)
        {
            return null;
        }

        Grid? grid = Assemble(skin, frame, frame.Pieces, true);
        if (grid is null)
        {
            return null;
        }

        AddGlows(skin, frame, grid);
        // the little lights, like a real string of Christmas lights (Stacy, 2026-11-02): each layer is the frame again with a group of bulbs lit; the groups are interleaved along each string
        // (every other bulb) and light up in turn, sharply, as the bulbs of a real string do - two groups: one on while the other is off
        int count = frame.Lights.Count;
        for (int index = 0; index < count; index++)
        {
            SkinLightLayer layer = frame.Lights[index];
            if (Assemble(skin, frame, layer.Pieces, false) is not Grid lit)
            {
                continue;
            }

            Grid.SetRowSpan(lit, 3);
            Grid.SetColumnSpan(lit, 3);
            grid.Children.Add(lit);
            double cycle = layer.Period, slot = cycle / count;
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(cycle), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(index == 0 ? 1 : 0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (index > 0)
            {
                blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(slot * index))));
            }

            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(slot * (index + 1) - 0.0001))));
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(index == 0 ? 1 : 0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle))));
            lit.BeginAnimation(UIElement.OpacityProperty, blink);
        }

        return grid;
    }

    /// <summary>The 3 x 3 grid of a set of pieces (the frame's own, or one of the lit layers'); null when a piece cannot be read.</summary>
    private static Grid? Assemble(SkinPack skin, SkinFrameInfo frame, IReadOnlyDictionary<string, string> files, bool withMargin)
    {
        BitmapSource? Piece(string key) => files.TryGetValue(key, out string? file) ? skin.Bitmap(file) : null;
        BitmapSource?[] pieces = { Piece("tl"), Piece("t"), Piece("tr"), Piece("l"), Piece("r"), Piece("bl"), Piece("b"), Piece("br") };
        if (pieces.Any(p => p is null))
        {
            return null;
        }

        BitmapSource tl = pieces[0]!, t = pieces[1]!, tr = pieces[2]!, l = pieces[3]!, r = pieces[4]!, bl = pieces[5]!, b = pieces[6]!, br = pieces[7]!;
        double ppp = frame.Ppp;
        BitmapScalingMode mode = frame.PixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        var grid = new Grid
        {
            IsHitTestVisible = false, Margin = withMargin ? new Thickness(-frame.Overhang.Left, -frame.Overhang.Top, -frame.Overhang.Right, -frame.Overhang.Bottom) : new Thickness(0),
            SnapsToDevicePixels = false, UseLayoutRounding = false,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(tl.PixelWidth * ppp) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(tr.PixelWidth * ppp) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tl.PixelHeight * ppp) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(bl.PixelHeight * ppp) });
        Add(grid, tl, 0, 0, mode);
        if (frame.TileEdges)
        {
            TileAcross(grid, t, 0, 1, ppp, mode);
        }
        else
        {
            Add(grid, t, 0, 1, mode);
        }

        Add(grid, tr, 0, 2, mode);
        if (frame.TileSides)
        {
            Tile(grid, l, 1, 0, ppp, mode);
            Tile(grid, r, 1, 2, ppp, mode);
        }
        else
        {
            Add(grid, l, 1, 0, mode);
            Add(grid, r, 1, 2, mode);
        }
        Add(grid, bl, 2, 0, mode);
        if (frame.TileEdges)
        {
            TileAcross(grid, b, 2, 1, ppp, mode);
        }
        else
        {
            Add(grid, b, 2, 1, mode);
        }

        Add(grid, br, 2, 2, mode);
        return grid;
    }

    /// <summary>
    /// The decorations outside the frame, each at its corner of the panel and pushed out of it, with a slow sway, a flickering light or a twinkle when the skin asks for them.
    /// <paramref name="outward"/> is how far they go out of the panel on each side (the room the window must give them). Never take a click.
    /// </summary>
    public static IReadOnlyList<FrameworkElement> Decos(SkinPack skin, out Thickness outward)
    {
        var list = new List<FrameworkElement>();
        Creatures.Clear();
        double left = 0, top = 0, right = 0, bottom = 0;
        var random = new Random(5);
        foreach (SkinDeco deco in skin.Decos)
        {
            if (skin.Bitmap(deco.File) is not BitmapSource bitmap)
            {
                continue;
            }

            bool isLeft = deco.Anchor is "tl" or "bl", isTop = deco.Anchor is "tl" or "tr";
            double side = deco.Along - deco.Out;
            var holder = new Grid
            {
                Width = deco.W, Height = deco.H, IsHitTestVisible = false,
                HorizontalAlignment = isLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = isTop ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = new Thickness(isLeft ? side : 0, isTop ? -deco.Down : 0, isLeft ? 0 : side, isTop ? 0 : -deco.Down),
            };
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            holder.Children.Add(image);
            if (deco.Light is Point light && skin.Get("lueur_lanterne") is (SkinImage, BitmapSource halo))
            {
                double size = deco.LightSize * deco.W;
                var glow = new Image
                {
                    Source = halo, Width = size, Height = size, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(light.X * deco.W - size / 2, light.Y * deco.H - size / 2, 0, 0),
                };
                holder.Children.Add(glow);
                glow.BeginAnimation(UIElement.OpacityProperty, Flicker(random));
            }

            if (deco.Twinkle)
            {
                image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(1.4 + random.NextDouble()))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }

            var motion = new TransformGroup();
            if (deco.Sway > 0)
            {
                var turn = new RotateTransform(0);
                holder.RenderTransformOrigin = deco.Pivot;
                motion.Children.Add(turn);
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-deco.Sway, deco.Sway, TimeSpan.FromSeconds(2.6 + random.NextDouble()))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }

            if (deco.Bob > 0)
            {
                // a creature of the Monsters skin hops: up and down, a smaller hop, then it rests until the next one (each at its own pace, Stacy 2026-11-21: "les animer légèrement")
                var hop = new TranslateTransform();
                motion.Children.Add(hop);
                var jump = HopAnimation(deco.Bob, deco.BobSeconds, deco.BobDelay);
                hop.BeginAnimation(TranslateTransform.YProperty, jump);
            }

            if (motion.Children.Count > 0)
            {
                holder.RenderTransform = motion;
            }

            Daily(image, bitmap, skin.Variants(deco.File), list.Count, deco);
            if (deco.Bob > 0 && skin.Get("etincelle") is (SkinImage, BitmapSource sparkleBitmap))
            {
                Creatures.Add(new Creature(image, holder, sparkleBitmap));
            }

            double outSide = Math.Max(0, deco.Out - deco.Along), outVertical = Math.Max(0, deco.Down) + (isTop ? deco.Bob : 0); // a hop goes up: the window gives room for it
            if (isLeft)
            {
                left = Math.Max(left, outSide);
            }
            else
            {
                right = Math.Max(right, outSide);
            }

            if (isTop)
            {
                top = Math.Max(top, outVertical);
            }
            else
            {
                bottom = Math.Max(bottom, outVertical);
            }

            list.Add(holder);
        }

        outward = new Thickness(left, top, right, bottom);
        return list;
    }

    private static DoubleAnimationUsingKeyFrames Flicker(Random random)
    {
        var flicker = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        double t = 0;
        for (int i = 0; i < 14; i++)
        {
            t += 0.08 + random.NextDouble() * 0.22;
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0.45 + random.NextDouble() * 0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }

        flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t + 0.15))));
        return flicker;
    }

    /// <summary>The frame's lights (Stacy, 2026-10-30: the lanterns of the Noël skin "anime"): a warm halo on each, whose brightness flickers like a flame, each at its own pace.</summary>
    private static void AddGlows(SkinPack skin, SkinFrameInfo frame, Grid grid)
    {
        if (frame.Glows.Count == 0 || skin.Get("lueur_lanterne") is not (SkinImage, BitmapSource halo))
        {
            return;
        }

        var random = new Random(7);
        foreach (SkinGlow glow in frame.Glows)
        {
            (int row, int column) = glow.Corner switch { "tl" => (0, 0), "tr" => (0, 2), "bl" => (2, 0), _ => (2, 2) };
            var canvas = new Canvas { IsHitTestVisible = false };
            Grid.SetRow(canvas, row);
            Grid.SetColumn(canvas, column);
            var image = new Image { Source = halo, Width = glow.Size, Height = glow.Size, IsHitTestVisible = false, Opacity = 0.6 };
            Canvas.SetLeft(image, glow.X - glow.Size / 2);
            Canvas.SetTop(image, glow.Y - glow.Size / 2);
            canvas.Children.Add(image);
            grid.Children.Add(canvas);
            image.BeginAnimation(UIElement.OpacityProperty, Flicker(random));
        }
    }

    /// <summary>A side piece repeated down the side at its own size (a garland's strand keeps its needles instead of being drawn out).</summary>
    /// <summary>A creature of the hopping row, for the shiny moment.</summary>
    internal sealed record Creature(Image Image, Grid Holder, BitmapSource Sparkle);

    /// <summary>The creatures of the skin drawn last (the four of the Monsters console), in the order of the skin.</summary>
    internal static readonly List<Creature> Creatures = new();

    /// <summary>The colours of a picture turned half way round the colour wheel, for a creature's shiny form; the greys, whites and blacks keep theirs (the eyes, the outline).</summary>
    internal static BitmapSource Shinify(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (pixels[i + 3] == 0 || max - min < 28)
            {
                continue; // transparent, or grey: left alone
            }

            double hue;
            double d = max - min;
            if (max == r)
            {
                hue = ((g - b) / d) % 6;
            }
            else if (max == g)
            {
                hue = (b - r) / d + 2;
            }
            else
            {
                hue = (r - g) / d + 4;
            }

            hue = (hue * 60 + 150 + 360) % 360;
            double sat = max == 0 ? 0 : d / max, val = max / 255.0;
            sat = Math.Min(1, sat * 1.1);
            double c = val * sat, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = val - c;
            (double rr, double gg, double bb) = hue switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
            pixels[i + 2] = (byte)Math.Round((rr + m) * 255);
            pixels[i + 1] = (byte)Math.Round((gg + m) * 255);
            pixels[i] = (byte)Math.Round((bb + m) * 255);
        }

        var result = BitmapSource.Create(w, h, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// A creature shines (Stacy, 2026-11-23: "vraiment rare, une fois par jour max, que ça se voie de fou, 5-6 secondes, de beaux effets sans trop en faire"): for about five and a half seconds it
    /// takes its shiny colours under a golden halo that swells and ebbs, a white flash opens the moment, and a dozen little stars blink round it one after the other. Everything is put back at the end.
    /// </summary>
    internal static void ShineMoment(int index)
    {
        if (index < 0 || index >= Creatures.Count)
        {
            return;
        }

        Creature creature = Creatures[index];
        Image image = creature.Image;
        BitmapSource original = (BitmapSource)image.Source;
        var halo = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD2, 0x4A), BlurRadius = 6, ShadowDepth = 0, Opacity = 1 };
        image.Source = Shinify(original);
        image.Effect = halo;
        // the halo swells and ebbs, brighter at the start (the flash)
        var swell = new DoubleAnimationUsingKeyFrames();
        swell.KeyFrames.Add(new LinearDoubleKeyFrame(26, TimeSpan.Zero));
        swell.KeyFrames.Add(new LinearDoubleKeyFrame(7, TimeSpan.FromSeconds(0.35)));
        for (int i = 0; i < 4; i++)
        {
            swell.KeyFrames.Add(new LinearDoubleKeyFrame(15, TimeSpan.FromSeconds(1.0 + i * 1.1)));
            swell.KeyFrames.Add(new LinearDoubleKeyFrame(8, TimeSpan.FromSeconds(1.55 + i * 1.1)));
        }

        swell.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(5.5)));
        halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, swell);
        // the little stars, one after the other, at random places round the creature
        var random = new Random();
        var stars = new List<Image>();
        for (int i = 0; i < 12; i++)
        {
            double size = 7 + random.NextDouble() * 6;
            var grow = new ScaleTransform(0.2, 0.2);
            var star = new Image
            {
                Source = creature.Sparkle, Width = size, Height = size, IsHitTestVisible = false, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-size / 2 + random.NextDouble() * creature.Holder.Width, -size / 2 + random.NextDouble() * creature.Holder.Height, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = grow,
            };
            RenderOptions.SetBitmapScalingMode(star, BitmapScalingMode.HighQuality);
            creature.Holder.Children.Add(star);
            stars.Add(star);
            TimeSpan begin = TimeSpan.FromSeconds(0.3 + i * 0.4 + random.NextDouble() * 0.2);
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(0.15)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(0.65)));
            var scale = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(1.3, TimeSpan.FromSeconds(0.22)));
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, TimeSpan.FromSeconds(0.65)));
            star.BeginAnimation(UIElement.OpacityProperty, fade);
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        var end = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5.6) };
        end.Tick += (_, _) =>
        {
            end.Stop();
            image.Effect = null;
            image.Source = original;
            foreach (Image star in stars)
            {
                creature.Holder.Children.Remove(star);
            }
        };
        end.Start();
    }

    /// <summary>A creature with several pictures shows one a day (the day of the year picks it, each creature at a step of its own, Stacy 2026-11-23: "faire changer tous les jours"); the meter left
    /// open overnight changes it at midnight, with a quick fade.</summary>
    private static void Daily(Image image, BitmapSource first, IReadOnlyList<BitmapSource> others, int slot, SkinDeco deco)
    {
        if (deco.Bob <= 0 || others.Count == 0)
        {
            return; // only the creatures that hop have several pictures; the type coins of the bottom band keep theirs
        }

        var all = new List<BitmapSource> { first };
        all.AddRange(others);
        int Today() => (DateTime.Now.DayOfYear + slot * 3) % all.Count;
        int shown = Today();
        image.Source = all[shown];
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) =>
        {
            int now = Today();
            if (now == shown)
            {
                return;
            }

            shown = now;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (_, _) =>
            {
                image.Source = all[now];
                image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            };
            image.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        image.Loaded += (_, _) => timer.Start();
        image.Unloaded += (_, _) => timer.Stop();
    }

    /// <summary>A creature's hop: up, down, a smaller hop, then a rest to the end of its round (<paramref name="seconds"/>), after <paramref name="delay"/> seconds.</summary>
    internal static DoubleAnimationUsingKeyFrames HopAnimation(double bob, double seconds, double delay)
    {
        double rest = Math.Max(1.0, seconds);
        var jump = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(rest), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
        jump.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        jump.KeyFrames.Add(new EasingDoubleKeyFrame(-bob, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.16)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        jump.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.32)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        jump.KeyFrames.Add(new EasingDoubleKeyFrame(-bob * 0.45, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.43)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        jump.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.54)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        jump.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(rest))));
        return jump;
    }

    /// <summary>The flash of a shining creature's star: invisible for most of the round, then grows and fades in two thirds of a second. <paramref name="manual"/>: no start (a test seeks it).</summary>
    internal static (DoubleAnimationUsingKeyFrames Opacity, DoubleAnimationUsingKeyFrames Scale) ShineAnimations(Image star, ScaleTransform scale, double period, double delay, bool manual)
    {
        double round = Math.Max(2.0, period);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(round), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.18))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.34))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.68))));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(round))));
        var grow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(round), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
        grow.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        grow.KeyFrames.Add(new LinearDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.24))));
        grow.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.68))));
        grow.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(round))));
        if (!manual)
        {
            star.BeginAnimation(UIElement.OpacityProperty, fade);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        return (fade, grow);
    }

    private static void Tile(Grid grid, BitmapSource source, int row, int column, double ppp, BitmapScalingMode mode)
    {
        var brush = new ImageBrush(source)
        {
            TileMode = TileMode.FlipY, Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, AlignmentY = AlignmentY.Top,
            Viewport = new Rect(0, 0, source.PixelWidth * ppp, source.PixelHeight * ppp),
        };
        RenderOptions.SetBitmapScalingMode(brush, mode);
        var side = new System.Windows.Shapes.Rectangle { Fill = brush, IsHitTestVisible = false };
        Grid.SetRow(side, row);
        Grid.SetColumn(side, column);
        grid.Children.Add(side);
    }

    /// <summary>A top or bottom piece repeated along its edge, mirrored so that no seam shows.</summary>
    private static void TileAcross(Grid grid, BitmapSource source, int row, int column, double ppp, BitmapScalingMode mode)
    {
        var brush = new ImageBrush(source)
        {
            TileMode = TileMode.FlipX, Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, AlignmentX = AlignmentX.Left,
            Viewport = new Rect(0, 0, source.PixelWidth * ppp, source.PixelHeight * ppp),
        };
        RenderOptions.SetBitmapScalingMode(brush, mode);
        var edge = new System.Windows.Shapes.Rectangle { Fill = brush, IsHitTestVisible = false };
        Grid.SetRow(edge, row);
        Grid.SetColumn(edge, column);
        grid.Children.Add(edge);
    }

    private static void Add(Grid grid, BitmapSource source, int row, int column, BitmapScalingMode mode)
    {
        var image = new Image { Source = source, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, mode);
        Grid.SetRow(image, row);
        Grid.SetColumn(image, column);
        grid.Children.Add(image);
    }

    /// <summary>A named image of the skin at its default size (or <paramref name="width"/> x its ratio), or null.</summary>
    public static Image? Picture(SkinPack skin, string name, double? width = null)
    {
        if (skin.Get(name) is not (SkinImage info, BitmapSource bitmap))
        {
            return null;
        }

        double w = width ?? info.W;
        var image = new Image { Source = bitmap, Width = w, Height = w * info.H / Math.Max(0.01, info.W), Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
}
