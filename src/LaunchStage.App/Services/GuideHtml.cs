using System.IO;
using System.Net;
using System.Text;

namespace LaunchStageApp.Services;

/// <summary>
/// The guide as one web page (guide.html plus an images folder): to read in a browser, print, or save as PDF.
/// Light colors so it prints well; every topic starts on a new page when printed.
/// </summary>
internal static class GuideHtml
{
    /// <summary>Writes guide.html and its pictures into the folder. Returns the page's full path.</summary>
    public static string Write(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "images"));
        var html = new StringBuilder();
        string version = AppInfo.Version;

        html.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>LaunchStage guide</title>
            <style>
              body { font-family: "Segoe UI", Arial, sans-serif; font-size: 15px; line-height: 1.55; color: #1F1F1F;
                     background: #F3F3F3; margin: 0; }
              main { max-width: 820px; margin: 0 auto; padding: 32px 24px 64px; }
              .cover { border-top: 8px solid #00838F; background: #FFFFFF; border-radius: 8px; padding: 28px 32px; margin-bottom: 24px; }
              .cover h1 { margin: 0 0 6px; font-size: 32px; }
              .cover p { margin: 0; color: #5F5F5F; }
              .cover p.rights { margin-top: 10px; font-size: 13px; }
              footer { color: #5F5F5F; font-size: 12.5px; text-align: center; padding: 8px 24px 0; }
              nav { background: #FFFFFF; border-radius: 8px; padding: 18px 32px; margin-bottom: 24px; }
              nav ol { margin: 8px 0 0; padding-left: 22px; }
              nav a { color: #00838F; text-decoration: none; }
              section { background: #FFFFFF; border-radius: 8px; padding: 26px 32px; margin-bottom: 24px; }
              h1 { font-size: 26px; margin: 0 0 14px; }
              h2 { font-size: 19px; margin: 26px 0 8px; }
              a { color: #00838F; }
              code { font-family: Consolas, monospace; background: #EBEBEB; border: 1px solid #DDDDDD; border-radius: 4px;
                     padding: 0 5px; font-size: 13.5px; }
              ol li, ul li { margin-bottom: 6px; }
              figure { margin: 18px 0; }
              figure img { max-width: 100%; border: 1px solid #DDDDDD; border-radius: 6px; }
              figcaption { color: #5F5F5F; font-size: 13px; margin-top: 4px; }
              .tip { border-left: 4px solid #00838F; background: #EEF7F7; border-radius: 4px; padding: 10px 14px; margin: 16px 0; }
              table { border-collapse: collapse; width: 100%; margin: 12px 0; }
              th, td { border: 1px solid #DDDDDD; padding: 6px 10px; text-align: left; vertical-align: top; }
              th { background: #F3F3F3; }
              @media print {
                body { background: #FFFFFF; font-size: 12pt; }
                main { padding: 0; max-width: none; }
                .cover, nav, section { border-radius: 0; padding: 0; margin: 0 0 18px; }
                section { break-before: page; }
                figure, .tip, table { break-inside: avoid; }
                a { color: #1F1F1F; text-decoration: none; }
              }
            </style>
            </head>
            <body>
            <main>
            """);

        html.Append($"""
            <div class="cover"><h1>LaunchStage guide</h1><p>Your whole workspace in one press. Version {Encode(version)}.</p>
            <p class="rights">{Encode(AppInfo.Copyright)} {Encode(AppInfo.UseNotice)}</p></div>
            <nav><strong>In this guide</strong><ol>
            """);
        foreach (var topic in Guide.Topics)
        {
            html.Append($"<li><a href=\"#{topic.Id}\">{Encode(topic.Title)}</a></li>");
        }

        html.Append("</ol></nav>\n");

        foreach (var topic in Guide.Topics)
        {
            html.Append($"<section id=\"{topic.Id}\">\n<h1>{Encode(topic.Title)}</h1>\n");
            foreach (var block in topic.Blocks)
            {
                AppendBlock(html, block, folder);
            }

            html.Append("</section>\n");
        }

        html.Append($"<footer>{Encode(AppInfo.Copyright)} LaunchStage and this guide may not be copied, shared or " +
                    "distributed without the permission and consent of Bones_84. See LICENSE.txt.</footer>\n");
        html.Append("</main>\n</body>\n</html>\n");
        string page = Path.Combine(folder, "guide.html");
        File.WriteAllText(page, html.ToString(), new UTF8Encoding(false));
        return page;
    }

    private static void AppendBlock(StringBuilder html, GuideBlock block, string folder)
    {
        switch (block.Kind)
        {
            case GuideBlockKind.Heading:
                html.Append($"<h2>{Inline(block.Text)}</h2>\n");
                break;
            case GuideBlockKind.Paragraph:
                html.Append($"<p>{Inline(block.Text)}</p>\n");
                break;
            case GuideBlockKind.Tip:
                html.Append($"<div class=\"tip\"><strong>Tip:</strong> {Inline(block.Text)}</div>\n");
                break;
            case GuideBlockKind.Steps or GuideBlockKind.Bullets:
                string tag = block.Kind == GuideBlockKind.Steps ? "ol" : "ul";
                html.Append($"<{tag}>");
                foreach (string item in block.Items)
                {
                    html.Append($"<li>{Inline(item)}</li>");
                }

                html.Append($"</{tag}>\n");
                break;
            case GuideBlockKind.Table:
                html.Append("<table>");
                for (int r = 0; r < block.Rows.Count; r++)
                {
                    string cell = r == 0 ? "th" : "td";
                    html.Append("<tr>");
                    foreach (string text in block.Rows[r])
                    {
                        html.Append($"<{cell}>{Inline(text)}</{cell}>");
                    }

                    html.Append("</tr>");
                }

                html.Append("</table>\n");
                break;
            case GuideBlockKind.Image when block.File != null:
                byte[]? bytes = Guide.ImageBytes(block.File);
                if (bytes == null)
                {
                    break; // picture not made yet
                }

                File.WriteAllBytes(Path.Combine(folder, "images", block.File), bytes);
                html.Append($"<figure><img src=\"images/{Encode(block.File)}\" alt=\"{Encode(block.Text)}\" />" +
                            $"<figcaption>{Encode(block.Text)}</figcaption></figure>\n");
                break;
        }
    }

    private static string Inline(string text)
    {
        var html = new StringBuilder();
        foreach (var part in Guide.ParseInline(text))
        {
            string encoded = Encode(part.Text);
            html.Append(part.Kind switch
            {
                GuideInlineKind.Bold => $"<strong>{encoded}</strong>",
                GuideInlineKind.Italic => $"<em>{encoded}</em>",
                GuideInlineKind.Code => $"<code>{encoded}</code>",
                GuideInlineKind.Link => $"<a href=\"#{part.Target}\">{encoded}</a>",
                _ => encoded
            });
        }

        return html.ToString();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
