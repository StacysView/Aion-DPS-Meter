using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
        Content = _frame;
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
/// The two pet panels over the game, fed by the capture: the pet collection (a soul, a level, the login list) and
/// the player's place. Their events come from the capture thread: BeginInvoke, at most one rebuild per 250 ms (the
/// first one at once). Created by the meter when <see cref="MeterSettings.PetPanels"/> is on.
/// </summary>
public sealed class PetPanelSet
{
    private static readonly TimeSpan RebuildGap = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan TopGap = TimeSpan.FromSeconds(2);

    private readonly Aion2PetCollection _pets;
    private readonly Aion2PlayerPosition _position;
    private readonly Dispatcher _dispatcher;
    private readonly PetMapPanel _map;
    private readonly PetListPanel _list;
    private readonly Action _later;
    private readonly Action<Aion2PetState, bool> _onChanged;
    private readonly Action<Aion2PetState, int> _onLevel;
    private HashSet<int> _pins = new();
    private string? _farmZone;
    private DateTime _lastBuild = DateTime.MinValue, _lastTop = DateTime.MinValue;
    private DispatcherTimer? _timer;
    private bool _closed, _mapGone, _listGone;

    public PetPanelSet(Aion2PetCollection pets, Aion2PlayerPosition position)
    {
        _pets = pets;
        _position = position;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _map = new PetMapPanel();
        _list = new PetListPanel();
        _later = () => _dispatcher.BeginInvoke(DispatcherPriority.Background, ScheduleRefresh);
        _onChanged = (_, _) => _later();
        _onLevel = (_, _) => _later();
        _pets.Changed += _onChanged;
        _pets.LevelRaised += _onLevel;
        _pets.ListReplaced += _later;
        _position.Changed += _later;
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

    /// <summary>The source this set follows (the meter makes a new set for a new source).</summary>
    public Aion2PetCollection Pets => _pets;

    public PetMapPanel MapPanel => _map;

    public PetListPanel ListPanel => _list;

    /// <summary>What the panels show now (read by the smoke harness).</summary>
    public TrackerModel? Model { get; private set; }

    /// <summary>Takes the panels' settings (lock, opacity, size, pins, followed zone) and shows them.</summary>
    public void Apply(MeterSettings settings)
    {
        if (_closed)
        {
            return;
        }

        _pins = settings.PetPins.ToHashSet();
        _farmZone = settings.PetFarmZone;
        foreach (PetPanelWindow panel in new PetPanelWindow[] { _map, _list })
        {
            panel.SetBackgroundOpacity(settings.PetPanelsOpacity);
            panel.SetLocked(settings.PetPanelsLocked);
        }

        _map.SetSize(settings.PetMapSize);
        _list.MapSizeForDefault = _map.MapSize;
        Refresh();
        // The saved place is taken when the panels first appear; later they stay where they were dragged.
        if (!_map.IsVisible)
        {
            _map.PlaceAt(settings.PetMapLeft, settings.PetMapTop);
            _map.Show();
        }

        if (!_list.IsVisible)
        {
            _list.PlaceAt(settings.PetListLeft, settings.PetListTop);
            _list.Show();
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
        _pets.Changed -= _onChanged;
        _pets.LevelRaised -= _onLevel;
        _pets.ListReplaced -= _later;
        _position.Changed -= _later;
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

    /// <summary>Rebuilds both panels from the collection and the place.</summary>
    public void Refresh()
    {
        if (_closed)
        {
            return;
        }

        _lastBuild = DateTime.UtcNow;
        string language = LocalizationManager.Instance.Language;
        TrackerModel model = PetTrackerView.Build(_pets, _position, language, _pins, _farmZone);
        Model = model;
        bool otherMap = model.Map is not null && _position.Map is { } here && here.Key != model.Map.Key;
        _map.ShowModel(model, otherMap);
        _list.ShowModel(model);
        if (DateTime.UtcNow - _lastTop >= TopGap)
        {
            _lastTop = DateTime.UtcNow;
            _map.KeepOnTop();
            _list.KeepOnTop();
        }
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
