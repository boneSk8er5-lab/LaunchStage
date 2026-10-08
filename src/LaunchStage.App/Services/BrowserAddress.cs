using System.Windows.Automation;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;

namespace LaunchStageApp.Services;

/// <summary>
/// Reads the website a browser window is showing, from its address bar (through Windows' accessibility support,
/// the same way screen readers do). Works for Chrome, Brave, Edge, Vivaldi, Opera and Firefox most of the time;
/// returns null when it can't tell.
/// </summary>
internal static class BrowserAddress
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(4);

    public static string? TryRead(IntPtr window)
    {
        try
        {
            // Run with a time limit: a busy browser can take a while to answer.
            var read = Task.Run(() => Read(window));
            if (!read.Wait(Limit))
            {
                Log.Info("Reading a browser's address bar took too long; skipped.");
                return null;
            }

            return read.Result;
        }
        catch (Exception ex)
        {
            Log.Debug($"Couldn't read a browser's address bar: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>For a browser window just added to a profile: saves the website it's showing, if it can be read.</summary>
    public static void FillIn(LaunchStage.Core.Models.AppEntry entry, IntPtr window)
    {
        if (!BrowserPrivacy.IsBrowser(entry))
        {
            return;
        }

        string? site = TryRead(window);
        if (site != null)
        {
            entry.Websites = new List<string> { site };
            Log.Info($"'{entry.Name}' window is showing {site}; saved as its website.");
        }
    }

    private static string? Read(IntPtr window)
    {
        var root = AutomationElement.FromHandle(window);

        // Chromium browsers show the address bar as a text box; Firefox as a drop-down box.
        var addressLike = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));

        // Chromium browsers build their accessibility tree on the first request, so look twice. The browser's own
        // toolbar comes before the page in the tree, so the first box with a web address is the address bar.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (AutomationElement box in root.FindAll(TreeScope.Descendants, addressLike))
            {
                if (box.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern) && pattern is ValuePattern value)
                {
                    string text = value.Current.Value?.Trim() ?? "";
                    if (text.Length > 0 && !text.Contains(' ') && text.Contains('.'))
                    {
                        return BrowserPrivacy.NormalizeWebsite(text);
                    }
                }
            }

            Thread.Sleep(300);
        }

        return null;
    }
}
