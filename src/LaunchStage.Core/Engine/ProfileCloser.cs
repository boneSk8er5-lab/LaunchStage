using System.Diagnostics;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

/// <summary>
/// Closes a profile: every app in it that's marked "close with profile" is closed one at a time,
/// in the profile's order, waiting for save prompts. Nothing is ever force-closed.
/// </summary>
public sealed class ProfileCloser
{
    private readonly IActivationUi _ui;
    private readonly AppSettings _settings;
    private readonly ActivationOptions _options;

    public ProfileCloser(IActivationUi ui, AppSettings settings, ActivationOptions? options = null)
    {
        _ui = ui;
        _settings = settings;
        _options = options ?? new ActivationOptions();
    }

    public ActivationResult Close(Profile profile)
    {
        var result = new ActivationResult();
        var timer = Stopwatch.StartNew();
        Log.Info($"===== Closing profile '{profile.Name}' =====");

        var openWindows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);
        var assigned = WindowMatcher.AssignWindows(profile, openWindows);

        // Apps that remember where their windows were (overlays, OBS docks...) save it as they close. Moving the
        // windows back to their profile spots first means they remember the profile's layout, not where they
        // were dragged during the session.
        if (profile.ResetPositionsBeforeClosing)
        {
            PutBackBeforeClosing(profile, assigned);
        }

        var targets = new List<WindowInfo>();
        var mainWindowAppsDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in profile.Apps.Where(a => a.CloseWithProfile))
        {
            string? process = WindowMatcher.GetProcessName(app);
            if (process != null && ClosesMainWindowOnly(process))
            {
                if (mainWindowAppsDone.Add(process))
                {
                    AddMainWindow(profile, app, process, openWindows, targets, result);
                }

                continue;
            }

            IEnumerable<WindowInfo> windows = openWindows.Where(w => WindowMatcher.Matches(app, w));

            if (ClosesPickedWindowsOnly(profile, app))
            {
                // Only this exact window; the app closes its other windows itself.
                assigned.TryGetValue(app, out var picked);
                if (picked == null || !WindowMatcher.SameTitle(picked.Title, app.CapturedTitle))
                {
                    if (windows.Any())
                    {
                        string message = $"Couldn't find {app.Name}'s '{app.CapturedTitle}' window, so its windows were left open.";
                        result.Problems.Add(message);
                        Log.Warn(message);
                    }

                    continue;
                }

                windows = new[] { picked };
            }

            foreach (var window in windows)
            {
                if (targets.All(t => t.Handle != window.Handle)
                    && !ProtectedApps.IsProtected(window, _settings, _options.ExtraProtectedProcesses))
                {
                    targets.Add(window);
                }
            }
        }

        if (targets.Count == 0)
        {
            Log.Info("Nothing to close; none of the profile's closing apps are open.");
        }
        else if (!WindowCloseRunner.CloseOneAtATime(targets, _ui, result))
        {
            result.Stopped = true;
            Log.Info("Closing was stopped by the user.");
            return result;
        }

        if (profile.RestoreKeptApps)
        {
            RestoreKeptApps(profile, result);
        }

        WindowSnapshot.Delete(profile.Name);

        Log.Info($"===== Closed '{profile.Name}' in {timer.Elapsed.TotalSeconds:0.0}s with {result.Problems.Count} problem(s) =====");
        return result;
    }

    /// <summary>
    /// Apps that must only be closed through their main window. Closing one of their other windows first deletes
    /// it (Transparent Twitch Chat asks to remove that chat window), so LaunchStage closes the main window and the
    /// app closes the rest itself.
    /// </summary>
    private static readonly string[] MainWindowOnlyApps = { "TransparentTwitchChatWPF", "TransparentTwitchChat" };

    private static bool ClosesMainWindowOnly(string processName) =>
        MainWindowOnlyApps.Contains(processName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the app's main window (its windows, minus any owned by or inside another window) and queues just
    /// that one to close.
    /// </summary>
    private void AddMainWindow(Profile profile, AppEntry app, string process, List<WindowInfo> openWindows,
        List<WindowInfo> targets, ActivationResult result)
    {
        var mainWindows = WindowFinder.GetTopLevelWindows(process);
        if (mainWindows.Count == 0)
        {
            if (openWindows.Any(w => WindowMatcher.SameProcess(app, w)))
            {
                string message = $"Couldn't find {app.Name}'s main window, so it was left open.";
                result.Problems.Add(message);
                Log.Warn(message);
            }
            else
            {
                Log.Info($"'{app.Name}' isn't open; nothing to close.");
            }

            return;
        }

        // If there's more than one, skip windows saved in the profile as one of the app's other windows (overlays).
        var otherTitles = WindowMatcher.Siblings(profile, app)
            .Where(s => !string.IsNullOrEmpty(s.CapturedTitle))
            .Select(s => s.CapturedTitle!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var main = mainWindows.FirstOrDefault(w => !otherTitles.Contains(w.Title)) ?? mainWindows[0];

        string title = main.Title.Length > 0 ? $"'{main.Title}'" : "no title";
        Log.Info($"'{app.Name}': closing only its main window ({title}, {main.Bounds.Width}x{main.Bounds.Height}, " +
                 $"{mainWindows.Count} main window(s) found); the app closes its other windows itself.");

        if (!ProtectedApps.IsProtected(main, _settings, _options.ExtraProtectedProcesses))
        {
            targets.Add(main);
        }
    }

    private void PutBackBeforeClosing(Profile profile, Dictionary<AppEntry, WindowInfo> assigned)
    {
        var monitors = Monitors.GetAll();
        foreach (var (app, window) in assigned)
        {
            // Only windows that are about to close: ticked ones, and the other windows of an app whose main
            // window is ticked (they close along with it). Apps that stay open are handled by RestoreKeptApps.
            bool closing = app.CloseWithProfile || WindowMatcher.Siblings(profile, app).Any(s => s.CloseWithProfile);
            if (!closing || app.Position == null || app.Behavior == AppBehavior.LaunchOnly)
            {
                continue;
            }

            var monitor = Monitors.Resolve(app.Position, monitors, out string? note);
            var target = Monitors.ToScreenRect(app.Position, monitor, exact: note == null);
            if (WindowMover.IsAt(window.Handle, target, app.Position.State))
            {
                continue;
            }

            _ui.Status($"Putting {app.Name} back in its spot...");
            if (WindowMover.Apply(window.Handle, target, app.Position.State, out string? error))
            {
                Log.Info($"Put '{app.Name}' ('{window.Title}') back in its profile spot before closing, so it remembers that spot.");
            }
            else
            {
                Log.Warn($"Couldn't put '{app.Name}' back before closing: {error}.");
            }
        }
    }

    /// <summary>
    /// True when this app has several windows in the profile and only some are ticked to close: then only the
    /// ticked windows are closed (matched by title), and the app is trusted to close the rest itself.
    /// </summary>
    public static bool ClosesPickedWindowsOnly(Profile profile, AppEntry app) =>
        WindowMatcher.IsOneOfSeveral(profile, app) && WindowMatcher.Siblings(profile, app).Any(s => !s.CloseWithProfile);

    /// <summary>Moves apps that stay open (e.g. Discord) back to where they were before the profile started.</summary>
    private void RestoreKeptApps(Profile profile, ActivationResult result)
    {
        // Unticked windows of an app whose main window was closed went with it; they aren't "kept".
        var kept = profile.Apps.Where(a => !a.CloseWithProfile && !WindowMatcher.Siblings(profile, a).Any(s => s.CloseWithProfile)).ToList();
        if (kept.Count == 0)
        {
            return;
        }

        var snapshot = WindowSnapshot.Load(profile.Name);
        if (snapshot.Count == 0)
        {
            Log.Info("No before-profile positions saved, so kept apps stay where they are.");
            return;
        }

        var monitors = Monitors.GetAll();
        var used = new HashSet<SnapshotWindow>();
        var openWindows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);

        foreach (var app in kept)
        {
            foreach (var window in openWindows.Where(w => WindowMatcher.Matches(app, w)))
            {
                // Same window as before if it's still open; otherwise any saved window of the same app.
                var saved = snapshot.FirstOrDefault(s => !used.Contains(s) && s.Handle == window.Handle.ToInt64()
                                                         && string.Equals(s.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
                            ?? snapshot.FirstOrDefault(s => !used.Contains(s)
                                                            && string.Equals(s.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (saved == null)
                {
                    Log.Info($"'{app.Name}' wasn't open before the profile started, so it stays where it is.");
                    continue;
                }

                used.Add(saved);
                var monitor = Monitors.Resolve(saved.Position, monitors, out string? note);
                var target = Monitors.ToScreenRect(saved.Position, monitor, exact: note == null);

                _ui.Status($"Putting {window.AppName} back...");
                if (WindowMover.Apply(window.Handle, target, saved.Position.State, out string? error))
                {
                    Log.Info($"Put '{app.Name}' back where it was: {target} ({saved.Position.State}).");
                }
                else
                {
                    string message = $"Couldn't move {app.Name} back: {error}.";
                    result.Problems.Add(message);
                    Log.Warn(message);
                }
            }
        }
    }
}
