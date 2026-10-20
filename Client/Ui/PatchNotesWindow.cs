using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// "What's new": the patch notes of the version just installed (assets/patch-notes/&lt;version&gt;.md, written for each release
/// and also put on its GitHub release), shown once at the first start of that version (Stacy, 2026-10-18). The notes are a
/// small Markdown subset: "# " title, "## " section, "**bold**" lines and "- " items, with **bold** and *italic* inside.
/// </summary>
public sealed class PatchNotesWindow : Window
{
    private static readonly Brush Back = Frozen(Color.FromRgb(0x14, 0x1C, 0x24));
    private static readonly Brush Card = Frozen(Color.FromRgb(0x1A, 0x24, 0x2E));
    private static readonly Brush Heading = Frozen(Color.FromRgb(0xF2, 0xF5, 0xF7));
    private static readonly Brush Body = Frozen(Color.FromRgb(0xC8, 0xD2, 0xDC));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0xB9, 0xA6, 0xFF));

    public PatchNotesWindow(string version, string markdown)
    {
        Title = $"Aion DPS {version}";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Back;
        Topmost = true;

        var page = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        foreach (string raw in markdown.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                page.Children.Add(new Border { Height = 6 });
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                page.Children.Add(Text(line[2..], 20, Heading, FontWeights.Bold, new Thickness(0, 0, 0, 10)));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                page.Children.Add(Text(line[3..], 15, Accent, FontWeights.SemiBold, new Thickness(0, 8, 0, 4)));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var item = new DockPanel { Margin = new Thickness(4, 2, 0, 2) };
                var dot = Text("•", 13, Accent, FontWeights.Bold, new Thickness(0, 0, 8, 0));
                DockPanel.SetDock(dot, Dock.Left);
                item.Children.Add(dot);
                item.Children.Add(Text(line[2..], 13, Body, FontWeights.Normal, new Thickness(0)));
                page.Children.Add(item);
            }
            else
            {
                page.Children.Add(Text(line, 13, Heading, FontWeights.Normal, new Thickness(0, 6, 0, 2)));
            }
        }

        var close = new Button { Content = "OK", Width = 110, Height = 32, Margin = new Thickness(0, 10, 22, 16), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new Border
        {
            Background = Card,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(14, 14, 14, 0),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page },
        });
        Content = root;
    }

    /// <summary>The notes of this version, if the release carries some and they were not shown yet; marks them shown.</summary>
    public static (string Version, string Markdown)? Pending(MeterSettings settings)
    {
        Version? v = typeof(PatchNotesWindow).Assembly.GetName().Version;
        if (v is null)
        {
            return null;
        }

        string version = $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "patch-notes", version + ".md");
        if (settings.LastPatchNotes == version || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return (version, File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The notes of this version, else of the newest version before it that has some (Settings → Updates).</summary>
    public static (string Version, string Markdown)? Latest()
    {
        Version? current = typeof(PatchNotesWindow).Assembly.GetName().Version;
        string folder = Path.Combine(AppContext.BaseDirectory, "assets", "patch-notes");
        if (current is null || !Directory.Exists(folder))
        {
            return null;
        }

        var best = Directory.GetFiles(folder, "*.md")
            .Select(f => (Path: f, Ok: System.Version.TryParse(Path.GetFileNameWithoutExtension(f), out Version? v), V: v))
            .Where(f => f.Ok && f.V! <= new Version(current.Major, current.Minor, Math.Max(0, current.Build)))
            .OrderByDescending(f => f.V)
            .FirstOrDefault();
        if (best.Path is null)
        {
            return null;
        }

        try
        {
            return (Path.GetFileNameWithoutExtension(best.Path), File.ReadAllText(best.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A line with **bold** and *italic* parts.</summary>
    private static TextBlock Text(string text, double size, Brush brush, FontWeight weight, Thickness margin)
    {
        var block = new TextBlock { FontSize = size, Foreground = brush, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = margin };
        int i = 0;
        while (i < text.Length)
        {
            if (text.AsSpan(i).StartsWith("**") && text.IndexOf("**", i + 2, StringComparison.Ordinal) is int endBold and > 0)
            {
                block.Inlines.Add(new Run(text[(i + 2)..endBold]) { FontWeight = FontWeights.Bold, Foreground = Heading });
                i = endBold + 2;
            }
            else if (text[i] == '*' && text.IndexOf('*', i + 1) is int endItalic and > 0)
            {
                block.Inlines.Add(new Run(text[(i + 1)..endItalic]) { FontStyle = FontStyles.Italic });
                i = endItalic + 1;
            }
            else
            {
                int next = text.IndexOf('*', i);
                int stop = next < 0 ? text.Length : next == i ? i + 1 : next;
                block.Inlines.Add(new Run(text[i..stop]));
                i = stop;
            }
        }

        return block;
    }

    private static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
