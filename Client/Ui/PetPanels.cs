using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// A small panel of the meter over the game (the pet map, the pet list): topmost, no taskbar entry, never takes the
/// keyboard. Locked, it lets every click through to the game (WS_EX_TRANSPARENT on the layered window, like
/// <see cref="TeaseBanner"/>); unlocked, it is dragged by its body and its place is saved when the drag ends. At
/// start it is put back on a visible screen when the saved place is on none (a monitor unplugged). Only local
/// windows: nothing goes to the game.
/// </summary>
public abstract class PetPanelWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    protected static readonly Brush Accent = PetLook.Frozen(Color.FromRgb(0x7B, 0x5C, 0xF0));
    protected static readonly Brush CardFrame = PetLook.Frozen(Color.FromRgb(0x2A, 0x38, 0x46));
    protected static readonly Brush MutedText = PetLook.Frozen(Color.FromRgb(0x8F, 0xA3, 0xB3));

    private readonly Border _frame;
    private readonly Thumb _grip;
    private bool _locked = true;
    private double _opacity = 0.85;

    protected PetPanelWindow(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        _frame = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = CardFrame,
            Padding = new Thickness(8, 6, 8, 8),
        };
        // The corner grip of an unlocked panel, like the overlay's: drag it to resize, the size is saved when it ends.
        _grip = (Thumb)System.Windows.Markup.XamlReader.Parse(
            """
            <Thumb xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="14" Height="14" Cursor="SizeNWSE"
                   HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,2,2" Visibility="Collapsed">
                <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                        <Grid Background="Transparent">
                            <Path Data="M12,2 L2,12 M12,6 L6,12 M12,10 L10,12" Stroke="#B8A6FF" StrokeThickness="1.2"/>
                        </Grid>
                    </ControlTemplate>
                </Thumb.Template>
            </Thumb>
            """);
        _grip.DragDelta += (_, e) => ResizeBy(e.HorizontalChange, e.VerticalChange);
        _grip.DragCompleted += (_, _) => SaveSize();
        var root = new Grid();
        root.Children.Add(_frame);
        root.Children.Add(_grip);
        Content = root;
        ApplyBackground();

        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            ApplyClickThrough();
        };
        Loaded += (_, _) => KeepOnVisibleScreen();
        _frame.MouseLeftButtonDown += (_, e) =>
        {
            if (_locked || e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove(); // returns when the button is released
            }
            catch (InvalidOperationException)
            {
                return; // the button was already up
            }

            SavePlace(Left, Top);
        };
    }

    /// <summary>The panel's content, inside the rounded frame.</summary>
    protected UIElement? Body
    {
        set => _frame.Child = value;
    }

    public bool IsLocked => _locked;

    /// <summary>Whether Windows sends the clicks through this panel (read by the smoke harness).</summary>
    public bool IsClickThrough
    {
        get
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            return handle != IntPtr.Zero && (GetWindowLong(handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0;
        }
    }

    /// <summary>Locked: click-through, the plain frame. Unlocked: takes the mouse, purple frame, move cursor.</summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        _frame.BorderBrush = locked ? CardFrame : Accent;
        _frame.Cursor = locked ? null : Cursors.SizeAll;
        _grip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        ApplyClickThrough();
        OnLockedChanged(locked);
    }

    /// <summary>The opacity of the dark background, 0.2 to 1 (the text and the map stay solid).</summary>
    public void SetBackgroundOpacity(double opacity)
    {
        _opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0.85, 0.2, 1.0);
        ApplyBackground();
    }

    /// <summary>Puts the panel back at the front of the topmost band (a borderless game can take it).</summary>
    public void KeepOnTop() => NativeOverlay.KeepOnTop(new WindowInteropHelper(this).Handle);

    protected virtual void OnLockedChanged(bool locked)
    {
    }

    /// <summary>Saves the place the panel was dragged to.</summary>
    protected abstract void SavePlace(double left, double top);

    /// <summary>The corner grip moved by (dx, dy) DIPs: the panel takes its new size (within its bounds).</summary>
    protected abstract void ResizeBy(double dx, double dy);

    /// <summary>Saves the size the panel was given with its corner grip.</summary>
    protected abstract void SaveSize();

    /// <summary>Drags the corner grip by (dx, dy), as the mouse would (for the smoke harness).</summary>
    public void ResizeForTest(double dx, double dy) => ResizeBy(dx, dy);

    /// <summary>Where the panel goes when it has no saved place, or when the saved one is on no screen.</summary>
    protected abstract Point DefaultPlace(Rect workArea);

    /// <summary>Load-modify-save of the settings file, like the rest of the meter; a locked file loses only this save.</summary>
    protected static void SaveSettings(Action<MeterSettings> change)
    {
        try
        {
            MeterSettings onDisk = MeterSettings.Load();
            change(onDisk);
            onDisk.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    protected static string Loc(string key) => LocalizationManager.Instance[key];

    private void ApplyBackground()
    {
        byte alpha = (byte)Math.Round(_opacity * 255);
        var brush = new LinearGradientBrush(Color.FromArgb(alpha, 0x14, 0x1C, 0x24), Color.FromArgb(alpha, 0x0B, 0x10, 0x16), 90);
        brush.Freeze();
        _frame.Background = brush;
    }

    private void ApplyClickThrough()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return; // SourceInitialized applies it
        }

        // The window stays layered either way (AllowsTransparency needs it); only the transparent bit is switched.
        int style = GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_LAYERED;
        style = _locked ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(handle, GWL_EXSTYLE, style);
    }

    /// <summary>At least a corner of 60 x 30 DIPs of the panel must lie on a screen's work area, else it goes to its
    /// default place on the primary screen.</summary>
    private void KeepOnVisibleScreen()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var panel = new Rect(Left, Top, Math.Max(ActualWidth, 60), Math.Max(ActualHeight, 30));
        bool visible = false;
        foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
        {
            System.Drawing.Rectangle area = screen.WorkingArea;
            var work = new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
            Rect overlap = Rect.Intersect(work, panel);
            if (!overlap.IsEmpty && overlap.Width >= 60 && overlap.Height >= 30)
            {
                visible = true;
                break;
            }
        }

        if (!visible)
        {
            Point place = DefaultPlace(SystemParameters.WorkArea);
            Left = place.X;
            Top = place.Y;
        }
    }
}

/// <summary>
/// The two pet panels over the game, fed by the capture: the pet collection (a soul, a level, the login list), the
/// player's place and the Hidden Cubes around him. Their events come from the capture thread: BeginInvoke, at most one
/// rebuild per 250 ms (the first one at once). A move that keeps the shown map only moves the player's arrow (and the
/// cubes) on the map, up to 10 times a second, without rebuilding anything. A pet gaining souls or a level makes its
/// line flash (<see cref="MeterSettings.PetFlash"/>). Created by the meter when <see cref="MeterSettings.PetPanels"/> is on. Nothing here may end the
/// meter (it has no handler for an exception of the UI thread, and the panels open again at every start): anything
/// unforeseen while applying or refreshing closes the set for the session (<see cref="Failed"/>), the saved setting
/// is left as it is, and the meter runs on.
/// </summary>
public sealed class PetPanelSet
{
    private static readonly TimeSpan RebuildGap = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MoveGap = TimeSpan.FromMilliseconds(100);
    private readonly DispatcherTimer _glide;
    private static readonly TimeSpan CubeGap = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TopGap = TimeSpan.FromSeconds(2);

    private readonly Aion2PetCollection _pets;
    private readonly Aion2PlayerPosition _position;
    private readonly Aion2HiddenCubes? _cubes;
    private readonly Dispatcher _dispatcher;
    private readonly PetMapPanel _map;
    private readonly PetListPanel _list;
    private readonly PetFlashes _flashes = new();
    private readonly Action _later, _moved;
    private readonly Action<Aion2PetState, bool> _onChanged;
    private HashSet<int> _pins = new();
    private string? _farmZone;
    private (string? MapKey, bool ZoneKnown) _shownFor;
    private DateTime _lastBuild = DateTime.MinValue, _lastTop = DateTime.MinValue, _lastMove = DateTime.MinValue;
    private DispatcherTimer? _timer, _moveTimer, _topTimer, _cubeTimer;
    private bool _closed, _mapGone, _listGone, _placed, _hidden, _zoomRestored;

    public PetPanelSet(Aion2PetCollection pets, Aion2PlayerPosition position, Aion2HiddenCubes? cubes = null)
    {
        _pets = pets;
        _position = position;
        _cubes = cubes;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _map = new PetMapPanel();
        _list = new PetListPanel();
        _later = () => _dispatcher.BeginInvoke(DispatcherPriority.Background, ScheduleRefresh);
        _moved = () => _dispatcher.BeginInvoke(DispatcherPriority.Background, OnMoved);
        // A soul or a level (a level-up comes with its own Changed): the pet's line flashes at the next rebuild.
        _onChanged = (state, _) => _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _flashes.Mark(state.PetId);
            ScheduleRefresh();
        });
        _pets.Changed += _onChanged;
        _pets.ListReplaced += _later;
        _position.Changed += _moved;
        // In flight the game sends a place only 2-3 times a second, with the velocity 10 times: the arrow glides
        // between places at 30 frames a second while a recent velocity says the player moves.
        _glide = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        _glide.Tick += (_, _) =>
        {
            if (_position.IsGliding(DateTime.UtcNow))
            {
                MovePlayer();
            }
        };
        _glide.Start();
        // A panel closed from outside (Alt+F4) ends the set: the meter makes a new one at its next settings apply.
        _map.Closed += (_, _) =>
        {
            _mapGone = true;
            Close();
        };
        _list.Closed += (_, _) =>
        {
            _listGone = true;
            Close();
        };
    }

    /// <summary>True once closed; a closed set never shows again.</summary>
    public bool IsClosed => _closed;

    /// <summary>True when the set closed itself on an unforeseen error; the meter then shows no panels until it restarts.</summary>
    public bool Failed { get; private set; }

    /// <summary>The error that closed the set (for the smoke harness).</summary>
    public string? FailedWith { get; private set; }

    /// <summary>The source this set follows (the meter makes a new set for a new source).</summary>
    public Aion2PetCollection Pets => _pets;

    public PetMapPanel MapPanel => _map;

    public PetListPanel ListPanel => _list;

    /// <summary>What the panels show now (read by the smoke harness).</summary>
    public TrackerModel? Model { get; private set; }

    /// <summary>Takes the panels' settings (lock, opacity, size, zoom at first, pins, followed zone) and shows them
    /// (unless hidden with the meter's UI).</summary>
    public void Apply(MeterSettings settings)
    {
        if (_closed)
        {
            return;
        }

        try
        {
            ApplyCore(settings);
        }
        catch (Exception e)
        {
            Fail("apply", e);
        }
    }

    /// <summary>The meter is hidden (Ctrl+Alt+H minimizes it): the panels go with it, and come back with it.</summary>
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (_closed || !_placed)
        {
            return; // the first Apply shows them, or not
        }

        try
        {
            if (hidden)
            {
                _map.Hide();
                _list.Hide();
            }
            else
            {
                _map.Show();
                _list.Show();
                Refresh();
            }
        }
        catch (Exception e)
        {
            Fail("hide", e);
        }
    }

    private void ApplyCore(MeterSettings settings)
    {
        _pins = settings.PetPins.ToHashSet();
        _farmZone = settings.PetFarmZone;
        foreach (PetPanelWindow panel in new PetPanelWindow[] { _map, _list })
        {
            panel.SetBackgroundOpacity(settings.PetPanelsOpacity);
            panel.SetLocked(settings.PetPanelsLocked);
        }

        _flashes.Enabled = settings.PetFlash;
        _map.SetSize(settings.PetMapSize);
        _list.MapSizeForDefault = _map.MapSize;
        _list.SetSize(settings.PetListWidth, settings.PetListHeight);
        if (!_zoomRestored)
        {
            // The saved zoom is taken once; later the wheel changes it (and saves it).
            _zoomRestored = true;
            _map.View.SetZoom(settings.PetMapZoom);
        }

        Refresh();
        if (_closed)
        {
            return; // the refresh failed
        }

        // The saved place is taken when the panels first appear; later they stay where they were dragged.
        if (!_placed && !_hidden)
        {
            _placed = true;
            _map.PlaceAt(settings.PetMapLeft, settings.PetMapTop);
            _map.Show();
            _list.PlaceAt(settings.PetListLeft, settings.PetListTop);
            _list.Show();
        }

        // A borderless game can take the front back at any time: both panels are put on top again every 2 s.
        if (_topTimer is null)
        {
            _topTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TopGap };
            _topTimer.Tick += (_, _) =>
            {
                try
                {
                    if (!_closed && _map.IsVisible && _list.IsVisible)
                    {
                        _map.KeepOnTop();
                        _list.KeepOnTop();
                    }
                }
                catch (Exception e)
                {
                    Fail("keep on top", e);
                }
            };
            _topTimer.Start();
        }

        // The cubes come and go without a move of the player (one picked up, one announced while he stands still).
        if (_cubes is not null && _cubeTimer is null)
        {
            _cubeTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = CubeGap };
            _cubeTimer.Tick += (_, _) =>
            {
                try
                {
                    if (!_closed && !_hidden)
                    {
                        ShowCubes();
                    }
                }
                catch (Exception e)
                {
                    Fail("cubes", e);
                }
            };
            _cubeTimer.Start();
        }
    }

    /// <summary>The pins or the followed zone changed in the Pets window.</summary>
    public void SetChoices(IReadOnlyCollection<int> pins, string? farmZone)
    {
        _pins = pins.ToHashSet();
        _farmZone = farmZone;
        Refresh();
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _timer?.Stop();
        _moveTimer?.Stop();
        _topTimer?.Stop();
        _cubeTimer?.Stop();
        _pets.Changed -= _onChanged;
        _pets.ListReplaced -= _later;
        _position.Changed -= _moved;
        _glide.Stop();
        // A window already closed (or closing: this runs from its Closed event) must not be closed again.
        if (!_mapGone)
        {
            _mapGone = true;
            _map.Close();
        }

        if (!_listGone)
        {
            _listGone = true;
            _list.Close();
        }
    }

    /// <summary>Rebuilds both panels from the collection and the place; an unforeseen error closes the set.</summary>
    public void Refresh()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            RefreshCore();
        }
        catch (Exception e)
        {
            Fail("refresh", e);
        }
    }

    private void RefreshCore()
    {
        _lastBuild = DateTime.UtcNow;
        _lastMove = _lastBuild;
        string language = LocalizationManager.Instance.Language;
        TrackerModel model = PetTrackerView.Build(_pets, _position, language, _pins, _farmZone);
        Model = model;
        _shownFor = (_position.Map?.Key, _position.ZoneId > 0);
        // The zone is known (a zone frame came) but it is not the shown map: a dungeon, a city, another map.
        bool otherMap = model.Map is not null && _position.ZoneId > 0 && _position.Map?.Key != model.Map.Key;
        _map.ShowModel(model, otherMap);
        _list.ShowModel(model, _flashes, _flashes.StartPending());
        ShowCubes();
        if (!_hidden && DateTime.UtcNow - _lastTop >= TopGap)
        {
            _lastTop = DateTime.UtcNow;
            _map.KeepOnTop();
            _list.KeepOnTop();
        }
    }

    /// <summary>Closes the set for this session and writes one line for a debugger (the meter keeps no log file). The
    /// setting stays on: the panels come back at the next start.</summary>
    private void Fail(string during, Exception e)
    {
        Failed = true;
        FailedWith = $"{during}: {e.GetType().Name}: {e.Message}";
        System.Diagnostics.Trace.WriteLine("[pet panels] closed after an error in " + FailedWith);
        try
        {
            Close();
        }
        catch (Exception)
        {
            _closed = true; // a window that cannot even close: the set stays dead
        }
    }

    /// <summary>The player moved (UI thread). On the same map and zone state as the last rebuild only the arrow and the
    /// cubes move (at most every <see cref="MoveGap"/>); anything else (a teleport to another map, the first zone
    /// frame) rebuilds.</summary>
    private void OnMoved()
    {
        if (_closed)
        {
            return;
        }

        if (Model?.Map is null || _shownFor != (_position.Map?.Key, _position.ZoneId > 0))
        {
            ScheduleRefresh();
            return;
        }

        TimeSpan since = DateTime.UtcNow - _lastMove;
        if (since >= MoveGap)
        {
            MovePlayer();
            return;
        }

        if (_moveTimer is { IsEnabled: true })
        {
            return;
        }

        _moveTimer ??= new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
        _moveTimer.Interval = MoveGap - since;
        _moveTimer.Tick -= OnMoveTimer;
        _moveTimer.Tick += OnMoveTimer;
        _moveTimer.Start();
    }

    private void OnMoveTimer(object? sender, EventArgs e)
    {
        _moveTimer?.Stop();
        MovePlayer();
    }

    /// <summary>Puts the player's new place into the shown model and the map (no rebuild of the dots or the list).</summary>
    private void MovePlayer()
    {
        if (_closed || Model?.Map is not { } map)
        {
            return;
        }

        try
        {
            _lastMove = DateTime.UtcNow;
            (double X, double Y)? player = _position.Map?.Key == map.Key && _position.Predicted(DateTime.UtcNow) is { } here ? map.ToPixel(here.X, here.Y) : null;
            if (player is null && Model.Player is null)
            {
                return;
            }

            Model = Model with { Player = player };
            Moves++;
            _map.View.SetPlayer(player);
            ShowCubes();
        }
        catch (Exception e)
        {
            Fail("move", e);
        }
    }

    /// <summary>How many moves only moved the arrow (read by the smoke harness).</summary>
    public int Moves { get; private set; }

    /// <summary>The live Hidden Cubes on the shown map, while the player stands on it.</summary>
    private void ShowCubes()
    {
        if (_cubes is null)
        {
            return;
        }

        var shown = new List<MapCube>();
        if (Model?.Map is { } map && _position.Map?.Key == map.Key && _position.Last is { } me)
        {
            foreach ((float x, float y) in _cubes.Live(me.At))
            {
                (double px, double py) = map.ToPixel(x, y);
                double metres = Math.Sqrt((x - me.X) * (double)(x - me.X) + (y - me.Y) * (double)(y - me.Y)) / 100.0;
                shown.Add(new MapCube(px, py, metres));
            }
        }

        _map.View.SetCubes(shown);
    }

    /// <summary>One rebuild per <see cref="RebuildGap"/> at most; a call inside the gap is merged into one later.</summary>
    private void ScheduleRefresh()
    {
        if (_closed)
        {
            return;
        }

        TimeSpan since = DateTime.UtcNow - _lastBuild;
        if (since >= RebuildGap)
        {
            Refresh();
            return;
        }

        if (_timer is { IsEnabled: true })
        {
            return;
        }

        _timer ??= new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
        _timer.Interval = RebuildGap - since;
        _timer.Tick -= OnTimer;
        _timer.Tick += OnTimer;
        _timer.Start();
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Refresh();
    }
}
