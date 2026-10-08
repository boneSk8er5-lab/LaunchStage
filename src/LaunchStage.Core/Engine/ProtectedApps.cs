using LaunchStage.Core.Desktop;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

/// <summary>Apps that are never closed or minimized: parts of Windows, LaunchStage itself, and the user's Never close list.</summary>
public static class ProtectedApps
{
    private static readonly string[] BuiltIn =
    {
        "LaunchStage",
        "LaunchStageCli",
        "TextInputHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "LockApp"
    };

    public static bool IsProtected(WindowInfo window, AppSettings settings, IEnumerable<string>? extra = null)
    {
        return InList(BuiltIn) || InList(settings.NeverClose) || (extra != null && InList(extra));

        bool InList(IEnumerable<string> names) =>
            names.Any(n => string.Equals(WindowMatcher.StripExe(n.Trim()), window.ProcessName, StringComparison.OrdinalIgnoreCase));
    }
}
