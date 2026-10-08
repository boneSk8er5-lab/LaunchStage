using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Native;

namespace LaunchStage.Core.Engine;

/// <summary>
/// Private (incognito) browser windows. A profile entry for a browser can be marked private: it's opened with the
/// browser's private flag, and only private windows count as that entry (normal entries skip them).
///
/// Telling a private window apart: Edge and Firefox put it in the window title. Chrome and Brave usually don't,
/// so LaunchStage also remembers the private windows it opened itself while it keeps running.
/// </summary>
public static class BrowserPrivacy
{
    private sealed record Browser(string Process, string Flag, Func<string, bool> TitleLooksPrivate, bool IsFirefox = false);

    private static readonly Browser[] Browsers =
    {
        new("chrome", "--incognito", t => Has(t, "(Incognito)") || Has(t, "New Incognito tab")),
        new("brave", "--incognito", t => Has(t, "(Private)") || Has(t, "New Private Tab") || Has(t, "Private Window")),
        new("msedge", "--inprivate", t => Has(t, "InPrivate")),
        new("firefox", "-private-window", t => Has(t, "Private Browsing"), IsFirefox: true),
        new("vivaldi", "--incognito", t => Has(t, "Private Window") || Has(t, "(Private)")),
        new("opera", "--private", t => Has(t, "Private browsing") || Has(t, "(Private)")),
        new("chromium", "--incognito", t => Has(t, "(Incognito)") || Has(t, "New Incognito tab")),

        // Built on Chrome's engine: same switches as Chrome.
        new("arc", "--incognito", t => Has(t, "(Incognito)") || Has(t, "Private")),
        new("thorium", "--incognito", t => Has(t, "(Incognito)") || Has(t, "New Incognito tab")),

        // Built on Firefox's engine: same switches as Firefox.
        new("zen", "-private-window", t => Has(t, "Private Browsing"), IsFirefox: true),
        new("librewolf", "-private-window", t => Has(t, "Private Browsing"), IsFirefox: true),
        new("waterfox", "-private-window", t => Has(t, "Private Browsing"), IsFirefox: true),
        new("floorp", "-private-window", t => Has(t, "Private Browsing"), IsFirefox: true),
        new("mullvadbrowser", "-private-window", t => Has(t, "Private Browsing"), IsFirefox: true)
    };

    // Private windows LaunchStage opened (or saw by title) while running.
    private static readonly HashSet<IntPtr> KnownPrivate = new();
    private static readonly object Gate = new();

    /// <summary>True for a browser LaunchStage knows how to open privately.</summary>
    public static bool IsBrowser(string? processName) => Find(processName) != null;

    public static bool IsBrowser(AppEntry app) => IsBrowser(WindowMatcher.GetProcessName(app));

    /// <summary>The flag that opens this app's browser in a private window, or null if it isn't a known browser.</summary>
    public static string? PrivateFlag(AppEntry app) => Find(WindowMatcher.GetProcessName(app))?.Flag;

    /// <summary>True when this browser window is private, as far as LaunchStage can tell.</summary>
    public static bool IsPrivate(WindowInfo window)
    {
        var browser = Find(window.ProcessName);
        if (browser == null)
        {
            return false;
        }

        lock (Gate)
        {
            if (KnownPrivate.Contains(window.Handle))
            {
                return true;
            }
        }

        if (browser.TitleLooksPrivate(window.Title))
        {
            Remember(window.Handle);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The arguments to launch with: the private flag (for private browser entries) or the new-window flag (for
    /// browser entries with websites), then the entry's own arguments, then its websites (one tab each).
    /// </summary>
    public static string? ArgumentsFor(AppEntry app)
    {
        var browser = Find(WindowMatcher.GetProcessName(app));
        var sites = browser == null ? new List<string>() : WebsitesFor(app);
        var parts = new List<string>();

        if (browser != null && app.PrivateWindow)
        {
            parts.Add(browser.Flag);
        }
        else if (browser != null && sites.Count > 0)
        {
            parts.Add(browser.IsFirefox ? "-new-window" : "--new-window");
        }

        if (!string.IsNullOrWhiteSpace(app.Arguments))
        {
            parts.Add(app.Arguments.Trim());
        }

        for (int i = 0; i < sites.Count; i++)
        {
            // Firefox takes one site per window switch; the rest go in as extra tabs.
            parts.Add(browser!.IsFirefox && i > 0 ? $"-new-tab \"{sites[i]}\"" : $"\"{sites[i]}\"");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>True for a browser entry that opens its own websites.</summary>
    public static bool HasWebsites(AppEntry app) => IsBrowser(app) && WebsitesFor(app).Count > 0;

    /// <summary>The entry's websites, cleaned up ("kick.com" becomes "https://kick.com").</summary>
    public static List<string> WebsitesFor(AppEntry app) =>
        (app.Websites ?? new List<string>())
        .Select(NormalizeWebsite)
        .Where(s => s.Length > 0)
        .ToList();

    /// <summary>Adds https:// when a website was typed without it. Leaves about:, file: and similar alone.</summary>
    public static string NormalizeWebsite(string? site)
    {
        string text = (site ?? "").Trim().Replace("\"", "");
        if (text.Length == 0 || text.Contains("://") || text.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("edge:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return "https://" + text;
    }

    /// <summary>The browser's windows open right now (to spot the new one after opening a private window).</summary>
    public static HashSet<IntPtr> CurrentWindows(AppEntry app) =>
        WindowFinder.GetAppWindows(includeNotInTaskbar: true)
            .Where(w => WindowMatcher.SameProcess(app, w))
            .Select(w => w.Handle)
            .ToHashSet();

    /// <summary>
    /// After opening a private window: waits for the browser's new window and remembers it as private.
    /// Returns false if no new window showed up in time.
    /// </summary>
    public static bool RememberNewWindow(AppEntry app, HashSet<IntPtr> before, TimeSpan timeout)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var fresh = WindowFinder.GetAppWindows(includeNotInTaskbar: true)
                .FirstOrDefault(w => WindowMatcher.SameProcess(app, w) && !before.Contains(w.Handle));
            if (fresh != null)
            {
                Remember(fresh.Handle);
                Log.Info($"'{app.Name}' opened a private window ('{fresh.Title}').");
                return true;
            }

            Thread.Sleep(200);
        }

        Log.Warn($"'{app.Name}' was asked for a private window, but no new window appeared within {timeout.TotalSeconds:0}s.");
        return false;
    }

    /// <summary>You said this open window is (or isn't) private, e.g. by ticking it in the profile window.</summary>
    public static void MarkPrivate(IntPtr handle, bool isPrivate)
    {
        if (isPrivate)
        {
            Remember(handle);
            return;
        }

        lock (Gate)
        {
            KnownPrivate.Remove(handle);
        }
    }

    private static void Remember(IntPtr handle)
    {
        lock (Gate)
        {
            KnownPrivate.RemoveWhere(h => !NativeMethods.IsWindow(h)); // forget closed windows
            KnownPrivate.Add(handle);
        }
    }

    private static Browser? Find(string? processName) =>
        processName == null
            ? null
            : Browsers.FirstOrDefault(b => string.Equals(b.Process, WindowMatcher.StripExe(processName), StringComparison.OrdinalIgnoreCase));

    private static bool Has(string title, string text) => title.Contains(text, StringComparison.OrdinalIgnoreCase);
}
