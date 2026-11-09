using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>What the "Morts du donjon" window shows: the run's bosses, one row per player (see <see cref="RunDeaths"/>), their names and classes.</summary>
public sealed record RunDeathsData(IReadOnlyList<RunBoss> Bosses, IReadOnlyList<RunDeathRow> Rows, Func<int, string> NameOf, Func<int, string> ClassOf);

/// <summary>
/// The deaths of the dungeon, one line per player, most deaths first: a column per boss (in the order they fell) and the total
/// (Stacy, 2026-11-09, for the first Ludra run). A small, compact recap: it opens by itself at the death of each boss of Ludra
/// (setting RunDeathsRecap) without taking the focus, and from the app menu. A snapshot, like the details window.
/// </summary>
public sealed class RunDeathsWindow : Window
{
    private const double RowHeight = 24;
    private const double BossColumn = 62;

    public RunDeathsWindow(RunDeathsData data)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["RunDeaths.Title"];
        int bosses = Math.Max(1, data.Bosses.Count);
        Width = Math.Min(40 + 128 + BossColumn * bosses + 50 + 28, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(86 + RowHeight * Math.Max(2, data.Rows.Count) + 8, SystemParameters.WorkArea.Height - 40);
        MinWidth = 300;
        MinHeight = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(ForegroundProperty, "Brush.Body");
        SetResourceReference(FontSizeProperty, "FontSize.Base");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);

        var dock = new DockPanel { Margin = new Thickness(14, 10, 14, 8) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(head, Dock.Top);
        TextBlock sub = Text(data.Bosses.Count == 0 ? "" : string.Format(CultureInfo.CurrentCulture, loc["RunDeaths.Sub"], data.Bosses.Count), "Brush.Muted", 11);
        DockPanel.SetDock(sub, Dock.Right);
        sub.VerticalAlignment = VerticalAlignment.Bottom;
        head.Children.Add(sub);
        head.Children.Add(Text(loc["RunDeaths.Title"], "Brush.Heading", 15, bold: true));
        dock.Children.Add(head);

        if (data.Rows.Count == 0)
        {
            TextBlock empty = Text(loc["RunDeaths.Empty"], "Brush.Muted", 12);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(2, 12, 0, 0);
            dock.Children.Add(empty);
        }
        else
        {
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Table(data, loc) });
        }

        var root = new Border { Child = dock };
        Color top = TryFindResource("Color.SurfaceTop") is Color t ? t : Color.FromRgb(0x14, 0x1C, 0x24);
        Color bottom = TryFindResource("Color.SurfaceBottom") is Color b ? b : Color.FromRgb(0x0B, 0x10, 0x16);
        root.Background = new LinearGradientBrush(top, bottom, 90);
        Content = root;
        ThemedChrome.Apply(this);

        // Like the overlay, a little see-through: it fades in, rests slightly transparent over the game and turns solid under the mouse.
        Opacity = 0;
        Loaded += (_, _) => FadeTo(RestOpacity, 350);
        MouseEnter += (_, _) => FadeTo(1, 150);
        MouseLeave += (_, _) => FadeTo(RestOpacity, 300);
    }

    private const double RestOpacity = 0.88;

    private void FadeTo(double opacity, int milliseconds) =>
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(opacity, TimeSpan.FromMilliseconds(milliseconds)));

    /// <summary>"Isolation Specialist Rahu" -> "Rahu": the last word is the boss's own name in all four of Ludra's.</summary>
    private static string ShortName(string boss)
    {
        int space = boss.LastIndexOf(' ');
        return space >= 0 ? boss[(space + 1)..] : boss;
    }

    private static UIElement Table(RunDeathsData data, LocalizationManager loc)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
        foreach (RunBoss _ in data.Bosses)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BossColumn) });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        int totalColumn = grid.ColumnDefinitions.Count - 1;

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (int i = 0; i < data.Bosses.Count; i++)
        {
            TextBlock name = Text(ShortName(data.Bosses[i].Name), "Brush.Dim", 10.5, bold: true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = data.Bosses[i].Name;
            Place(grid, name, 0, 2 + i, HorizontalAlignment.Right);
            name.Margin = new Thickness(0, 0, 8, 0);
        }

        TextBlock totalHead = Text(loc["RunDeaths.Total"], "Brush.Dim", 10.5, bold: true);
        totalHead.Margin = new Thickness(0, 0, 6, 0);
        Place(grid, totalHead, 0, totalColumn, HorizontalAlignment.Right);

        for (int r = 0; r < data.Rows.Count; r++)
        {
            RunDeathRow row = data.Rows[r];
            int line = r + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            var back = new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 1, 0, 1) };
            back.SetResourceReference(Border.BackgroundProperty, r % 2 == 0 ? "Brush.Card" : "Brush.Field");
            Grid.SetRow(back, line);
            Grid.SetColumnSpan(back, grid.ColumnDefinitions.Count);
            grid.Children.Add(back);

            Place(grid, Text((r + 1).ToString(CultureInfo.CurrentCulture), "Brush.Dim", 10.5), line, 0, HorizontalAlignment.Center);
            string className = data.ClassOf(row.PlayerId);
            var who = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(className) && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
            {
                who.Children.Add(new Image { Source = icon, Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0) });
            }

            TextBlock player = Text(data.NameOf(row.PlayerId), "Brush.Heading", 12, bold: true);
            if (!string.IsNullOrEmpty(className))
            {
                player.Foreground = ClassColors.For(className);
            }

            player.TextTrimming = TextTrimming.CharacterEllipsis;
            who.Children.Add(player);
            Place(grid, who, line, 1);

            for (int i = 0; i < row.PerBoss.Count; i++)
            {
                Place(grid, Count(row.PerBoss[i], bold: false, 8), line, 2 + i, HorizontalAlignment.Right);
            }

            Place(grid, Count(row.Total, bold: true, 6), line, totalColumn, HorizontalAlignment.Right);
        }

        return grid;
    }

    /// <summary>A death count: "-" when nothing is known, a muted 0, the number brighter the more there are.</summary>
    private static TextBlock Count(int? value, bool bold, double right)
    {
        string text = value is int n ? n.ToString(CultureInfo.CurrentCulture) : "–";
        string brush = value is null ? "Brush.Faint" : value == 0 ? "Brush.Muted" : "Brush.BrandText";
        TextBlock block = Text(text, brush, bold ? 13 : 12, bold || value > 0);
        block.Margin = new Thickness(0, 0, right, 0);
        return block;
    }

    private static void Place(Grid grid, UIElement element, int row, int column, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        if (element is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            if (fe.HorizontalAlignment == HorizontalAlignment.Stretch || align != HorizontalAlignment.Left)
            {
                fe.HorizontalAlignment = align;
            }
        }

        grid.Children.Add(element);
    }

    private static TextBlock Text(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        block.SetResourceReference(TextBlock.FontSizeProperty, ThemeManager.FontKey(size));
        return block;
    }
}
