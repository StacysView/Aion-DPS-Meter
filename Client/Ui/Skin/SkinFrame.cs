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

        BitmapSource? Piece(string key) => frame.Pieces.TryGetValue(key, out string? file) ? skin.Bitmap(file) : null;
        BitmapSource?[] pieces = { Piece("tl"), Piece("t"), Piece("tr"), Piece("l"), Piece("r"), Piece("bl"), Piece("b"), Piece("br") };
        if (pieces.Any(p => p is null))
        {
            return null;
        }

        BitmapSource tl = pieces[0]!, t = pieces[1]!, tr = pieces[2]!, l = pieces[3]!, r = pieces[4]!, bl = pieces[5]!, b = pieces[6]!, br = pieces[7]!;
        double ppp = frame.Ppp;
        var grid = new Grid
        {
            IsHitTestVisible = false, Margin = new Thickness(-frame.Overhang.Left, -frame.Overhang.Top, -frame.Overhang.Right, -frame.Overhang.Bottom),
            SnapsToDevicePixels = false, UseLayoutRounding = false,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(tl.PixelWidth * ppp) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(tr.PixelWidth * ppp) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tl.PixelHeight * ppp) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(bl.PixelHeight * ppp) });
        Add(grid, tl, 0, 0);
        Add(grid, t, 0, 1);
        Add(grid, tr, 0, 2);
        Add(grid, l, 1, 0);
        Add(grid, r, 1, 2);
        Add(grid, bl, 2, 0);
        Add(grid, b, 2, 1);
        Add(grid, br, 2, 2);
        AddGlows(skin, frame, grid);
        return grid;
    }

    /// <summary>
    /// The decorations outside the frame, each at its corner of the panel and pushed out of it, with a slow sway, a flickering light or a twinkle when the skin asks for them.
    /// <paramref name="outward"/> is how far they go out of the panel on each side (the room the window must give them). Never take a click.
    /// </summary>
    public static IReadOnlyList<FrameworkElement> Decos(SkinPack skin, out Thickness outward)
    {
        var list = new List<FrameworkElement>();
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

            if (deco.Sway > 0)
            {
                var turn = new RotateTransform(0);
                holder.RenderTransformOrigin = deco.Pivot;
                holder.RenderTransform = turn;
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-deco.Sway, deco.Sway, TimeSpan.FromSeconds(2.6 + random.NextDouble()))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }

            double outSide = Math.Max(0, deco.Out - deco.Along), outVertical = Math.Max(0, deco.Down);
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

    private static void Add(Grid grid, BitmapSource source, int row, int column)
    {
        var image = new Image { Source = source, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
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
