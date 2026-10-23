using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The dummy lab: two stored fights of the local player side by side, per skill what it did and the
/// level it had (Combat/DummyLab does the maths). Opened from the fight history with two fights
/// picked; the first is the earlier one (A), the second the later (B).
/// </summary>
public partial class DummyLabWindow : Window
{
    /// <summary>One skill's line, already formatted: "A → B" in each column.</summary>
    private sealed record Row(string Skill, string Level, Brush LevelBrush, string PerCast, string Crit, string Total, string DpsChange);

    public DummyLabWindow(FightDetail first, FightDetail second)
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        ThemedChrome.Apply(this);

        // The earlier fight is A, whatever order they were picked in.
        (FightDetail a, FightDetail b) = first.Summary.StartedAt <= second.Summary.StartedAt ? (first, second) : (second, first);
        Show(a, b, DummyLab.Compare(a, b, id => Aion2SkillNames.DisplayOf(id)));
    }

    private void Show(FightDetail a, FightDetail b, DummyComparison result)
    {
        var loc = LocalizationManager.Instance;
        TitleText.Text = a.Summary.TargetName == b.Summary.TargetName ? a.Summary.TargetName : a.Summary.TargetName + " / " + b.Summary.TargetName;
        LineA.Text = FightLine("A", a, result.DpsA, result.HitsA);
        LineB.Text = FightLine("B", b, result.DpsB, result.HitsB);
        TotalText.Text = string.Format(loc["DummyLab.Total"], result.DpsA.ToString("N0"), result.DpsB.ToString("N0"), Percent(result.DpsChange));

        DummyVerdict v = result.Verdict;
        VerdictText.Text = v.Kind switch
        {
            DummyVerdictKind.NoLevelData => loc["DummyLab.Verdict.NoLevelData"],
            DummyVerdictKind.NoLevelChange => loc["DummyLab.Verdict.NoLevelChange"],
            DummyVerdictKind.SingleChange => string.Format(loc["DummyLab.Verdict.Single"], v.Skill, v.LevelA, v.LevelB,
                v.PerCastChange is double pc ? Percent(pc) : "-", Signed(v.DpsDelta) + " DPS"),
            DummyVerdictKind.SingleChangeUnused => string.Format(loc["DummyLab.Verdict.SingleUnused"], v.Skill, v.LevelA, v.LevelB),
            _ => string.Format(loc["DummyLab.Verdict.Mixed"], v.ChangedCount),
        };

        WarningsList.ItemsSource = result.Warnings.Select(w => loc[w switch
        {
            DummyWarning.NotDummyFight => "DummyLab.Warn.NotDummy",
            DummyWarning.DurationsDiffer => "DummyLab.Warn.Duration",
            DummyWarning.FewHits => "DummyLab.Warn.FewHits",
            _ => "DummyLab.Warn.Noise",
        }]).ToList();

        Brush changed = TryFindResource("Brush.Warning") as Brush ?? Brushes.Gold;
        Brush plain = TryFindResource("Brush.Label") as Brush ?? Brushes.Gray;
        RowsList.ItemsSource = result.Rows.Select(r => new Row(
            r.Skill,
            r.LevelA is null || r.LevelB is null ? "-" : r.LevelA == r.LevelB ? r.LevelA.ToString()! : $"{r.LevelA} → {r.LevelB}",
            r.LevelChanged ? changed : plain,
            r.HitsA == 0 && r.HitsB == 0 ? "-" : $"{r.PerCastA:N0} → {r.PerCastB:N0}",
            $"{r.CritRateA * 100:0} → {r.CritRateB * 100:0} %",
            $"{r.TotalA:N0} → {r.TotalB:N0}",
            Signed(r.DpsDelta))).ToList();

        RankingList.ItemsSource = result.LevelChanges.Count == 0
            ? new[] { loc["DummyLab.Ranking.None"] }
            : result.LevelChanges.Select((r, i) =>
                $"{i + 1}. {r.Skill}  {r.LevelA} → {r.LevelB}  ·  {Signed(r.DpsDelta)} DPS" + (r.PerCastChange is double c ? $"  ·  {Percent(c)} / {loc["DummyLab.ColPerCast"]}" : "")
                + (r.LowSample ? $"  ({r.HitsA} → {r.HitsB})" : "")).ToList();
    }

    private static string FightLine(string label, FightDetail fight, double dps, int hits) =>
        string.Format(LocalizationManager.Instance["DummyLab.FightLine"], label, fight.Summary.StartedAt.ToString("d MMM") + " " + fight.Summary.StartedAt.ToString("t"),
            fight.Summary.DurationDisplay, dps.ToString("N0"), hits);

    private static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("N0");

    private static string Percent(double fraction) => (fraction >= 0 ? "+" : "") + (fraction * 100).ToString("0.0") + " %";
}
