using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>What the target panel shows: the marked player's name, class and combat power, and how that power stands against the
/// local player's own (<see cref="Ratio"/> = target / own; null while the own power is unknown).</summary>
public sealed record TargetPowerData(string Name, string? ClassName, int Power, double? Ratio, bool Sample = false, DuelRecord? Duel = null);

/// <summary>Builds the panel's line; no WPF, so it can be tested.</summary>
public static class TargetPowerView
{
    /// <summary>The fixed number the 10 against 10 arena gives everybody: not a real power (SkeeveTV).</summary>
    public const int ArenaPower = 397_228;

    /// <summary>Within 5 % either way the two powers count as equal.</summary>
    public const double EqualBand = 0.05;

    /// <summary>The marked entity when it is a player whose combat power the game told; null for a monster, an unknown player or the arena's fixed number.</summary>
    public static TargetPowerData? Build(Aion2EntityDirectory entities, int ownPower, Aion2Duels? duels = null)
    {
        int target = entities.LocalTarget;
        if (target <= 0 || !entities.LocalPlayerAlive)
        {
            return null;
        }

        if (entities.SeenProfileOf(target) is not { CombatPower: int power } seen)
        {
            // A player the meter knows by name but whose power the game has not told yet: shown at once, the power follows ("..." until then).
            return entities.IsKnownPlayer(target) && entities.NameFor(target) is { } known && !known.StartsWith("Player #", StringComparison.Ordinal)
                ? new TargetPowerData(known, null, 0, null, Duel: duels?.Of(known, DateTime.UtcNow))
                : null;
        }

        if (power == ArenaPower)
        {
            return null;
        }

        string? className = seen.ClassId is int id ? Aion2SkillNames.ClassFromCode(id * 4 + 1) : null;
        string name = entities.NameFor(target) ?? "?";
        return new TargetPowerData(name, className, power, ownPower > 0 ? power / (double)ownPower : null, Duel: duels?.Of(name, DateTime.UtcNow));
    }

    /// <summary>-1 = the target is weaker (green), 1 = stronger (red), 0 = about the same or unknown.</summary>
    public static int Side(double? ratio) => ratio is not double r ? 0 : r > 1 + EqualBand ? 1 : r < 1 - EqualBand ? -1 : 0;
}

/// <summary>
/// The combat power of the player the local player marked (tab or click), over the game (Stacy, 2026-11-13): "Jeane · Spiritmaster · 195 426",
/// green when the target is weaker than the local player, red when stronger. The game itself tells both the mark and the power (the "player
/// appeared" frame; layout by SkeeveTV, MIT): nothing is looked up anywhere. Topmost, never takes the keyboard, click-through when locked,
/// dragged when unlocked (a sample line stands in). Like the other panels nothing here may end the meter. Only a local window.
/// </summary>
public sealed class TargetPowerPanel : PetPanelWindow
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan TopGap = TimeSpan.FromSeconds(2);
    private static readonly Brush Weaker = PetLook.Frozen(Color.FromRgb(0x5F, 0xD3, 0x8A));
    private static readonly Brush Stronger = PetLook.Frozen(Color.FromRgb(0xF0, 0x6A, 0x6A));
    private static readonly Brush Even = PetLook.Frozen(Color.FromRgb(0xF2, 0xD0, 0x6B));

    private readonly Aion2EntityDirectory _entities;
    private readonly Aion2Duels? _duels;
    private readonly TextBlock _duel = new() { FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private static readonly Brush DuelLost = PetLook.Frozen(Color.FromRgb(0xFF, 0x2E, 0x2E));
    private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _class = new() { FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _power = new() { FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _gap = new() { FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer;
    private DateTime _lastTop = DateTime.MinValue;
    private int _ownPower;
    private bool _placed, _hidden, _closed;

    public TargetPowerPanel(Aion2EntityDirectory entities, Aion2Duels? duels = null) : base("Aion DPS - target power")
    {
        _entities = entities;
        _duels = duels;
        _class.Foreground = MutedText;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(_name);
        line.Children.Add(_class);
        line.Children.Add(_power);
        line.Children.Add(_gap);
        line.Children.Add(_duel);
        FramePadding = new Thickness(10, 5, 10, 6);
        Body = line;
        SizeToContent = SizeToContent.WidthAndHeight;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tick };
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
        };
    }

    public Aion2EntityDirectory Entities => _entities;

    public bool IsClosed => _closed;

    /// <summary>True when the panel closed itself on an unforeseen error; the meter then shows none until it restarts.</summary>
    public bool Failed { get; private set; }

    /// <summary>The line shown last (read by the preview); null while hidden.</summary>
    public TargetPowerData? Shown { get; private set; }

    public void Apply(MeterSettings settings)
    {
        if (_closed)
        {
            return;
        }

        try
        {
            _ownPower = Math.Max(0, settings.OwnCombatPower);
            SetLocked(settings.TargetPowerLocked);
            SetBackgroundOpacity(0.85);
            if (!_placed)
            {
                _placed = true;
                Point place = settings.TargetPowerLeft is double l && settings.TargetPowerTop is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
                Left = place.X;
                Top = place.Y;
            }

            _timer.Start();
            Refresh();
        }
        catch (Exception e)
        {
            Fail("apply", e);
        }
    }

    public void ClosePanel()
    {
        if (_closed)
        {
            return;
        }

        _timer.Stop();
        Close();
    }

    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (_placed && !_closed)
        {
            Refresh();
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
            // The party roster tells the local player's own power: kept in the settings so it is known outside a party too.
            if (_entities.OwnCombatPower is int own && own != _ownPower)
            {
                _ownPower = own;
                SaveSettings(s => s.OwnCombatPower = own);
            }

            TargetPowerData? data = _hidden ? null : TargetPowerView.Build(_entities, _ownPower, _duels);
            if (data is null && !_hidden && !IsLocked)
            {
                data = new TargetPowerData(Loc("TargetPower.SampleName"), Aion2SkillNames.ClassFromCode(21), 195_426, _ownPower > 0 ? 195_426 / (double)_ownPower : null, Sample: true);
            }

            Shown = data;
            if (data is null)
            {
                if (IsVisible)
                {
                    Hide();
                }

                return;
            }

            _name.Text = data.Name;
            _class.Text = data.ClassName is null ? "" : LocalizationManager.Instance["Class." + data.ClassName];
            _class.Visibility = data.ClassName is null ? Visibility.Collapsed : Visibility.Visible;
            _power.Text = data.Power > 0 ? data.Power.ToString("N0", CultureInfo.CurrentCulture) : "…";
            int side = TargetPowerView.Side(data.Ratio);
            _power.Foreground = data.Ratio is null ? Brushes.White : side > 0 ? Stronger : side < 0 ? Weaker : Even;
            _gap.Text = data.Ratio is double r ? (r >= 1 ? "+" : "−") + Math.Abs((r - 1) * 100).ToString("0", CultureInfo.CurrentCulture) + " %" : "";
            _gap.Foreground = _power.Foreground;
            _gap.Visibility = data.Ratio is null ? Visibility.Collapsed : Visibility.Visible;
            // The score of the deaths between the two: "5 - 2" = he killed me 5 times, I killed him twice; vivid red when he leads, green when I do.
            if (data.Duel is { } duel)
            {
                _duel.Text = $"\u2620 {duel.KilledMe} \u2013 {duel.IKilled}";
                _duel.Foreground = duel.Side < 0 ? DuelLost : duel.Side > 0 ? Weaker : Even;
                _duel.Visibility = Visibility.Visible;
            }
            else
            {
                _duel.Visibility = Visibility.Collapsed;
            }

            if (!IsVisible)
            {
                Show();
            }

            if (DateTime.UtcNow - _lastTop >= TopGap)
            {
                _lastTop = DateTime.UtcNow;
                KeepOnTop();
            }
        }
        catch (Exception e)
        {
            Fail("refresh", e);
        }
    }

    private void Fail(string during, Exception e)
    {
        Failed = true;
        System.Diagnostics.Trace.WriteLine($"[target power] closed after an error in {during}: {e.GetType().Name}: {e.Message}");
        try
        {
            ClosePanel();
        }
        catch (Exception)
        {
            _closed = true;
            _timer.Stop();
        }
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.TargetPowerLeft = left;
        s.TargetPowerTop = top;
    });

    protected override void ResizeBy(double dx, double dy)
    {
    }

    protected override void SaveSize()
    {
    }

    /// <summary>Under the game's target bar: top middle of the screen.</summary>
    protected override Point DefaultPlace(Rect workArea) => new(workArea.Left + workArea.Width / 2 - 120, workArea.Top + workArea.Height * 0.10);
}
