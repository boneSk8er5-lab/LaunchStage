using LaunchStage.Core.Desktop;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

/// <summary>Decides which open window belongs to which app in a profile.</summary>
public static class WindowMatcher
{
    /// <summary>The process name to look for, or null when the app can't be matched (e.g. a plain URL).</summary>
    public static string? GetProcessName(AppEntry app)
    {
        string? name = null;
        if (!string.IsNullOrWhiteSpace(app.ProcessName))
        {
            name = app.ProcessName.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(app.Path)
                 && app.Path.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(app.Path.Trim());
        }

        return name == null ? null : StripExe(name);
    }

    /// <summary>Same app (by process), ignoring title and class. Used to decide what's "in" a profile.</summary>
    public static bool SameProcess(AppEntry app, WindowInfo window)
    {
        string? name = GetProcessName(app);
        return name != null && string.Equals(window.ProcessName, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Full match: process plus the optional title and class filters.</summary>
    public static bool Matches(AppEntry app, WindowInfo window)
    {
        if (!SameProcess(app, window))
        {
            return false;
        }

        // A private browser entry only takes private windows, and a normal one only normal windows.
        if (BrowserPrivacy.IsBrowser(window.ProcessName) && BrowserPrivacy.IsPrivate(window) != app.PrivateWindow)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(app.TitleContains)
            && window.Title.IndexOf(app.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(app.WindowClass)
            && !string.Equals(window.ClassName, app.WindowClass, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The best open window for this app: the one with the same title as when it was saved, if there is one;
    /// otherwise the first matching window. titleOnly: only accept the same title. With a profile given, a window
    /// whose title belongs to another window of the same app in the profile is never taken (so the main window
    /// doesn't grab an overlay's spot, or the reverse).
    /// </summary>
    public static WindowInfo? PickBest(AppEntry app, IEnumerable<WindowInfo> candidates, bool titleOnly = false, Profile? profile = null)
    {
        var matching = candidates.Where(w => Matches(app, w)).ToList();
        if (!string.IsNullOrEmpty(app.CapturedTitle))
        {
            var sameTitle = matching.FirstOrDefault(w => SameTitle(w.Title, app.CapturedTitle));
            if (sameTitle != null || titleOnly)
            {
                return sameTitle;
            }
        }

        if (profile != null)
        {
            var otherTitles = Siblings(profile, app)
                .Where(s => !ReferenceEquals(s, app) && !string.IsNullOrEmpty(s.CapturedTitle))
                .Select(s => WithoutCounter(s.CapturedTitle!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return matching.FirstOrDefault(w => !otherTitles.Contains(WithoutCounter(w.Title)));
        }

        return matching.FirstOrDefault();
    }

    /// <summary>Every entry in the profile for the same app (process) as this one, including itself.</summary>
    public static List<AppEntry> Siblings(Profile profile, AppEntry app)
    {
        string? process = GetProcessName(app);
        return process == null
            ? new List<AppEntry> { app }
            : profile.Apps.Where(a => string.Equals(GetProcessName(a), process, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Which open window belongs to which app in the profile: windows with the saved title first, then the rest
    /// in order. Each window goes to at most one app.
    /// </summary>
    public static Dictionary<AppEntry, WindowInfo> AssignWindows(Profile profile, IReadOnlyList<WindowInfo> openWindows)
    {
        var assigned = new Dictionary<AppEntry, WindowInfo>();
        var used = new HashSet<IntPtr>();

        foreach (bool titleOnly in new[] { true, false })
        {
            foreach (var app in profile.Apps.Where(a => !assigned.ContainsKey(a)))
            {
                var window = PickBest(app, openWindows.Where(w => !used.Contains(w.Handle)), titleOnly, titleOnly ? null : profile);
                if (window != null)
                {
                    assigned[app] = window;
                    used.Add(window.Handle);
                }
            }
        }

        return assigned;
    }

    /// <summary>The profile has more than one window of this app, and this one's title is known.</summary>
    public static bool IsOneOfSeveral(Profile profile, AppEntry app) =>
        !string.IsNullOrEmpty(app.CapturedTitle) && Siblings(profile, app).Count > 1;

    /// <summary>
    /// Same window title, ignoring a notification counter at the start: "(7) Twitch - Brave" and "(12) Twitch - Brave"
    /// are the same window.
    /// </summary>
    public static bool SameTitle(string? a, string? b) =>
        string.Equals(WithoutCounter(a), WithoutCounter(b), StringComparison.OrdinalIgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex Counter = new(@"^\(\d+\+?\)\s*");

    private static string WithoutCounter(string? title) => Counter.Replace((title ?? "").Trim(), "");

    public static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
