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
/// monsters' spawn points, and a gold diamond on each live Hidden Cube (a gold arrow and its distance on the edge for one
/// out of view), nothing above or below it. Unlocked, the wheel zooms (50 to 200 %), a hovered dot names its pet and the
/// corner grip sets the map's side (kept square, saved as <see cref="MeterSettings.PetMapSize"/>). While the place is
/// unknown the map is centred on the zone's spawn points; where there is no map it says so. Map pictures: NCSoft
/// (Aion 2), exported by Stacy; spawn points: SkeeveTV (MIT), whose PetMapWindow gave the idea.
/// </summary>
public sealed class PetMapPanel : PetPanelWindow
{
    private const double DefaultSize = 260;

    private readonly PetMapView _view = new();

    /// <summary>The map alone in its rounded frame (Stacy, 2026-10-17: no zone name, zoom or hint above and below it).</summary>
    public PetMapPanel() : base("Aion DPS - pet map")
    {
        FramePadding = new Thickness(4);
        Body = _view;
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
    }

    public void PlaceAt(double? left, double? top)
    {
        Point place = left is double l && top is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
        Left = place.X;
        Top = place.Y;
    }

    /// <summary>Shows a model. It may throw on something unforeseen: <see cref="PetPanelSet.Refresh"/> catches it.</summary>
    public void ShowModel(TrackerModel model, bool otherMap) => _view.SetModel(model, otherMap);

    protected override void OnLockedChanged(bool locked)
    {
        _view.Interactive = !locked;
        if (locked)
        {
            _view.ClearHover(); // a click-through panel gets no mouse-leave: the hovered dot would stay lit
        }
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.PetMapLeft = left;
        s.PetMapTop = top;
    });

    protected override Point DefaultPlace(Rect workArea) => new(workArea.Right - MapSize - 40, workArea.Top + 120);

    /// <summary>The map stays square: the grip's larger move gives the new side.</summary>
    protected override void ResizeBy(double dx, double dy) => SetSize(MapSize + (Math.Abs(dx) >= Math.Abs(dy) ? dx : dy));

    protected override void SaveSize() => SaveSettings(s => s.PetMapSize = MapSize);
}

/// <summary>A live Hidden Cube on the shown map: its pixel on the whole map and its distance to the player.</summary>
public sealed record MapCube(double PixelX, double PixelY, double Metres);

/// <summary>The drawing of the pet map: tiles, dots, the player's arrow and the states without a place or a map. The
/// panel over the game zooms from 50 to 200 %; the big map of the settings page (<see cref="Pannable"/>) from 25 to
/// 400 % and is dragged around (a double click puts it back on the player).</summary>
public sealed class PetMapView : FrameworkElement
{
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
    // The Hidden Cubes: gold, the meter's cube colour (Stacy, 2026-10-17), on a dark halo like the arrow's.
    private static readonly Brush CubeFill = PetLook.Frozen(Color.FromRgb(0xF2, 0xC1, 0x4E));
    private static readonly Brush CubeHalo = PetLook.Frozen(Color.FromArgb(0x80, 0x0B, 0x10, 0x16));
    private static readonly Pen CubePen = FrozenPen(PetLook.Frozen(Color.FromArgb(0xE0, 0x05, 0x08, 0x0C)), 1.4);
    // The enemy guards, meant to be seen at a glance (Stacy, 2026-10-17): a see-through red danger zone, a big red badge
    // with a white skull and a white edge, and out of view a red arrow on the edge with the distance.
    private static readonly Brush GuardFill = PetLook.Frozen(Color.FromRgb(0xE5, 0x1F, 0x1F));
    private static readonly Brush GuardZone = PetLook.Frozen(Color.FromArgb(0x48, 0xFF, 0x26, 0x26));
    private static readonly Pen GuardZonePen = FrozenPen(PetLook.Frozen(Color.FromArgb(0xC0, 0xFF, 0x3B, 0x3B)), 1.2);
    private static readonly Pen GuardPen = FrozenPen(Brushes.White, 1.8);
    private static readonly Typeface SkullFace = new(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>The danger zone drawn around a guard, in metres.</summary>
    private const double GuardZoneMetres = 20;
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly Dictionary<(string Map, int Col, int Row), BitmapSource?> _tiles = new();
    private readonly Dictionary<int, Brush> _dotBrushes = new();
    private TrackerModel? _model;
    private bool _otherMap;
    private (double X, double Y)? _lastPlayer;
    private double _heading;
    private TrackerDot? _hovered;
    private IReadOnlyList<MapCube> _cubes = [];
    private IReadOnlyList<MapCube> _guards = [];

    private (double X, double Y) _pan;
    private Point? _dragFrom;

    public PetMapView()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>The zoom's bounds (the wheel and <see cref="SetZoom"/>): 0.5 to 2 over the game.</summary>
    public double MinZoom { get; init; } = 0.5;

    public double MaxZoom { get; init; } = 2.0;

    /// <summary>The map is dragged with the mouse (the settings page): the view leaves the player by
    /// <see cref="Pan"/>; a double click or another map brings it back.</summary>
    public bool Pannable { get; init; }

    /// <summary>How far the view was dragged from its centre, in map pixels.</summary>
    public (double X, double Y) Pan => _pan;

    /// <summary>Drags the view by (dx, dy) screen pixels, as the mouse would (for the smoke harness).</summary>
    public void PanBy(double dx, double dy)
    {
        if (_model?.Map is not { } map)
        {
            return;
        }

        double k = Zoom * map.MetresPerPixel;
        _pan = (_pan.X - dx / k, _pan.Y - dy / k);
        InvalidateVisual();
    }

    /// <summary>Back on the player (or the zone's spawn points).</summary>
    public void ResetPan()
    {
        _pan = (0, 0);
        InvalidateVisual();
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

    /// <summary>The last drawing said "no checked pet on this map" (read by the smoke harness).</summary>
    public bool NoCheckedPetShown { get; private set; }

    /// <summary>Pets are checked but none of them lives on the shown map: an empty map would look broken, a line says
    /// why (Stacy, 2026-10-17). No WPF, so the self-test covers it.</summary>
    public static bool NoCheckedPetHere(TrackerModel? model) =>
        model is { Filtered: true, Map: not null } && model.Dots.Count == 0;

    public void SetModel(TrackerModel model, bool otherMap)
    {
        StopGlide();
        NoteHeading(model.Player, model.Map?.Key == _model?.Map?.Key);
        if (model.Map?.Key != _model?.Map?.Key)
        {
            _pan = (0, 0); // another map: a drag on the old one means nothing there
        }

        _model = model;
        _dotBrushes.Clear(); // the hues go by rank in this model's pets
        _otherMap = otherMap;
        _hovered = null;
        InvalidateVisual();
    }

    /// <summary>Only the player moved on the same map: the arrow (and the view around it) is drawn again, nothing is
    /// rebuilt. The hovered dot stays.</summary>
    public void SetPlayer((double X, double Y)? player)
    {
        if (_model is null)
        {
            return;
        }

        NoteHeading(player, true);
        // The game tells the player's place only now and then (his casts, a teleport): a short step glides there in
        // 0.35 s instead of jumping (Stacy, 2026-10-17); a long one (a teleport, a first place) jumps.
        if (player is { } to && _model.Player is { } from && _model.Map is { } map
            && Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y)) * map.MetresPerPixel < GlideMetres)
        {
            _glideFrom = from;
            _glideTo = to;
            _glideStart = DateTime.UtcNow;
            if (!_gliding)
            {
                _gliding = true;
                CompositionTarget.Rendering += OnGlideFrame;
            }

            return;
        }

        StopGlide();
        _model = _model with { Player = player };
        InvalidateVisual();
    }

    private const double GlideMetres = 120;
    private static readonly TimeSpan GlideTime = TimeSpan.FromMilliseconds(350);
    private bool _gliding;
    private (double X, double Y) _glideFrom, _glideTo;
    private DateTime _glideStart;

    /// <summary>The arrow on its way: eased out, one drawing per screen frame, then the place it was going to.</summary>
    private void OnGlideFrame(object? sender, EventArgs e)
    {
        if (_model is null)
        {
            StopGlide();
            return;
        }

        double t = Math.Clamp((DateTime.UtcNow - _glideStart).TotalMilliseconds / GlideTime.TotalMilliseconds, 0, 1);
        double eased = 1 - (1 - t) * (1 - t);
        _model = _model with { Player = (_glideFrom.X + (_glideTo.X - _glideFrom.X) * eased, _glideFrom.Y + (_glideTo.Y - _glideFrom.Y) * eased) };
        InvalidateVisual();
        if (t >= 1)
        {
            StopGlide();
        }
    }

    private void StopGlide()
    {
        if (_gliding)
        {
            _gliding = false;
            CompositionTarget.Rendering -= OnGlideFrame;
        }
    }

    /// <summary>The enemy guards around the player (red marks with a white "!"; none drawn out of view).</summary>
    public void SetGuards(IReadOnlyList<MapCube> guards)
    {
        if (guards.Count == 0 && _guards.Count == 0)
        {
            return;
        }

        _guards = guards;
        InvalidateVisual();
    }

    /// <summary>How many guards the last drawing showed (read by the selftest harness).</summary>
    public int GuardsDrawn { get; private set; }

    /// <summary>The live Hidden Cubes to draw (gold diamonds; an arrow on the edge for one out of view).</summary>
    public void SetCubes(IReadOnlyList<MapCube> cubes)
    {
        if (cubes.Count == 0 && _cubes.Count == 0)
        {
            return;
        }

        _cubes = cubes;
        InvalidateVisual();
    }

    /// <summary>How many cubes the last drawing put in view, and how many it pointed to from the edge (for the smoke harness).</summary>
    public int CubesDrawn { get; private set; }

    public int CubesPointed { get; private set; }

    private void NoteHeading((double X, double Y)? player, bool sameMap)
    {
        if (player is not { } p)
        {
            return;
        }

        if (_lastPlayer is { } before && sameMap)
        {
            double dx = p.X - before.X, dy = p.Y - before.Y;
            if (dx * dx + dy * dy <= 9)
            {
                return; // too small a step for a direction: the arrow keeps its heading, the step adds up with the next
            }

            _heading = Math.Atan2(dx, -dy) * 180 / Math.PI; // the way he moved, clockwise from north
        }

        _lastPlayer = p;
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

    /// <summary>Sets the zoom (within <see cref="MinZoom"/> and <see cref="MaxZoom"/>), as the wheel does.</summary>
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Pannable || !Interactive)
        {
            return; // over the game a drag on the map moves the panel
        }

        if (e.ClickCount >= 2)
        {
            ResetPan();
        }
        else if (CaptureMouse())
        {
            _dragFrom = e.GetPosition(this);
            Cursor = Cursors.SizeAll;
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragFrom = null;
        Cursor = null;
    }

    private void EndDrag()
    {
        if (_dragFrom is not null)
        {
            _dragFrom = null;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is { } from)
        {
            Point now = e.GetPosition(this);
            PanBy(now.X - from.X, now.Y - from.Y);
            _dragFrom = now;
            return;
        }

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
        CubesDrawn = 0;
        CubesPointed = 0;
        NoCheckedPetShown = false;

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
        (double X, double Y) middle = _model.Player ?? DensestDots(_model.Dots, w / k) ?? (map.MapPixels / 2.0, map.MapPixels / 2.0);
        (double X, double Y) centre = (Math.Clamp(middle.X + _pan.X, 0, map.MapPixels), Math.Clamp(middle.Y + _pan.Y, 0, map.MapPixels));
        _pan = (centre.X - middle.X, centre.Y - middle.Y); // a drag past the map's edge stops at it
        Centre = centre;
        DrawTiles(dc, map, centre, k, w, h);
        DrawDots(dc, centre, k, w, h);
        DrawGuards(dc, centre, k, w, h, zones: true);
        DrawCubes(dc, centre, k, w, h);

        if (_model.Player is { } me)
        {
            DrawArrow(dc, w / 2 + (me.X - centre.X) * k, h / 2 + (me.Y - centre.Y) * k);
            DrawGuards(dc, centre, k, w, h, zones: false); // over the arrow: a guard a few metres away must not hide under it
        }
        else
        {
            DrawPill(dc, Loc(_otherMap ? "PetPanels.ElsewhereMap" : "PetPanels.UnknownPlace"), w, h - 26);
        }

        if (NoCheckedPetHere(_model))
        {
            NoCheckedPetShown = true;
            DrawPill(dc, Loc("PetPanels.NoCheckedHere"), w, 8); // at the top: the bottom one may say where the player is
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

    /// <summary>Each live Hidden Cube: a gold diamond at its place, or, out of view, a gold arrow on the edge pointing to
    /// it with its distance ("23 m").</summary>
    private void DrawCubes(DrawingContext dc, (double X, double Y) centre, double k, double w, double h)
    {
        if (_model?.Player is null)
        {
            return; // the cubes are only known around the player
        }

        foreach (MapCube cube in _cubes)
        {
            double dx = (cube.PixelX - centre.X) * k, dy = (cube.PixelY - centre.Y) * k;
            double x = w / 2 + dx, y = h / 2 + dy;
            if (x >= 6 && y >= 6 && x <= w - 6 && y <= h - 6)
            {
                var diamond = new StreamGeometry();
                using (StreamGeometryContext g = diamond.Open())
                {
                    g.BeginFigure(new Point(x, y - 7), true, true);
                    g.LineTo(new Point(x + 6, y), true, true);
                    g.LineTo(new Point(x, y + 7), true, true);
                    g.LineTo(new Point(x - 6, y), true, true);
                }

                diamond.Freeze();
                dc.DrawEllipse(CubeHalo, null, new Point(x, y), 10, 10);
                dc.DrawGeometry(CubeFill, CubePen, diamond);
                CubesDrawn++;
                continue;
            }

            // Out of view: a gold arrow on the edge (inside the frame), the distance beside it, towards the middle.
            double scale = Math.Min((w / 2 - 14) / Math.Max(Math.Abs(dx), 1e-6), (h / 2 - 14) / Math.Max(Math.Abs(dy), 1e-6));
            var tip = new Point(w / 2 + dx * scale, h / 2 + dy * scale);
            double angle = Math.Atan2(dy, dx);
            var arrow = new StreamGeometry();
            using (StreamGeometryContext g = arrow.Open())
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(new Point(tip.X - 14 * Math.Cos(angle - 0.45), tip.Y - 14 * Math.Sin(angle - 0.45)), true, true);
                g.LineTo(new Point(tip.X - 9 * Math.Cos(angle), tip.Y - 9 * Math.Sin(angle)), true, true);
                g.LineTo(new Point(tip.X - 14 * Math.Cos(angle + 0.45), tip.Y - 14 * Math.Sin(angle + 0.45)), true, true);
            }

            arrow.Freeze();
            dc.DrawGeometry(CubeFill, CubePen, arrow);
            FormattedText label = Text(string.Format(CultureInfo.CurrentCulture, "{0:0} m", cube.Metres), 11, CubeFill);
            double lx = tip.X - 24 * Math.Cos(angle) - label.Width / 2, ly = tip.Y - 24 * Math.Sin(angle) - label.Height / 2;
            lx = Math.Clamp(lx, 3, Math.Max(3, w - label.Width - 3));
            ly = Math.Clamp(ly, 3, Math.Max(3, h - label.Height - 3));
            dc.DrawRoundedRectangle(PillFill, null, new Rect(lx - 4, ly - 1, label.Width + 8, label.Height + 2), 4, 4);
            dc.DrawText(label, new Point(lx, ly));
            CubesPointed++;
        }
    }

    /// <summary>The guards in two passes: <paramref name="zones"/> the danger zones under everything, else the badges and the
    /// out-of-view arrows over the player's arrow.</summary>
    private void DrawGuards(DrawingContext dc, (double X, double Y) centre, double k, double w, double h, bool zones)
    {
        if (!zones)
        {
            GuardsDrawn = 0;
        }
        if (_model?.Player is null)
        {
            return; // the guards are only known around the player
        }

        double zone = Math.Max(14, GuardZoneMetres / (_model.Map?.MetresPerPixel ?? 1) * k);
        foreach (MapCube guard in _guards)
        {
            double dx = (guard.PixelX - centre.X) * k, dy = (guard.PixelY - centre.Y) * k;
            double x = w / 2 + dx, y = h / 2 + dy;
            if (x >= 8 && y >= 8 && x <= w - 8 && y <= h - 8)
            {
                if (zones)
                {
                    dc.DrawEllipse(GuardZone, GuardZonePen, new Point(x, y), zone, zone);
                    continue;
                }

                dc.DrawEllipse(CubeHalo, null, new Point(x, y), 13, 13);
                dc.DrawEllipse(GuardFill, GuardPen, new Point(x, y), 9.5, 9.5);
                var skull = new FormattedText("\u2620", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, SkullFace, 12, Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(skull, new Point(x - skull.Width / 2, y - skull.Height / 2));
                GuardsDrawn++;
                continue;
            }

            if (zones)
            {
                continue;
            }

            // Out of view: a red arrow on the edge pointing to the guard, its distance beside it.
            double scale = Math.Min((w / 2 - 14) / Math.Max(Math.Abs(dx), 1e-6), (h / 2 - 14) / Math.Max(Math.Abs(dy), 1e-6));
            var tip = new Point(w / 2 + dx * scale, h / 2 + dy * scale);
            double angle = Math.Atan2(dy, dx);
            var arrow = new StreamGeometry();
            using (StreamGeometryContext g = arrow.Open())
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(new Point(tip.X - 15 * Math.Cos(angle - 0.45), tip.Y - 15 * Math.Sin(angle - 0.45)), true, true);
                g.LineTo(new Point(tip.X - 10 * Math.Cos(angle), tip.Y - 10 * Math.Sin(angle)), true, true);
                g.LineTo(new Point(tip.X - 15 * Math.Cos(angle + 0.45), tip.Y - 15 * Math.Sin(angle + 0.45)), true, true);
            }

            arrow.Freeze();
            dc.DrawGeometry(GuardFill, GuardPen, arrow);
            FormattedText label = Text(string.Format(CultureInfo.CurrentCulture, "\u2620 {0:0} m", guard.Metres), 11, GuardFill);
            double lx = tip.X - 26 * Math.Cos(angle) - label.Width / 2, ly = tip.Y - 26 * Math.Sin(angle) - label.Height / 2;
            lx = Math.Clamp(lx, 3, Math.Max(3, w - label.Width - 3));
            ly = Math.Clamp(ly, 3, Math.Max(3, h - label.Height - 3));
            dc.DrawRoundedRectangle(PillFill, null, new Rect(lx - 4, ly - 1, label.Width + 8, label.Height + 2), 4, 4);
            dc.DrawText(label, new Point(lx, ly));
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
