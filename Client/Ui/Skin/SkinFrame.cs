using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        return grid;
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
