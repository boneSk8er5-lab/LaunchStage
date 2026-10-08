using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LaunchStage.Core.Logging;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>
/// The built-in guide: topics on the left (with search), the chosen topic on the right with its steps and pictures.
/// Opened from the ? button (or F1) in the launcher, the tray's Help item, and the small ? links next to options.
/// </summary>
public partial class HelpWindow : Window
{
    private static HelpWindow? _open;
    private bool _syncing;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    private HelpWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        TopicsList.ItemsSource = Guide.Topics;
        AboutText.Text = $"LaunchStage {AppInfo.Version}\n{AppInfo.Copyright}\n{AppInfo.UseNotice}";
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>Opens the guide (or brings it forward) at a topic; the first topic when none is given.</summary>
    public static void ShowTopic(string? topicId = null)
    {
        // Opened before a dialog like Settings, the guide is disabled until that dialog closes: open a fresh one.
        if (_open != null && !IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(_open).Handle))
        {
            var stale = _open;
            _open = null;
            stale.Close();
        }

        if (_open == null)
        {
            var window = new HelpWindow();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_open, window))
                {
                    _open = null;
                }
            };
            _open = window;
            _open.Show();
        }
        else if (_open.WindowState == WindowState.Minimized)
        {
            _open.WindowState = WindowState.Normal;
        }

        _open.Activate();
        _open.Select(Guide.Find(topicId) ?? Guide.Topics.FirstOrDefault());
    }

    private void Select(GuideTopic? topic)
    {
        if (topic == null)
        {
            Reader.Document = new FlowDocument(new Paragraph(new Run("The guide couldn't be loaded.")));
            return;
        }

        if (TopicsList.ItemsSource is IEnumerable<GuideTopic> shown && !shown.Contains(topic))
        {
            SearchBox.Text = ""; // a link to a topic the search hides: show every topic again
        }

        _syncing = true;
        TopicsList.SelectedItem = topic;
        TopicsList.ScrollIntoView(topic);
        _syncing = false;
        Reader.Document = GuideDocument.Build(topic, ShowTopic);
    }

    private void Topics_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && TopicsList.SelectedItem is GuideTopic topic)
        {
            Reader.Document = GuideDocument.Build(topic, ShowTopic);
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        string search = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var matches = search.Length == 0
            ? Guide.Topics.ToList()
            : Guide.Topics.Where(t => search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(word => t.SearchText.Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();

        var current = TopicsList.SelectedItem;
        TopicsList.ItemsSource = matches;
        if (current is GuideTopic topic && matches.Contains(topic))
        {
            _syncing = true;
            TopicsList.SelectedItem = topic;
            _syncing = false;
        }
        else if (matches.Count > 0)
        {
            TopicsList.SelectedIndex = 0;
        }
    }

    private void Tour_Click(object sender, RoutedEventArgs e) => WelcomeWindow.ShowTour(this);

    private void License_Click(object sender, RoutedEventArgs e) => AppInfo.OpenLicense();

    private void WebPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string page = GuideHtml.Write(GuideHtml.TempFolder);

            // Through Explorer, so the browser opens as a normal app even when LaunchStage runs as administrator.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{page}\"") { UseShellExecute = true });
            Log.Info($"Opened the guide as a web page: {page}");
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't make the guide's web page: {ex}");
            Dialogs.Warn(this, $"The guide couldn't be opened as a web page: {ex.Message}");
        }
    }
}

/// <summary>Turns a guide topic into a FlowDocument in the app's colors (it follows dark / light).</summary>
internal static class GuideDocument
{
    public static FlowDocument Build(GuideTopic topic, Action<string?> openTopic)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14.5,
            PagePadding = new Thickness(0, 0, 12, 0),
            TextAlignment = TextAlignment.Left,
            LineHeight = 22
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        document.SetResourceReference(FlowDocument.BackgroundProperty, "PanelBrush");

        document.Blocks.Add(new Paragraph(new Run(topic.Title))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        foreach (var block in topic.Blocks)
        {
            if (Make(block, openTopic) is { } made)
            {
                document.Blocks.Add(made);
            }
        }

        return document;
    }

    private static Block? Make(GuideBlock block, Action<string?> openTopic)
    {
        switch (block.Kind)
        {
            case GuideBlockKind.Heading:
            {
                var heading = Text(block.Text, openTopic);
                heading.FontSize = 18;
                heading.FontWeight = FontWeights.SemiBold;
                heading.Margin = new Thickness(0, 18, 0, 6);
                return heading;
            }

            case GuideBlockKind.Paragraph:
                return Text(block.Text, openTopic);

            case GuideBlockKind.Steps or GuideBlockKind.Bullets:
            {
                var list = new List
                {
                    MarkerStyle = block.Kind == GuideBlockKind.Steps ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 4, 0, 12),
                    Padding = new Thickness(26, 0, 0, 0)
                };
                foreach (string item in block.Items)
                {
                    var paragraph = Text(item, openTopic);
                    paragraph.Margin = new Thickness(0, 0, 0, 6);
                    list.ListItems.Add(new ListItem(paragraph));
                }

                return list;
            }

            case GuideBlockKind.Tip:
            {
                var paragraph = Text(block.Text, openTopic);
                paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, new Bold(new Run("Tip: ")));
                paragraph.Margin = new Thickness(0);
                var tip = new Section(paragraph)
                {
                    BorderThickness = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 8, 0, 14)
                };
                tip.SetResourceReference(Section.BorderBrushProperty, "AccentBrush");
                tip.SetResourceReference(Section.BackgroundProperty, "InputBrush");
                return tip;
            }

            case GuideBlockKind.Table:
                return MakeTable(block, openTopic);

            case GuideBlockKind.Image when block.File != null:
            {
                var source = Guide.Image(block.File);
                if (source == null)
                {
                    return null; // picture not made yet
                }

                var image = new Image
                {
                    Source = source,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    MaxWidth = source.Width,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                var frame = new Border { Child = image, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
                frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
                var caption = new TextBlock { Text = block.Text, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
                caption.SetResourceReference(TextBlock.StyleProperty, "Hint");
                return new BlockUIContainer(new StackPanel { Children = { frame, caption } }) { Margin = new Thickness(0, 6, 0, 16) };
            }

            default:
                return null;
        }
    }

    private static Table MakeTable(GuideBlock block, Action<string?> openTopic)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 14) };
        int columns = block.Rows.Max(r => r.Count);
        for (int c = 0; c < columns; c++)
        {
            table.Columns.Add(new TableColumn { Width = c == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        for (int r = 0; r < block.Rows.Count; r++)
        {
            var row = new TableRow();
            foreach (string text in block.Rows[r])
            {
                var paragraph = Text(text, openTopic);
                paragraph.Margin = new Thickness(0);
                if (r == 0)
                {
                    paragraph.FontWeight = FontWeights.SemiBold;
                }

                var cell = new TableCell(paragraph) { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "PanelBorderBrush");
                row.Cells.Add(cell);
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>A paragraph with bold, italic, `keys` and links to other topics.</summary>
    private static Paragraph Text(string text, Action<string?> openTopic)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var part in Guide.ParseInline(text))
        {
            switch (part.Kind)
            {
                case GuideInlineKind.Bold:
                    paragraph.Inlines.Add(new Bold(new Run(part.Text)));
                    break;
                case GuideInlineKind.Italic:
                    paragraph.Inlines.Add(new Italic(new Run(part.Text)));
                    break;
                case GuideInlineKind.Code:
                {
                    var key = new Run(part.Text) { FontFamily = new FontFamily("Consolas"), FontSize = 13.5 };
                    key.SetResourceReference(TextElement.BackgroundProperty, "ButtonBrush");
                    paragraph.Inlines.Add(key);
                    break;
                }

                case GuideInlineKind.Link:
                {
                    var link = new Hyperlink(new Run(part.Text)) { Cursor = Cursors.Hand };
                    link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                    string? target = part.Target;
                    link.Click += (_, _) => openTopic(target);
                    paragraph.Inlines.Add(link);
                    break;
                }

                default:
                    paragraph.Inlines.Add(new Run(part.Text));
                    break;
            }
        }

        return paragraph;
    }
}
