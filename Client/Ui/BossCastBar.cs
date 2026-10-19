using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One skill a boss is winding up, as the bar shows it: the skill's name (its client text in the game language,
/// or what it paints on the floor when it has none), who casts it, whom it is aimed at, and how far the cast has come.
/// <see cref="Generic"/>: the skill has no name of its own ("Attack"), so the caster leads and the skill sits below.</summary>
public sealed record CastRowData(string Skill, string Caster, string Target, bool AtYou, double ElapsedMs, int CastMs, bool Generic, bool Sample = false);

/// <summary>
/// The boss cast bar's rows from what the windup frames said (<see cref="Aion2BossCasts"/>): no window, so the self-test
/// runs it. SkeeveTV's cast bar (Aion-DPS-Meter, MIT): only bosses and elite monsters (a boss of the catalog, or a monster
/// of 500,000 hit points or more), a player's windup never; the skill in the game language (0.45.2), the target behind it
/// (0.46.1), a plain "Attack" named by its floor shape (1.1.0). Upstream's learning of areas, landing times and lethal hits
/// is not ported.
/// </summary>
public static class BossCastBarData
{
    /// <summary>A cast stays on the bar this long after it ended (its damage is about to land).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>A monster this tough counts as a boss even when the catalog does not list it (SkeeveTV's threshold).</summary>
    public const long EliteHitPoints = 500_000;

    public const int MaxRows = 3;

    public static IReadOnlyList<CastRowData> Build(Aion2EntityDirectory dir, DateTime now, string language)
    {
        var loc = LocalizationManager.Instance;
        int local = dir.LocalPlayerId >= 0 ? dir.LocalPlayerId : dir.InferLocalPlayer() ?? -1;
        var scored = new List<(int Score, DateTime End, CastRowData Row)>();
        foreach (CastNotice cast in dir.Casts.Running(now, Grace))
        {
            if (cast.Actor == local || dir.IsKnownPlayer(cast.Actor) || !IsBoss(dir, cast.Actor))
            {
                continue; // a player's windup is no boss attack, an ordinary monster's is not shown
            }

            bool atYou = local >= 0 && cast.Target == local;
            string target = atYou ? loc["Combat.You"]
                : cast.Target <= 0 || cast.Target == cast.Actor ? loc["Combat.AllAround"]
                : NameOf(dir, cast.Target, language) ?? LocalizationManager.Instance["Combat.SomePlayer"];
            var row = new CastRowData(SkillText(cast.SkillId, out bool generic), NameOf(dir, cast.Actor, language) ?? LocalizationManager.Instance["Combat.Sample.Boss"], target, atYou,
                (now - cast.Start).TotalMilliseconds, cast.DurationMs, generic);
            scored.Add(((atYou ? 200 : 0) + (dir.BossNpcIdOf(cast.Actor) is not null ? 40 : 0), cast.End, row));
        }

        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.End).Take(MaxRows).OrderBy(s => s.End).Select(s => s.Row).ToList();
    }

    /// <summary>A boss of the catalog (under the NPC id the entity has now: ids are handed out again), or a monster of
    /// <see cref="EliteHitPoints"/> or more.</summary>
    public static bool IsBoss(Aion2EntityDirectory dir, int entity) =>
        (dir.BossNpcIdOf(entity) is int boss && (dir.NpcIdOf(entity) ?? boss) == boss) || (dir.HitPoints.HighestSeen(entity) ?? 0) >= EliteHitPoints;

    /// <summary>The skill's client text in the UI language; a skill without one ("Attack") says what it paints on the floor
    /// ("Attack to the front"), else "Attack" - never a bare number.</summary>
    public static string SkillText(int skillId, out bool generic)
    {
        generic = Aion2SkillNames.IsGeneric(skillId);
        if (generic && Aion2SkillShapes.KindOf(skillId) is { } shape)
        {
            return LocalizationManager.Instance["Combat.Shape." + shape];
        }

        string text = Aion2SkillNames.DisplayOf(skillId);
        return text.Length == 0 || text.All(char.IsDigit) ? Aion2SkillNames.Display("Attack") : text;
    }

    /// <summary>A player's name, a monster's in the game language (its NPC id), a boss's from the catalog; null when none is
    /// known yet (the bar then says "Boss" or "un joueur" rather than "#46870" or "Player #15274": a field boss already up when
    /// the meter started, a player not named yet - Stacy, 2026-10-19).</summary>
    public static string? NameOf(Aion2EntityDirectory dir, int id, string language)
    {
        if (!dir.IsKnownPlayer(id) && dir.NpcIdOf(id) is int npc && Aion2Npcs.NameOf(npc, language) is { Length: > 0 } monster)
        {
            return monster;
        }

        string? name = dir.NameFor(id);
        return name is null || name.StartsWith("Player #", StringComparison.Ordinal) ? null : name;
    }
}

/// <summary>The boss cast bar's rows: up to three, each a skill being cast with its name in bold, its target in yellow
/// behind it, the caster below and a bar that fills up to the hit. A cast aimed at the local player has a red frame and
/// a red bar (SkeeveTV's 0.46.1/0.47.0 look, MIT; drawn in the compact overlay's dark style).</summary>
public sealed class BossCastView : StackPanel
{
    internal static readonly Brush Text = PetLook.Frozen(Color.FromRgb(0xEE, 0xF2, 0xF6));
    internal static readonly Brush Muted = PetLook.Frozen(Color.FromRgb(0x8F, 0xA3, 0xB3));
    internal static readonly Brush Yellow = PetLook.Frozen(Color.FromRgb(0xF5, 0xC8, 0x4C));
    internal static readonly Brush Red = PetLook.Frozen(Color.FromRgb(0xFF, 0x4D, 0x5E));
    internal static readonly Brush Track = PetLook.Frozen(Color.FromRgb(0x24, 0x30, 0x3C));
    internal static readonly Brush RowBack = PetLook.Frozen(Color.FromArgb(0x55, 0x0B, 0x10, 0x16));
    internal static readonly Brush RowFrame = PetLook.Frozen(Color.FromRgb(0x2A, 0x38, 0x46));
    internal static readonly Brush RedBack = PetLook.Frozen(Color.FromArgb(0x38, 0xFF, 0x4D, 0x5E));

    private readonly List<CastRow> _views = new();

    /// <summary>The rows shown (read by the self-test and the preview).</summary>
    public IReadOnlyList<CastRowData> Shown { get; private set; } = [];

    public void Show(IReadOnlyList<CastRowData> rows)
    {
        while (_views.Count < rows.Count)
        {
            var view = new CastRow();
            _views.Add(view);
            Children.Add(view);
        }

        for (int i = 0; i < _views.Count; i++)
        {
            if (i < rows.Count)
            {
                _views[i].Update(rows[i], double.IsFinite(Width) ? Math.Max(110, (Width - 70) * 0.62) : 190);
                _views[i].Visibility = Visibility.Visible;
                _views[i].Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 0);
            }
            else
            {
                _views[i].Visibility = Visibility.Collapsed;
            }
        }

        Shown = rows;
    }

    private sealed class CastRow : Border
    {
        private readonly TextBlock _lead = new() { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _target = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Yellow, Margin = new Thickness(7, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _below = new() { FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _time = new() { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Text, MinWidth = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        private readonly Border _fill = new() { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1) };

        private readonly Grid _bar = new() { Height = 5, Margin = new Thickness(0, 5, 0, 0) };

        public CastRow()
        {
            Padding = new Thickness(8, 5, 8, 6);
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);

            // skill (bold) and target (yellow) on one line, the trimmed skill first; the caster below
            var names = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_lead, Dock.Left);
            names.Children.Add(_lead);
            names.Children.Add(_target);
            var text = new StackPanel();
            text.Children.Add(names);
            text.Children.Add(_below);

            var top = new DockPanel();
            DockPanel.SetDock(_time, Dock.Right);
            top.Children.Add(_time);
            top.Children.Add(text);

            _bar.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2) });
            _bar.Children.Add(_fill);

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(_bar);
            Child = stack;
        }

        public void Update(CastRowData row, double leadWidth)
        {
            // a skill with a name of its own leads with the caster below; a plain "Attack" (or its floor shape) gives the lead to the caster
            _lead.Text = row.Generic ? row.Caster : row.Skill;
            _below.Text = row.Generic ? row.Skill : row.Caster;
            _target.Text = "→ " + row.Target;
            _lead.MaxWidth = leadWidth; // the target keeps room behind a long skill name
            BorderBrush = row.AtYou ? Red : RowFrame;
            Background = row.AtYou ? RedBack : RowBack;
            _fill.Background = row.AtYou ? Red : Yellow;

            double cast = Math.Max(1, row.CastMs);
            ((ScaleTransform)_fill.RenderTransform).ScaleX = Math.Clamp(row.ElapsedMs / cast, 0, 1);
            double left = Math.Round(Math.Max(0, cast - row.ElapsedMs) / 1000.0, 1);
            _time.Text = left.ToString("0.0", CultureInfo.CurrentCulture);
            _time.Foreground = left < 0.5 ? Red : Text; // the last moments in red, as the number reads
            // the sample row (unlocked, to place the bar) has no time nor bar: they only mean something on a real cast
            _time.Visibility = row.Sample ? Visibility.Collapsed : Visibility.Visible;
            _bar.Visibility = row.Sample ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}

/// <summary>
/// The boss cast bar over the game (Settings → Overlay, off by default): a small panel like the pet panels - topmost, never
/// takes the keyboard, click-through when locked, dragged and resized (its width) when unlocked, place and width saved.
/// Locked, it is on the screen only while a boss winds a skill up; unlocked, a sample row stands in so it can be placed.
/// Reads the casts every 100 ms on the UI thread. Like the pet panels nothing here may end the meter: an unforeseen error
/// closes the bar for the session (<see cref="Failed"/>). Only a local window: nothing goes to the game.
/// </summary>
public sealed class BossCastBarPanel : PetPanelWindow
{
    public const double MinBarWidth = 220, MaxBarWidth = 600, DefaultBarWidth = 320;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TopGap = TimeSpan.FromSeconds(2);

    private readonly Aion2EntityDirectory _entities;
    private readonly BossCastView _view = new();
    private readonly DispatcherTimer _timer;
    private DateTime _lastTop = DateTime.MinValue;
    private bool _placed, _hidden, _closed;

    public BossCastBarPanel(Aion2EntityDirectory entities) : base("Aion DPS - boss cast bar")
    {
        _entities = entities;
        FramePadding = new Thickness(6);
        Body = _view;
        SetWidth(DefaultBarWidth);
        Aion2Npcs.Warm();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tick };
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
        };
    }

    /// <summary>The directory this bar follows (the meter makes a new bar for a new source).</summary>
    public Aion2EntityDirectory Entities => _entities;

    public bool IsClosed => _closed;

    /// <summary>True when the bar closed itself on an unforeseen error; the meter then shows none until it restarts.</summary>
    public bool Failed { get; private set; }

    public double BarWidth { get; private set; } = DefaultBarWidth;

    /// <summary>The rows shown now (read by the preview).</summary>
    public BossCastView View => _view;

    /// <summary>Takes the settings (lock, opacity, width, the saved place the first time) and starts following the casts.</summary>
    public void Apply(MeterSettings settings)
    {
        if (_closed)
        {
            return;
        }

        try
        {
            SetLocked(settings.BossCastBarLocked);
            SetBackgroundOpacity(settings.BossCastBarOpacity);
            SetWidth(settings.BossCastBarWidth);
            if (!_placed)
            {
                _placed = true;
                Point place = settings.BossCastBarLeft is double l && settings.BossCastBarTop is double t ? new Point(l, t) : DefaultPlace(SystemParameters.WorkArea);
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

    /// <summary>Closes the bar for good (a closed window must not be closed again: Alt+F4 may have done it).</summary>
    public void CloseBar()
    {
        if (_closed)
        {
            return;
        }

        _timer.Stop();
        Close();
    }

    /// <summary>The meter is minimized: the bar goes with it, and comes back with it.</summary>
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (_placed && !_closed)
        {
            Refresh();
        }
    }

    public void SetWidth(double width)
    {
        BarWidth = Math.Clamp(double.IsFinite(width) ? width : DefaultBarWidth, MinBarWidth, MaxBarWidth);
        _view.Width = BarWidth;
    }

    /// <summary>The rows for a moment, a sample one while unlocked and nothing is cast (to place the bar).</summary>
    public IReadOnlyList<CastRowData> RowsAt(DateTime now)
    {
        IReadOnlyList<CastRowData> rows = BossCastBarData.Build(_entities, now, LocalizationManager.Instance.Language);
        if (rows.Count == 0 && !IsLocked)
        {
            var loc = LocalizationManager.Instance;
            rows = [new CastRowData(Aion2SkillNames.Display("Attack"), loc["Combat.Sample.Boss"], loc["Combat.You"], true, 900, 1500, Generic: false, Sample: true)];
        }

        return rows;
    }

    private void Refresh()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            IReadOnlyList<CastRowData> rows = _hidden ? [] : RowsAt(DateTime.Now);
            if (rows.Count == 0)
            {
                if (IsVisible)
                {
                    Hide();
                }

                return;
            }

            _view.Show(rows);
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
            Fail("refresh", e);
        }
    }

    private void Fail(string during, Exception e)
    {
        Failed = true;
        System.Diagnostics.Trace.WriteLine($"[boss cast bar] closed after an error in {during}: {e.GetType().Name}: {e.Message}");
        try
        {
            CloseBar();
        }
        catch (Exception)
        {
            _closed = true;
            _timer.Stop();
        }
    }

    protected override void SavePlace(double left, double top) => SaveSettings(s =>
    {
        s.BossCastBarLeft = left;
        s.BossCastBarTop = top;
    });

    protected override void ResizeBy(double dx, double dy) => SetWidth(BarWidth + dx);

    protected override void SaveSize() => SaveSettings(s => s.BossCastBarWidth = BarWidth);

    /// <summary>Above the middle of the screen, where the eye is during a fight.</summary>
    protected override Point DefaultPlace(Rect workArea) => new(workArea.Left + (workArea.Width - BarWidth) / 2, workArea.Top + workArea.Height * 0.22);
}
