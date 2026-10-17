using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The pet map over the game: a square of <see cref="MeterSettings.PetMapSize"/> px of the game's world map around
/// the player (1 px = 1 m at 100 %), his purple arrow in the middle and a dot in each needed pet's colour on its
/// monsters' spawn points. Unlocked, the wheel zooms (50 to 200 %) and a hovered dot names its pet. While the place is
/// unknown the map is centred on the zone's spawn points; where there is no map it says so. Map pictures: NCSoft
/// (Aion 2), exported by Stacy; spawn points: SkeeveTV (MIT), whose PetMapWindow gave the idea.
/// </summary>
public sealed class PetMapPanel : PetPanelWindow
{
    private const double DefaultSize = 260;

    private readonly TextBlock _title = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock _zoom = new() { FontSize = 11, Foreground = MutedText, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _hint = new() { FontSize = 10.5, Foreground = MutedText, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    private readonly PetMapView _view = new();

    public PetMapPanel() : base("Aion DPS - pet map")
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 5), LastChildFill = true };
        DockPanel.SetDock(_zoom, Dock.Right);
        header.Children.Add(_zoom);
        header.Children.Add(_title);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_view);
        stack.Children.Add(_hint);
        Body = stack;
        _view.ZoomChanged += zoom => _zoom.Text = string.Format(CultureInfo.CurrentCulture, "{0:0} %", zoom * 100);
        // The wheel's zoom is kept for the next start (load-modify-save, like a drag).
        _view.ZoomedByWheel += zoom => SaveSettings(s => s.PetMapZoom = zoom);
        SetSize(DefaultSize);
    }

    /// <summary>The map's side as last set (the list panel goes below it by default).</summary>
    public double MapSize { get; private set; } = DefaultSize;

    /// <summary>What the map draws (read by the smoke harness).</summary>
    public PetMapView View => _view;

    public void SetSize(double size)
    {
        MapSize = Math.Clamp(double.IsFinite(size) ? size : DefaultSize, 160, 600);
        _view.Width = MapSize;
        _view.Height = MapSize;
        _title.MaxWidth = MapSize - 50;
        _hint.MaxWidth = MapSize;
    }

    public void PlaceAt(double? left, double? top)
    {
        Point place = left is double l && top is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
        Left = place.X;
        Top = place.Y;
    }

    /// <summary>Shows a model. It may throw on something unforeseen: <see cref="PetPanelSet.Refresh"/> catches it.</summary>
    public void ShowModel(TrackerModel model, bool otherMap)
    {
        _title.Text = model.Map is null ? Loc("Pets.Title") : model.ZoneName;
        _view.SetModel(model, otherMap);
    }

    protected override void OnLockedChanged(bool locked)
    {
        _view.Interactive = !locked;
        if (locked)
        {
            _view.ClearHover(); // a click-through panel gets no mouse-leave: the hovered dot would stay lit
        }

        _zoom.Text = string.Format(CultureInfo.CurrentCulture, "{0:0} %", _view.Zoom * 100);
        _zoom.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _hint.Text = Loc("PetPanels.Unlocked");
        _hint.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.PetMapLeft = left;
        s.PetMapTop = top;
    });

    protected override Point DefaultPlace(Rect workArea) => new(workArea.Right - MapSize - 40, workArea.Top + 120);
}

/// <summary>The drawing of the pet map: tiles, dots, the player's arrow and the states without a place or a map.</summary>
public sealed class PetMapView : FrameworkElement
{
    private const double MinZoom = 0.5, MaxZoom = 2.0;
    private const double DotRadius = 3.5;

    private static readonly Brush Water = PetLook.Frozen(Color.FromRgb(0x0E, 0x15, 0x1C));
    private static readonly Brush ArrowFill = PetLook.Frozen(Color.FromRgb(0x7B, 0x5C, 0xF0));
    // A dark disc ringed in purple under the arrow, and a dark edge around its white outline: it stays readable on the
    // purple flowers of the Forest of Exiles as on the red earth of Drana.
    private static readonly Brush ArrowHalo = PetLook.Frozen(Color.FromArgb(0x90, 0x0B, 0x10, 0x16));
    private static readonly Pen ArrowRing = FrozenPen(PetLook.Frozen(Color.FromArgb(0xE0, 0x9D, 0x85, 0xFF)), 1.5);
    private static readonly Pen ArrowShadow = FrozenPen(PetLook.Frozen(Color.FromArgb(0xD0, 0x05, 0x08, 0x0C)), 3.6);
    private static readonly Pen ArrowPen = FrozenPen(Brushes.White, 1.5);
    private static readonly Pen DotPen = FrozenPen(PetLook.Frozen(Color.FromArgb(0xC0, 0x05, 0x08, 0x0C)), 1.2);
    private static readonly Pen FramePen = FrozenPen(PetLook.Frozen(Color.FromRgb(0x2A, 0x38, 0x46)), 1);
    private static readonly Brush PillFill = PetLook.Frozen(Color.FromArgb(0xD8, 0x14, 0x1C, 0x24));
    private static readonly Brush MutedText = PetLook.Frozen(Color.FromRgb(0x8F, 0xA3, 0xB3));
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly Dictionary<(string Map, int Col, int Row), BitmapSource?> _tiles = new();
    private readonly Dictionary<int, Brush> _dotBrushes = new();
    private TrackerModel? _model;
    private bool _otherMap;
    private (double X, double Y)? _lastPlayer;
    private double _heading;
    private TrackerDot? _hovered;

    public PetMapView()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public event Action<double>? ZoomChanged;

    /// <summary>The player zoomed with the wheel (not raised by <see cref="SetZoom"/>): the panel saves it.</summary>
    public event Action<double>? ZoomedByWheel;

    /// <summary>True when the last drawing failed and the "no map" state was drawn instead (for the smoke harness).</summary>
    public bool RenderFailed { get; private set; }

    /// <summary>View pixels per metre: 1 at 100 %.</summary>
    public double Zoom { get; private set; } = 1.0;

    /// <summary>Unlocked: the wheel zooms and a hovered dot names its pet.</summary>
    public bool Interactive { get; set; }

    /// <summary>The map pixel in the middle of the view: the player, else the middle of the zone's dots, else the map's.</summary>
    public (double X, double Y)? Centre { get; private set; }

    /// <summary>How many tile pictures and dots the last drawing put in the view (for the smoke harness).</summary>
    public int TilesDrawn { get; private set; }

    public int DotsDrawn { get; private set; }

    public void SetModel(TrackerModel model, bool otherMap)
    {
        if (model.Player is { } p)
        {
            if (_lastPlayer is { } before && model.Map?.Key == _model?.Map?.Key)
            {
                double dx = p.X - before.X, dy = p.Y - before.Y;
                if (dx * dx + dy * dy > 9)
                {
                    _heading = Math.Atan2(dx, -dy) * 180 / Math.PI; // the way he moved, clockwise from north
                }
            }

            _lastPlayer = p;
        }

        _model = model;
        _dotBrushes.Clear(); // the hues go by rank in this model's pets
        _otherMap = otherMap;
        _hovered = null;
        InvalidateVisual();
    }

    /// <summary>Forgets the hovered dot and its name label.</summary>
    public void ClearHover()
    {
        if (_hovered is not null)
        {
            _hovered = null;
            InvalidateVisual();
        }
    }

    /// <summary>Sets the zoom (0.5 to 2), as the wheel does.</summary>
    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(double.IsFinite(zoom) ? zoom : 1.0, MinZoom, MaxZoom);
        ZoomChanged?.Invoke(Zoom);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Interactive)
        {
            return;
        }

        SetZoom(Zoom * (e.Delta > 0 ? 1.25 : 0.8));
        ZoomedByWheel?.Invoke(Zoom);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        TrackerDot? near = Interactive ? DotAt(e.GetPosition(this)) : null;
        if (!Equals(near, _hovered))
        {
            _hovered = near;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hovered is not null)
        {
            _hovered = null;
            InvalidateVisual();
        }
    }

    /// <summary>The dot under a point of the view (within 7 px), the nearest one.</summary>
    public TrackerDot? DotAt(Point point)
    {
        if (_model?.Map is not { } map || Centre is not { } centre)
        {
            return null;
        }

        double k = Zoom * map.MetresPerPixel, half = ActualWidth / 2, halfY = ActualHeight / 2;
        TrackerDot? best = null;
        double bestDistance = 49;
        foreach (TrackerDot dot in _model.Dots)
        {
            double x = half + (dot.PixelX - centre.X) * k - point.X, y = halfY + (dot.PixelY - centre.Y) * k - point.Y;
            double d = x * x + y * y;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = dot;
            }
        }

        return best;
    }

    /// <summary>Draws into a drawing of its own first: anything unforeseen (a broken tile, odd data) draws the "no map"
    /// state instead of throwing out of the render pass, which would end the meter.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        var drawing = new DrawingGroup();
        try
        {
            using (DrawingContext inner = drawing.Open())
            {
                RenderMap(inner);
            }

            RenderFailed = false;
            dc.DrawDrawing(drawing);
        }
        catch (Exception)
        {
            RenderFailed = true;
            TilesDrawn = 0;
            DotsDrawn = 0;
            Centre = null;
            try
            {
                RenderNoMap(dc, "PetPanels.NoMap", "PetPanels.NoMapHint");
            }
            catch (Exception)
            {
                // Not even the text: the view stays empty, the meter runs on.
            }
        }
    }

    private void RenderNoMap(DrawingContext dc, string title, string hint)
    {
        double w = ActualWidth, h = ActualHeight;
        var bounds = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(bounds, 7, 7));
        dc.DrawRectangle(Water, null, bounds);
        DrawCentredText(dc, Loc(title), 13, Brushes.White, w, h / 2 - 18);
        DrawCentredText(dc, Loc(hint), 11, MutedText, w, h / 2 + 4);
        dc.Pop();
        dc.DrawRoundedRectangle(null, FramePen, new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)), 7, 7);
    }

    private void RenderMap(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        TilesDrawn = 0;
        DotsDrawn = 0;

        if (_model?.Map is not { } map)
        {
            Centre = null;
            // No zone known yet (a start in the middle of a session) is not the same as a zone without a map.
            bool unknown = _model?.ZoneUnknown == true;
            RenderNoMap(dc, unknown ? "PetPanels.ZoneUnknown" : "PetPanels.NoMap", unknown ? "PetPanels.ZoneUnknownHint" : "PetPanels.NoMapHint");
            return;
        }

        var bounds = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(bounds, 7, 7));
        dc.DrawRectangle(Water, null, bounds);

        double k = Zoom * map.MetresPerPixel; // view pixels per map pixel
        (double X, double Y) centre = _model.Player ?? DensestDots(_model.Dots, w / k) ?? (map.MapPixels / 2.0, map.MapPixels / 2.0);
        Centre = centre;
        DrawTiles(dc, map, centre, k, w, h);
        DrawDots(dc, centre, k, w, h);

        if (_model.Player is not null)
        {
            DrawArrow(dc, w / 2, h / 2);
        }
        else
        {
            DrawPill(dc, Loc(_otherMap ? "PetPanels.ElsewhereMap" : "PetPanels.UnknownPlace"), w, h - 26);
        }

        if (_hovered is { } dot && _model.Pets.FirstOrDefault(p => p.PetId == dot.PetId) is { } pet)
        {
            DrawLabel(dc, pet.Name, w / 2 + (dot.PixelX - centre.X) * k, h / 2 + (dot.PixelY - centre.Y) * k, w);
        }

        dc.Pop();
        dc.DrawRoundedRectangle(null, FramePen, new Rect(0.5, 0.5, w - 1, h - 1), 7, 7);
    }

    private void DrawTiles(DrawingContext dc, Aion2WorldMap map, (double X, double Y) centre, double k, double w, double h)
    {
        int tile = map.TilePixels;
        double halfX = w / 2 / k, halfY = h / 2 / k;
        int c0 = Math.Max(0, (int)Math.Floor((centre.X - halfX) / tile)), c1 = Math.Min(map.Grid - 1, (int)Math.Floor((centre.X + halfX) / tile));
        int r0 = Math.Max(0, (int)Math.Floor((centre.Y - halfY) / tile)), r1 = Math.Min(map.Grid - 1, (int)Math.Floor((centre.Y + halfY) / tile));
        var keep = new HashSet<(string, int, int)>();
        // Each edge is rounded once and shared by the two tiles on either side of it, and every tile reaches 1 px over
        // the next one (drawn after it): an edge that still falls between two screen pixels blends with the next tile,
        // never with the dark background, so no seam line shows.
        double Edge(double mapPixel, double middle, double half) => Math.Round(half + (mapPixel - middle) * k);
        for (int col = c0; col <= c1; col++)
        {
            for (int row = r0; row <= r1; row++)
            {
                keep.Add((map.Key, col, row));
                if (Tile(map, col, row) is not { } bitmap)
                {
                    continue;
                }

                double x0 = Edge(col * tile, centre.X, w / 2), x1 = Edge((col + 1) * tile, centre.X, w / 2);
                double y0 = Edge(row * tile, centre.Y, h / 2), y1 = Edge((row + 1) * tile, centre.Y, h / 2);
                dc.DrawImage(bitmap, new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
                TilesDrawn++;
            }
        }

        // The cache keeps the tiles in view and the 9 around the centre's tile; the others are let go.
        int cc = (int)Math.Floor(centre.X / tile), cr = (int)Math.Floor(centre.Y / tile);
        for (int dc2 = -1; dc2 <= 1; dc2++)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                keep.Add((map.Key, cc + dc2, cr + dr));
            }
        }

        foreach (var key in _tiles.Keys.Where(key => !keep.Contains(key)).ToList())
        {
            _tiles.Remove(key);
        }
    }

    private void DrawDots(DrawingContext dc, (double X, double Y) centre, double k, double w, double h)
    {
        double radius = DotRadius * Math.Clamp(Math.Sqrt(Zoom), 0.85, 1.25);
        TrackerDot? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (TrackerDot dot in _model!.Dots)
        {
            double x = w / 2 + (dot.PixelX - centre.X) * k, y = h / 2 + (dot.PixelY - centre.Y) * k;
            if (x < -radius || y < -radius || x > w + radius || y > h + radius)
            {
                double d = (x - w / 2) * (x - w / 2) + (y - h / 2) * (y - h / 2);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = dot;
                }

                continue;
            }

            bool hovered = Equals(dot, _hovered);
            dc.DrawEllipse(DotBrush(dot.PetId), DotPen, new Point(x, y), hovered ? radius + 1.5 : radius, hovered ? radius + 1.5 : radius);
            DotsDrawn++;
        }

        // Nothing in view: a small triangle at the edge points to the nearest dot, in its pet's colour.
        if (DotsDrawn == 0 && nearest is not null)
        {
            double dx = (nearest.PixelX - centre.X) * k, dy = (nearest.PixelY - centre.Y) * k;
            double scale = Math.Min((w / 2 - 12) / Math.Max(Math.Abs(dx), 1e-6), (h / 2 - 12) / Math.Max(Math.Abs(dy), 1e-6));
            var tip = new Point(w / 2 + dx * scale, h / 2 + dy * scale);
            double angle = Math.Atan2(dy, dx);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext g = geometry.Open())
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(new Point(tip.X - 11 * Math.Cos(angle - 0.5), tip.Y - 11 * Math.Sin(angle - 0.5)), true, false);
                g.LineTo(new Point(tip.X - 11 * Math.Cos(angle + 0.5), tip.Y - 11 * Math.Sin(angle + 0.5)), true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(DotBrush(nearest.PetId), DotPen, geometry);
        }
    }

    private void DrawArrow(DrawingContext dc, double x, double y)
    {
        dc.DrawEllipse(ArrowHalo, ArrowRing, new Point(x, y), 13, 13);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(0, -9), true, true);
            g.LineTo(new Point(6.5, 7), true, true);
            g.LineTo(new Point(0, 3.5), true, true);
            g.LineTo(new Point(-6.5, 7), true, true);
        }

        var transform = new TransformGroup();
        transform.Children.Add(new RotateTransform(_heading));
        transform.Children.Add(new TranslateTransform(x, y));
        geometry.Transform = transform;
        geometry.Freeze();
        dc.DrawGeometry(null, ArrowShadow, geometry);
        dc.DrawGeometry(ArrowFill, ArrowPen, geometry);
    }

    private void DrawPill(DrawingContext dc, string text, double w, double y)
    {
        FormattedText formatted = Text(text, 11.5, Brushes.White);
        double pw = formatted.Width + 20, ph = formatted.Height + 6;
        dc.DrawRoundedRectangle(PillFill, FramePen, new Rect((w - pw) / 2, y, pw, ph), ph / 2, ph / 2);
        dc.DrawText(formatted, new Point((w - formatted.Width) / 2, y + 3));
    }

    private void DrawLabel(DrawingContext dc, string text, double x, double y, double w)
    {
        FormattedText formatted = Text(text, 11.5, Brushes.White);
        double lw = formatted.Width + 12, lh = formatted.Height + 4;
        double left = Math.Clamp(x - lw / 2, 2, Math.Max(2, w - lw - 2)), top = y - lh - 8;
        if (top < 2)
        {
            top = y + 8;
        }

        dc.DrawRoundedRectangle(PillFill, FramePen, new Rect(left, top, lw, lh), 4, 4);
        dc.DrawText(formatted, new Point(left + 6, top + 2));
    }

    private void DrawCentredText(DrawingContext dc, string text, double size, Brush brush, double w, double y)
    {
        FormattedText formatted = Text(text, size, brush);
        formatted.MaxTextWidth = Math.Max(20, w - 24);
        formatted.TextAlignment = TextAlignment.Center;
        dc.DrawText(formatted, new Point(12, y));
    }

    private FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush DotBrush(int petId)
    {
        if (!_dotBrushes.TryGetValue(petId, out Brush? brush))
        {
            int rank = _model?.Pets is { } pets ? pets.ToList().FindIndex(p => p.PetId == petId) : -1;
            _dotBrushes[petId] = brush = PetLook.Frozen(PetLook.DotColor(rank < 0 ? petId : rank));
        }

        return brush;
    }

    /// <summary>Where the place is unknown: the middle of the view-sized square of the map that holds the most dots
    /// (the zone's spawn points), so the map opens on something to hunt, not on the middle of two far groups.</summary>
    private static (double X, double Y)? DensestDots(IReadOnlyList<TrackerDot> dots, double cell)
    {
        if (dots.Count == 0 || !(cell > 0))
        {
            return null;
        }

        var groups = dots.GroupBy(d => ((int)Math.Floor(d.PixelX / cell), (int)Math.Floor(d.PixelY / cell)));
        var best = groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Item2).ThenBy(g => g.Key.Item1).First().ToList();
        return (best.Average(d => d.PixelX), best.Average(d => d.PixelY));
    }

    /// <summary>A tile, loaded the first time it is in view; null (drawn empty) when its file is missing, locked or
    /// broken, whatever the error (WIC throws COMException on some broken files).</summary>
    private BitmapSource? Tile(Aion2WorldMap map, int col, int row)
    {
        if (_tiles.TryGetValue((map.Key, col, row), out BitmapSource? cached))
        {
            return cached;
        }

        BitmapSource? bitmap = null;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, map.TilePath(col, row));
            if (File.Exists(path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                bitmap = image;
            }
        }
        catch (Exception)
        {
            bitmap = null;
        }

        _tiles[(map.Key, col, row)] = bitmap;
        return bitmap;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static string Loc(string key) => LocalizationManager.Instance[key];
}
