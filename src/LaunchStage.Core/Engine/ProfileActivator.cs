using System.Diagnostics;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

public sealed class ActivationOptions
{
    /// <summary>Extra process names to never close or minimize during this run (e.g. the terminal running the command).</summary>
    public IReadOnlyCollection<string> ExtraProtectedProcesses { get; init; } = Array.Empty<string>();
}

public sealed class ActivationResult
{
    /// <summary>True when the user chose "Stop profile".</summary>
    public bool Stopped { get; set; }

    /// <summary>Things that went wrong (an app didn't open, a window couldn't be moved).</summary>
    public List<string> Problems { get; } = new();

    /// <summary>Worth knowing but not wrong (e.g. a monitor was missing so a fallback was used).</summary>
    public List<string> Notes { get; } = new();
}

/// <summary>
/// Puts the PC into a profile, in strict phases:
/// 1. Close apps not in the profile, one at a time, waiting for any save prompts (if Close is chosen).
/// 2. Minimize apps not in the profile (if Minimize is chosen).
/// 3. Open what's missing, reuse what's already open, and move every window into place. Games go last.
/// 4. After a short wait, put back any window that moved itself (splash screens, apps that restore their own position).
/// </summary>
public sealed class ProfileActivator
{
    private readonly IActivationUi _ui;
    private readonly AppSettings _settings;
    private readonly ActivationOptions _options;

    public ProfileActivator(IActivationUi ui, AppSettings settings, ActivationOptions? options = null)
    {
        _ui = ui;
        _settings = settings;
        _options = options ?? new ActivationOptions();
    }

    public ActivationResult Activate(Profile profile)
    {
        var result = new ActivationResult();
        var timer = Stopwatch.StartNew();
        Log.Info($"===== Activating profile '{profile.Name}' ({profile.Apps.Count} apps, other apps: {profile.OtherApps}) =====");

        var monitors = Monitors.GetAll();
        foreach (var monitor in monitors)
        {
            Log.Info(monitor.ToString());
        }

        // Remember where the profile's apps are now, so closing the profile can put kept apps back.
        WindowSnapshot.SaveIfNeeded(profile, monitors);

        // Phase 1 and 2: deal with apps that aren't part of this profile.
        if (profile.OtherApps == OtherAppsAction.Close)
        {
            if (!RunClosePhase(profile, result))
            {
                result.Stopped = true;
                Log.Info("Profile stopped by the user during the close phase.");
                return result;
            }
        }
        else if (profile.OtherApps == OtherAppsAction.Minimize)
        {
            RunMinimizePhase(profile);
        }

        // Phase 3: launch and position. Programs first, games last so they don't steal focus.
        var claimed = new HashSet<IntPtr>();
        var placed = new List<PlacedWindow>();

        var programs = profile.Apps.Where(a => a.Type == AppType.Program).ToList();
        var games = profile.Apps.Where(a => a.Type == AppType.Game).ToList();
        foreach (var game in games.Where(g => !g.AutoStart))
        {
            Log.Info($"Not starting game '{game.Name}' (auto-start is off).");
        }

        RunLaunchPhase(profile, programs, monitors, claimed, placed, result);
        RunLaunchPhase(profile, games.Where(g => g.AutoStart).ToList(), monitors, claimed, placed, result);

        // Phase 4: second pass for windows that moved themselves or were replaced by a splash screen.
        if (profile.ReapplyDelaySeconds > 0 && placed.Any(p => ShouldPosition(p.App)))
        {
            _ui.Status($"Letting apps settle ({profile.ReapplyDelaySeconds}s)...");
            Thread.Sleep(TimeSpan.FromSeconds(profile.ReapplyDelaySeconds));
            RunReapplyPass(monitors, claimed, placed);
        }

        // Extra windows of profile apps that weren't placed (e.g. an overlay's other windows) count as "not in the profile".
        if (profile.OtherApps != OtherAppsAction.Leave)
        {
            MinimizeExtraWindows(profile, claimed);
        }

        Log.Info($"===== Finished '{profile.Name}' in {timer.Elapsed.TotalSeconds:0.0}s with {result.Problems.Count} problem(s) =====");
        return result;
    }

    // ---------------- Phase 1: close ----------------

    private bool RunClosePhase(Profile profile, ActivationResult result)
    {
        var targets = WindowFinder.GetAppWindows()
            .Where(w => !IsInProfile(profile, w) && !IsProtected(w))
            .ToList();

        if (targets.Count == 0)
        {
            return true;
        }

        Log.Info($"Close phase: {targets.Count} window(s) to close, one at a time.");
        return WindowCloseRunner.CloseOneAtATime(targets, _ui, result);
    }

    // ---------------- Phase 2: minimize ----------------

    private void RunMinimizePhase(Profile profile)
    {
        var targets = WindowFinder.GetAppWindows()
            .Where(w => w.State != WindowState.Minimized && !IsInProfile(profile, w) && !IsProtected(w))
            .ToList();

        foreach (var window in targets)
        {
            WindowMover.Minimize(window.Handle);
        }

        if (targets.Count > 0)
        {
            Log.Info($"Minimized {targets.Count} window(s) that aren't in this profile.");
        }
    }

    private void MinimizeExtraWindows(Profile profile, HashSet<IntPtr> claimed)
    {
        var extras = WindowFinder.GetAppWindows()
            .Where(w => !claimed.Contains(w.Handle)
                        && w.State != WindowState.Minimized
                        && IsInProfile(profile, w)
                        && !IsProtected(w)
                        // A game you opened yourself (auto-start off) is left alone.
                        && !profile.Apps.Any(a => a.Type == AppType.Game && !a.AutoStart && WindowMatcher.SameProcess(a, w)))
            .ToList();

        foreach (var window in extras)
        {
            WindowMover.Minimize(window.Handle);
            Log.Info($"Minimized an extra {window.AppName} window that isn't in this profile ('{window.Title}').");
        }
    }

    // ---------------- Phase 3: launch and position ----------------

    private void RunLaunchPhase(
        Profile profile,
        List<AppEntry> apps,
        List<MonitorInfo> monitors,
        HashSet<IntPtr> claimed,
        List<PlacedWindow> placed,
        ActivationResult result)
    {
        if (apps.Count == 0)
        {
            return;
        }

        var openWindows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);
        var waitingFor = new List<(AppEntry App, HashSet<IntPtr>? NotThese)>();
        var startedThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            // Reuse a window that's already open instead of opening a second copy. A browser window with its own
            // websites only reuses a window that's clearly the same one (same saved title); otherwise it opens a
            // fresh window on its sites instead of taking a window meant for something else.
            bool opensOwnSites = BrowserPrivacy.HasWebsites(app);
            var free = openWindows.Where(w => !claimed.Contains(w.Handle));
            var existing = !opensOwnSites ? WindowMatcher.PickBest(app, free, profile: profile)
                : string.IsNullOrEmpty(app.CapturedTitle) ? null
                : WindowMatcher.PickBest(app, free, titleOnly: true);
            if (existing != null)
            {
                claimed.Add(existing.Handle);
                Log.Info($"'{app.Name}' is already open; reusing it.");
                Place(app, existing.Handle, monitors, placed, result);
                continue;
            }

            // Another window of an app that was just opened (e.g. an overlay app that opens its own extra
            // windows): give it a moment to appear before opening the app a second time.
            string? process = WindowMatcher.GetProcessName(app);
            bool appAlreadyRunning = startedThisRun.Contains(process ?? "")
                                     || openWindows.Any(w => WindowMatcher.SameProcess(app, w));
            if (process != null && appAlreadyRunning && !profile.LaunchAllAtOnce && !app.PrivateWindow && !opensOwnSites)
            {
                _ui.Status($"Waiting for {app.Name}'s other window...");
                IntPtr own = WaitForReadyWindow(app, claimed, TimeSpan.FromSeconds(3), profile);
                if (own != IntPtr.Zero)
                {
                    Log.Info($"'{app.Name}' opened this window itself; no second copy needed.");
                    claimed.Add(own);
                    Place(app, own, monitors, placed, result);
                    continue;
                }
            }

            if (app.Behavior == AppBehavior.PositionOnly)
            {
                Log.Info($"'{app.Name}' isn't open and is set to Position Only, so it was skipped.");
                continue;
            }

            if (app.LaunchDelaySeconds > 0)
            {
                _ui.Status($"Waiting {app.LaunchDelaySeconds:0.#}s before opening {app.Name}...");
                Thread.Sleep(TimeSpan.FromSeconds(app.LaunchDelaySeconds));
            }

            _ui.Status($"Opening {app.Name}...");
            bool openPrivately = app.PrivateWindow && BrowserPrivacy.IsBrowser(app);
            var browserWindowsBefore = openPrivately || opensOwnSites ? BrowserPrivacy.CurrentWindows(app) : null;
            if (!Launcher.Launch(app, out string? error))
            {
                string message = $"{app.Name} didn't open: {error}.";
                result.Problems.Add(message);
                Log.Error(message);
                continue;
            }

            Log.Info($"Launched '{app.Name}' ({app.Path} {BrowserPrivacy.ArgumentsFor(app)}).");
            if (openPrivately && browserWindowsBefore != null)
            {
                // Chrome and Brave don't say "private" in the title, so remember which new window is the private one.
                _ui.Status($"Waiting for {app.Name}'s private window...");
                BrowserPrivacy.RememberNewWindow(app, browserWindowsBefore, TimeSpan.FromSeconds(Math.Max(5, app.LaunchTimeoutSeconds)));
            }
            if (process != null)
            {
                startedThisRun.Add(process);
            }

            // A window opened on its own websites is a new window: never one that was already open.
            var notThese = opensOwnSites ? browserWindowsBefore : null;
            if (profile.LaunchAllAtOnce)
            {
                waitingFor.Add((app, notThese));
            }
            else
            {
                // Default: wait until this app has fully loaded and is in place before starting the next one.
                WaitAndPlace(app, monitors, claimed, placed, result, profile, notThese);
            }
        }

        foreach (var (app, notThese) in waitingFor)
        {
            WaitAndPlace(app, monitors, claimed, placed, result, profile, notThese);
        }
    }

    private void WaitAndPlace(
        AppEntry app,
        List<MonitorInfo> monitors,
        HashSet<IntPtr> claimed,
        List<PlacedWindow> placed,
        ActivationResult result,
        Profile profile,
        HashSet<IntPtr>? notThese = null)
    {
        if (WindowMatcher.GetProcessName(app) == null)
        {
            Log.Info($"'{app.Name}' has no process name to watch for, so its window won't be positioned.");
            if (ShouldPosition(app))
            {
                result.Notes.Add($"{app.Name} opened, but add its \"processName\" to the profile so its window can be positioned.");
            }

            return;
        }

        _ui.Status($"Waiting for {app.Name} to open...");
        int timeoutSeconds = Math.Max(1, app.LaunchTimeoutSeconds);
        IntPtr handle = WaitForReadyWindow(app, claimed, TimeSpan.FromSeconds(timeoutSeconds), profile, notThese);
        if (handle == IntPtr.Zero)
        {
            string message = $"{app.Name} was started, but its window didn't appear within {timeoutSeconds}s.";
            result.Problems.Add(message);
            Log.Warn(message);
            return;
        }

        claimed.Add(handle);
        Place(app, handle, monitors, placed, result);
    }

    /// <summary>
    /// Waits for the app's window to appear AND finish loading:
    /// 1. its window shows up,
    /// 2. the app reports it's ready for input,
    /// 3. the window stops moving and resizing itself for a moment.
    /// A splash screen that closes along the way is skipped and the real window is waited for instead.
    /// When the profile has several windows of this app, for the first few seconds only the window with this
    /// entry's saved title is accepted (so the main window doesn't get an overlay's spot, or the reverse).
    /// </summary>
    private IntPtr WaitForReadyWindow(AppEntry app, HashSet<IntPtr> claimed, TimeSpan timeout, Profile profile,
        HashSet<IntPtr>? notThese = null)
    {
        var timer = Stopwatch.StartNew();
        bool preferTitle = WindowMatcher.IsOneOfSeveral(profile, app);

        // A browser's title is just the page it shows, so it says little about which entry a window is for.
        // After a short look for the saved title, a browser entry takes any new window of that browser, even one
        // showing the page saved for another entry (otherwise it would wait for a title that never comes).
        bool isBrowser = BrowserPrivacy.IsBrowser(app);
        var titleOnlyFor = TimeSpan.FromSeconds(Math.Min(isBrowser ? 2 : 5, timeout.TotalSeconds));
        while (timer.Elapsed < timeout)
        {
            bool titleOnly = preferTitle && timer.Elapsed < titleOnlyFor;
            var match = WindowMatcher.PickBest(
                app,
                WindowFinder.GetAppWindows(includeNotInTaskbar: true)
                    .Where(w => !claimed.Contains(w.Handle) && (notThese == null || !notThese.Contains(w.Handle))),
                titleOnly,
                isBrowser && !titleOnly ? null : profile);

            if (match == null)
            {
                Thread.Sleep(250);
                continue;
            }

            _ui.Status($"Waiting for {app.Name} to finish loading...");
            WaitForInputIdle(match.ProcessId);
            if (WaitUntilSettled(match.Handle))
            {
                Log.Info($"'{app.Name}' was ready after {timer.Elapsed.TotalSeconds:0.0}s.");
                return match.Handle;
            }

            Log.Info($"'{app.Name}' closed its first window (splash screen?); waiting for the real one.");
        }

        return IntPtr.Zero;
    }

    private static void WaitForInputIdle(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForInputIdle(5000);
        }
        catch
        {
            // Some apps (or admin apps) don't support this; the settle check below still applies.
        }
    }

    /// <summary>True once the window has held still for a second; false if it closed.</summary>
    private static bool WaitUntilSettled(IntPtr handle)
    {
        var timer = Stopwatch.StartNew();
        var stillFor = TimeSpan.FromMilliseconds(700);
        var maxWait = TimeSpan.FromSeconds(8);
        Rect? last = null;
        var unchangedSince = TimeSpan.Zero;

        while (timer.Elapsed < maxWait)
        {
            Thread.Sleep(250);
            if (WindowFinder.IsWindowGone(handle))
            {
                return false;
            }

            var now = WindowFinder.GetBounds(handle);
            if (now == null || last == null || !now.SamePlace(last))
            {
                last = now;
                unchangedSince = timer.Elapsed;
            }
            else if (timer.Elapsed - unchangedSince >= stillFor)
            {
                return true;
            }
        }

        return !WindowFinder.IsWindowGone(handle);
    }

    private void Place(
        AppEntry app,
        IntPtr handle,
        List<MonitorInfo> monitors,
        List<PlacedWindow> placed,
        ActivationResult result)
    {
        placed.Add(new PlacedWindow(app, handle));
        if (!ShouldPosition(app))
        {
            return;
        }

        var position = app.Position!;
        var monitor = Monitors.Resolve(position, monitors, out string? note);
        if (note != null)
        {
            result.Notes.Add($"{app.Name}: {note}.");
            Log.Warn($"{app.Name}: {note}.");
        }

        var target = Monitors.ToScreenRect(position, monitor, exact: note == null);
        if (WindowMover.Apply(handle, target, position.State, out string? error))
        {
            Log.Info($"Placed '{app.Name}' on monitor {monitor.Number}: {target} ({position.State}).");
            return;
        }

        string message = $"Couldn't move {app.Name}: {error}.";
        result.Problems.Add(message);
        Log.Warn(message);
    }

    // ---------------- Phase 4: re-apply ----------------

    private void RunReapplyPass(List<MonitorInfo> monitors, HashSet<IntPtr> claimed, List<PlacedWindow> placed)
    {
        foreach (var item in placed.Where(p => ShouldPosition(p.App)))
        {
            if (WindowFinder.IsWindowGone(item.Handle))
            {
                // The window we positioned was a splash screen; find the real one.
                claimed.Remove(item.Handle);
                var replacement = WindowMatcher.PickBest(
                    item.App,
                    WindowFinder.GetAppWindows(includeNotInTaskbar: true).Where(w => !claimed.Contains(w.Handle)));
                if (replacement == null)
                {
                    Log.Info($"'{item.App.Name}' closed its window and no replacement appeared.");
                    continue;
                }

                Log.Info($"'{item.App.Name}' switched to a new window (splash screen?); positioning the new one.");
                item.Handle = replacement.Handle;
                claimed.Add(item.Handle);
            }

            var position = item.App.Position!;
            var monitor = Monitors.Resolve(position, monitors, out string? note);
            var target = Monitors.ToScreenRect(position, monitor, exact: note == null);
            if (WindowMover.IsAt(item.Handle, target, position.State))
            {
                continue;
            }

            Log.Info($"'{item.App.Name}' moved itself after opening; putting it back.");
            if (!WindowMover.Apply(item.Handle, target, position.State, out string? error))
            {
                Log.Warn($"Second pass couldn't move '{item.App.Name}': {error}.");
            }
        }
    }

    // ---------------- Helpers ----------------

    private static bool ShouldPosition(AppEntry app) =>
        app.Behavior != AppBehavior.LaunchOnly && app.Position != null;

    private static bool IsInProfile(Profile profile, WindowInfo window) =>
        profile.Apps.Any(app => WindowMatcher.SameProcess(app, window));

    private bool IsProtected(WindowInfo window) =>
        ProtectedApps.IsProtected(window, _settings, _options.ExtraProtectedProcesses);

    private sealed class PlacedWindow
    {
        public PlacedWindow(AppEntry app, IntPtr handle)
        {
            App = app;
            Handle = handle;
        }

        public AppEntry App { get; }
        public IntPtr Handle { get; set; }
    }
}
