using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The dummy test's clock over the game: the state (waiting for the first hit, running, finished), a big countdown, the DPS and hits so far and a
/// progress bar. Like the boss cast bar it is a <see cref="PetPanelWindow"/>: topmost, never takes the keyboard, click-through while locked, dragged
/// (and its width changed with the corner grip) in move mode, its place and width saved in the settings. It shows itself while a test is armed, running or
/// finished, and in move mode (with the time of the next test) so it can be placed; otherwise it is hidden. It reads the controller every 100 ms and
/// whenever the controller says something changed (that event is raised on the thread that polls the capture, so it is marshalled to the dispatcher).
/// The idea of a countdown overlay for a timed dummy test is SkeeveTV's (Ui/DummyTestOverlay.cs, SkeeveAN/Aion-DPS-Meter, MIT, (c) 2026 SkeeveTV);
/// this one is drawn in the meter's own panel style and reads our controller. Only a local window: nothing goes to the game.
/// </summary>
public sealed class DummyTestOverlay : PetPanelWindow
{
    public const double MinOverlayWidth = 200, MaxOverlayWidth = 480, DefaultOverlayWidth = 250;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TopGap = TimeSpan.FromSeconds(2);
    private static readonly Brush Green = PetLook.Frozen(Color.FromRgb(0x5B, 0xD7, 0x5B));

    private readonly DummyTestController _controller;
    private readonly StackPanel _view = new();
    private readonly TextBlock _state = new() { FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _side = new() { FontSize = 11, Foreground = BossCastView.Muted, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _clock = new() { FontSize = 36, FontWeight = FontWeights.Bold, Foreground = BossCastView.Text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _stats = new() { FontSize = 13, Foreground = BossCastView.Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1) };
    private readonly DispatcherTimer _timer;
    private double _width = DefaultOverlayWidth;
    private bool _placed, _closed, _shown = true;

    public DummyTestOverlay(DummyTestController controller) : base("Aion DPS - dummy test")
    {
        _controller = controller;
        FramePadding = new Thickness(10, 7, 10, 8);

        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_state, Dock.Left);
        _state.Margin = new Thickness(0, 0, 8, 0);
        top.Children.Add(_state);
        top.Children.Add(_side);

        var bar = new Grid { Height = 5, Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(new Border { Background = BossCastView.Track, CornerRadius = new CornerRadius(2) });
        bar.Children.Add(_fill);

        _view.Children.Add(top);
        _view.Children.Add(_clock);
        _view.Children.Add(_stats);
        _view.Children.Add(bar);
        Body = _view;
        SetWidth(DefaultOverlayWidth);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tick };
        _timer.Tick += (_, _) => Refresh();
        _controller.Changed += OnControllerChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _controller.Changed -= OnControllerChanged;
        };
    }

    public bool IsClosed => _closed;

    /// <summary>The overlay is switched on (the window's check box): it then shows itself with a test, or in move mode. Off, it stays hidden.</summary>
    public bool ShownByUser => _shown;

    public double OverlayWidth => _width;

    /// <summary>Takes the saved place, width and on/off, and starts following the controller. Locked: the overlay always starts locked.</summary>
    public void Apply(MeterSettings settings)
    {
        if (_closed)
        {
            return;
        }

        _shown = settings.DummyTestOverlay;
        SetWidth(settings.DummyTestOverlayWidth);
        if (!_placed)
        {
            _placed = true;
            Point place = settings.DummyTestOverlayLeft is double l && settings.DummyTestOverlayTop is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
            Left = place.X;
            Top = place.Y;
        }

        _timer.Start();
        Refresh();
    }

    /// <summary>The window's "show the overlay" check box; saved with the settings.</summary>
    public void SetShown(bool shown)
    {
        _shown = shown;
        SaveSettings(s => s.DummyTestOverlay = shown);
        Refresh();
    }

    /// <summary>Move mode: unlocked, the overlay takes the mouse so it can be dragged and resized (and shows with the next test's time even when idle).</summary>
    public void SetMoveMode(bool move)
    {
        SetLocked(!move);
        Refresh();
    }

    public void SetWidth(double width)
    {
        _width = Math.Clamp(double.IsFinite(width) ? width : DefaultOverlayWidth, MinOverlayWidth, MaxOverlayWidth);
        _view.Width = _width;
    }

    /// <summary>Closes the overlay for good (the meter is closing).</summary>
    public void CloseOverlay()
    {
        if (_closed)
        {
            return;
        }

        _timer.Stop();
        Close();
    }

    // The controller raises Changed on the thread that polls the capture.
    private void OnControllerChanged()
    {
        if (!_closed)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Refresh));
        }
    }

    private void Refresh()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            _controller.Tick();
            DummyState state = _controller.State;
            if (!_shown || (state == DummyState.Idle && IsLocked))
            {
                if (IsVisible)
                {
                    Hide();
                }

                return;
            }

            Update(state);
            if (!IsVisible)
            {
                Show();
            }

            if (DateTime.UtcNow - _lastTop >= TopGap)
            {
                _lastTop = DateTime.UtcNow;
                KeepOnTop(); // a borderless game can take the front back
            }
        }
        catch (Exception e)
        {
            // A clock over the game must never end the meter: it closes for the session.
            System.Diagnostics.Trace.WriteLine($"[dummy test overlay] closed after an error: {e.GetType().Name}: {e.Message}");
            CloseOverlay();
        }
    }

    private DateTime _lastTop = DateTime.MinValue;

    private void Update(DummyState state)
    {
        DummyTextEnv env = DummyTextEnv.Current;
        DummyRun? run = state == DummyState.Running || state == DummyState.Finished ? _controller.Current : null;
        TimeSpan planned = _controller.Planned;
        TimeSpan remaining = state == DummyState.Idle ? planned : _controller.Remaining();

        _state.Text = DummyTestText.StateText(state, run?.Complete ?? true, env);
        _state.Foreground = state == DummyState.Running || (state == DummyState.Finished && run?.Complete != false) ? Green : BossCastView.Yellow;
        _side.Text = run is null ? DummyTestText.Duration(planned) : "#" + run.Number + (run.Label.Length > 0 ? "  " + run.Label : "");

        _clock.Text = DummyTestText.Clock(remaining, env.Culture);
        _clock.Foreground = state == DummyState.Running && remaining < TimeSpan.FromSeconds(5) ? BossCastView.Red : BossCastView.Text;

        DummyLive? live = run is null ? null : _controller.Live();
        _stats.Text = env.Format("DummyTest.Overlay.Stats", DummyTestText.Number(live?.Dps ?? 0, env.Culture), live?.Hits ?? 0);

        double progress = state switch
        {
            DummyState.Running when planned > TimeSpan.Zero => 1 - remaining.TotalSeconds / planned.TotalSeconds,
            DummyState.Finished => 1,
            _ => 0,
        };
        ((ScaleTransform)_fill.RenderTransform).ScaleX = Math.Clamp(progress, 0, 1);
        _fill.Background = state == DummyState.Finished && run?.Complete == false ? BossCastView.Yellow : Green;
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.DummyTestOverlayLeft = left;
        s.DummyTestOverlayTop = top;
    });

    protected override void ResizeBy(double dx, double dy) => SetWidth(_width + dx);

    protected override void SaveSize() => SaveSettings(s => s.DummyTestOverlayWidth = _width);

    /// <summary>Right of the middle of the screen, high up, out of the way of the character.</summary>
    protected override Point DefaultPlace(Rect workArea) => new(workArea.Right - _width - 60, workArea.Top + workArea.Height * 0.18);
}
