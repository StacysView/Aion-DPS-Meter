namespace AionDPS.Ui;

/// <summary>
/// Hides the meter's overlays while a game menu is open (see <see cref="MenuDetector"/>; SkeeveTV's idea and code, MIT). Only in the control mode that hides
/// the mouse cursor while playing: the meter learns it from the fights, and in the classic mode (cursor always shown) it does nothing. An overlay that is
/// unlocked (being placed) keeps the watch from hiding anything. Runs on the UI thread.
/// </summary>
public sealed class MenuWatcher : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly MenuDetector _detector = new();
    private readonly ControlModeLearner _learner = new();
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _lastOwnHit = DateTime.MinValue;
    private int _look;
    private bool _held;
    private DateTime? _lastClick;

    public MenuWatcher()
    {
        _timer.Tick += (_, _) =>
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine("[menu watch] " + e.GetType().Name + ": " + e.Message);
            }
        };
        _timer.Start();
    }

    /// <summary>True while a menu is open and the overlays should be out of the way.</summary>
    public static bool Hidden { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Hidden"/> changes.</summary>
    public static event Action<bool>? HiddenChanged;

    /// <summary>The control mode seen so far (also kept in the settings).</summary>
    public static ControlMode Mode { get; private set; } = ControlMode.Unknown;

    /// <summary>The local player just dealt damage (the control mode is learnt while fighting).</summary>
    public void NoteOwnHit() => _lastOwnHit = DateTime.UtcNow;

    public void Dispose()
    {
        _timer.Stop();
        Set(false);
    }

    /// <summary>True while <see cref="Hidden"/> is on because the game is not there (closed, or not in front with that option on) and not because of a menu
    /// (Stacy, 2026-11-14). The meter's own main window is then left alone: it must stay reachable without the game.</summary>
    public static bool Away { get; private set; }

    private static void Set(bool hidden, bool away = false)
    {
        if (Hidden != hidden || Away != away)
        {
            Away = away;
            Hidden = hidden;
            HiddenChanged?.Invoke(hidden);
        }
    }

    private bool _running = true;
    private DateTime _runningAt = DateTime.MinValue;
    private DateTime? _notInFrontSince;

    /// <summary>The overlays out of the way because the game is closed, or (the second option) because another program is in front for half a second.
    /// Never while a panel is unlocked (being placed), and the meter's own windows count as "with the game".</summary>
    private bool GameAway(bool inGame)
    {
        if (!AllOverlaysLocked(_settings) || (!_settings.HideWithoutGame && !_settings.HideWhenGameNotInFront))
        {
            _notInFrontSince = null;
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (inGame)
        {
            _running = true;
            _runningAt = now;
        }
        else if ((now - _runningAt).TotalSeconds >= 2)
        {
            _running = GameWindow.IsRunning();
            _runningAt = now;
        }

        if (_settings.HideWithoutGame && !_running)
        {
            return true;
        }

        if (!_settings.HideWhenGameNotInFront || inGame || GameWindow.ForegroundIsOwn())
        {
            _notInFrontSince = null;
            return false;
        }

        _notInFrontSince ??= now;
        return now - _notInFrontSince.Value >= TimeSpan.FromMilliseconds(500);
    }

    /// <summary>True when every overlay is locked (none is being placed).</summary>
    public static bool AllOverlaysLocked(MeterSettings s) => s.PetPanelsLocked && s.BossCastBarLocked && s.TargetPowerLocked;

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            if (Enum.TryParse(_settings.MenuControlMode, true, out ControlMode saved) && _learner.Mode == ControlMode.Unknown && saved != ControlMode.Unknown)
            {
                Mode = saved; // what an earlier session learnt counts until this one has seen enough
            }
        }

        bool inGame = GameWindow.ForegroundIsGame();
        bool cursor = inGame && GameWindow.IsCursorVisible();
        ControlMode learned = _learner.Note(DateTime.UtcNow, inGame, DateTime.UtcNow - _lastOwnHit < TimeSpan.FromSeconds(2), cursor);
        if (learned != ControlMode.Unknown && learned != Mode)
        {
            Mode = learned;
            var onDisk = MeterSettings.Load();
            onDisk.MenuControlMode = learned.ToString();
            onDisk.Save();
        }

        if (GameAway(inGame))
        {
            _detector.Reset();
            Set(true, away: true);
            return;
        }

        if (!_settings.HideDuringMenus || Mode == ControlMode.Classic)
        {
            _detector.Reset();
            Set(false);
            return;
        }

        // The keys are asked only while the cursor shows, and every other look: during play this costs nothing.
        if (!cursor)
        {
            _held = false;
            _look = 0;
        }
        else if (_look++ % 2 == 0)
        {
            _held = GameWindow.AnyKeyDown();
        }

        if (inGame && GameWindow.MouseButtonDown())
        {
            _lastClick = DateTime.UtcNow;
        }

        Set(_detector.Update(DateTime.UtcNow, inGame, cursor, _held, !AllOverlaysLocked(_settings), _lastClick));
    }
}
