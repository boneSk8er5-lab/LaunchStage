using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LaunchStage.Core.Logging;

namespace LaunchStageApp.Services;

internal enum GuideBlockKind
{
    Heading,
    Paragraph,
    Steps,
    Bullets,
    Image,
    Tip,
    Table
}

/// <summary>One piece of a guide topic: a heading, paragraph, list, picture, tip or table.</summary>
internal sealed class GuideBlock
{
    public GuideBlockKind Kind { get; init; }

    /// <summary>Heading, paragraph or tip text; the caption of a picture.</summary>
    public string Text { get; init; } = "";

    /// <summary>List items.</summary>
    public List<string> Items { get; init; } = new();

    /// <summary>Table rows (the first row is the header).</summary>
    public List<List<string>> Rows { get; init; } = new();

    /// <summary>A picture's file name in the guide's images folder.</summary>
    public string? File { get; init; }
}

internal sealed class GuideTopic
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public List<GuideBlock> Blocks { get; init; } = new();

    /// <summary>All the topic's words, for search.</summary>
    public string SearchText { get; init; } = "";
}

internal enum GuideInlineKind
{
    Text,
    Bold,
    Italic,
    Code,
    Link
}

internal readonly record struct GuideInline(GuideInlineKind Kind, string Text, string? Target = null);

/// <summary>
/// The LaunchStage guide: simple text files in the repo's guide folder, built into the app (Guide/ resources).
/// See guide/topics.txt for the format. Used by the Help window, the first-time walkthrough and the web page / PDF.
/// </summary>
internal static class Guide
{
    private static List<GuideTopic>? _topics;

    public static IReadOnlyList<GuideTopic> Topics => _topics ??= Load();

    public static GuideTopic? Find(string? id) =>
        Topics.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A guide picture, or null when it's missing (the guide still reads fine without it).</summary>
    public static ImageSource? Image(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Guide/images/{file}");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Debug($"Guide picture {file} is missing: {ex.Message}");
            return null;
        }
    }

    /// <summary>A guide picture's bytes (for the web page), or null when it's missing.</summary>
    public static byte[]? ImageBytes(string file)
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Guide/images/{file}"))?.Stream;
            if (stream == null)
            {
                return null;
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadResource(string path)
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Guide/{path}"))?.Stream;
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Log.Warn($"Guide file {path} couldn't be read: {ex.Message}");
            return null;
        }
    }

    private static List<GuideTopic> Load()
    {
        var topics = new List<GuideTopic>();
        string? index = ReadResource("topics.txt");
        if (index == null)
        {
            return topics;
        }

        foreach (string line in index.Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string? text = ReadResource(line + ".md");
            if (text != null)
            {
                topics.Add(Parse(line, text));
            }
        }

        return topics;
    }

    private static readonly Regex ImageLine = new(@"^!\[(?<caption>[^\]]*)\]\((?<file>[^)]+)\)$");
    private static readonly Regex StepLine = new(@"^\d+\.\s+(?<text>.+)$");

    /// <summary>Reads one topic file (see guide/topics.txt for the format).</summary>
    public static GuideTopic Parse(string id, string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        string title = id;
        var blocks = new List<GuideBlock>();
        var paragraph = new List<string>();
        GuideBlockKind? listKind = null;
        var listItems = new List<string>();
        var tableRows = new List<List<string>>();

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new GuideBlock { Kind = GuideBlockKind.Paragraph, Text = string.Join(" ", paragraph) });
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (listKind != null && listItems.Count > 0)
            {
                blocks.Add(new GuideBlock { Kind = listKind.Value, Items = listItems.ToList() });
            }

            listKind = null;
            listItems.Clear();
        }

        void FlushTable()
        {
            if (tableRows.Count > 0)
            {
                blocks.Add(new GuideBlock { Kind = GuideBlockKind.Table, Rows = tableRows.ToList() });
                tableRows.Clear();
            }
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushTable();
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                FlushAll();
                continue;
            }

            if (line.StartsWith("# "))
            {
                FlushAll();
                title = line[2..].Trim();
            }
            else if (line.StartsWith("## "))
            {
                FlushAll();
                blocks.Add(new GuideBlock { Kind = GuideBlockKind.Heading, Text = line[3..].Trim() });
            }
            else if (ImageLine.Match(line) is { Success: true } image)
            {
                FlushAll();
                blocks.Add(new GuideBlock { Kind = GuideBlockKind.Image, Text = image.Groups["caption"].Value, File = image.Groups["file"].Value });
            }
            else if (line.StartsWith("> "))
            {
                FlushAll();
                blocks.Add(new GuideBlock { Kind = GuideBlockKind.Tip, Text = line[2..].Trim() });
            }
            else if (line.StartsWith('|'))
            {
                FlushParagraph();
                FlushList();
                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToList();
                if (!cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
                {
                    tableRows.Add(cells); // skip the |---|---| line
                }
            }
            else if (StepLine.Match(line) is { Success: true } step)
            {
                FlushParagraph();
                FlushTable();
                if (listKind != GuideBlockKind.Steps)
                {
                    FlushList();
                    listKind = GuideBlockKind.Steps;
                }

                listItems.Add(step.Groups["text"].Value);
            }
            else if (line.StartsWith("- "))
            {
                FlushParagraph();
                FlushTable();
                if (listKind != GuideBlockKind.Bullets)
                {
                    FlushList();
                    listKind = GuideBlockKind.Bullets;
                }

                listItems.Add(line[2..].Trim());
            }
            else if (listKind != null && listItems.Count > 0)
            {
                listItems[^1] += " " + line; // a list item that continues on the next line
            }
            else
            {
                paragraph.Add(line);
            }
        }

        FlushAll();

        var words = new List<string> { title };
        foreach (var block in blocks)
        {
            words.Add(block.Text);
            words.AddRange(block.Items);
            words.AddRange(block.Rows.SelectMany(r => r));
        }

        return new GuideTopic
        {
            Id = id,
            Title = title,
            Blocks = blocks,
            SearchText = string.Join(" ", words.Select(w => string.Concat(ParseInline(w).Select(i => i.Text))))
        };
    }

    private static readonly Regex InlinePattern = new(
        @"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<link>[^\]]+)\]\(topic:(?<target>[\w-]+)\)|\*(?<italic>[^*\s][^*]*?)\*");

    /// <summary>Splits text into plain, bold, italic, `key` and [link](topic:id) pieces.</summary>
    public static List<GuideInline> ParseInline(string text)
    {
        var parts = new List<GuideInline>();
        int at = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > at)
            {
                parts.Add(new GuideInline(GuideInlineKind.Text, text[at..match.Index]));
            }

            if (match.Groups["bold"].Success)
            {
                parts.Add(new GuideInline(GuideInlineKind.Bold, match.Groups["bold"].Value));
            }
            else if (match.Groups["code"].Success)
            {
                parts.Add(new GuideInline(GuideInlineKind.Code, match.Groups["code"].Value));
            }
            else if (match.Groups["link"].Success)
            {
                parts.Add(new GuideInline(GuideInlineKind.Link, match.Groups["link"].Value, match.Groups["target"].Value));
            }
            else
            {
                parts.Add(new GuideInline(GuideInlineKind.Italic, match.Groups["italic"].Value));
            }

            at = match.Index + match.Length;
        }

        if (at < text.Length)
        {
            parts.Add(new GuideInline(GuideInlineKind.Text, text[at..]));
        }

        return parts;
    }
}
