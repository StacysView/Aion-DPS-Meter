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

    private static void Set(bool hidden)
    {
        if (Hidden != hidden)
        {
            Hidden = hidden;
            HiddenChanged?.Invoke(hidden);
        }
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
