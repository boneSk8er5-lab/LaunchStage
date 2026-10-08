using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

/// <summary>
/// Re-snap: puts a profile's open windows back in their spots. Nothing is opened, closed or minimized; apps that
/// aren't open are simply skipped.
/// </summary>
public static class ProfileResnapper
{
    public static ActivationResult Resnap(Profile profile)
    {
        var result = new ActivationResult();
        Log.Info($"===== Putting '{profile.Name}' windows back =====");

        var monitors = Monitors.GetAll();
        var assigned = WindowMatcher.AssignWindows(profile, WindowFinder.GetAppWindows(includeNotInTaskbar: true));
        int moved = 0;
        int alreadyThere = 0;

        foreach (var app in profile.Apps)
        {
            if (app.Behavior == AppBehavior.LaunchOnly || app.Position == null)
            {
                continue;
            }

            if (!assigned.TryGetValue(app, out var window))
            {
                Log.Info($"'{app.Name}' isn't open; skipped.");
                continue;
            }

            var monitor = Monitors.Resolve(app.Position, monitors, out string? note);
            if (note != null)
            {
                result.Notes.Add($"{app.Name}: {note}.");
            }

            var target = Monitors.ToScreenRect(app.Position, monitor, exact: note == null);
            if (WindowMover.IsAt(window.Handle, target, app.Position.State))
            {
                alreadyThere++;
                continue;
            }

            if (WindowMover.Apply(window.Handle, target, app.Position.State, out string? error))
            {
                moved++;
                Log.Info($"Put '{app.Name}' back on monitor {monitor.Number}: {target} ({app.Position.State}).");
            }
            else
            {
                string message = $"Couldn't move {app.Name}: {error}.";
                result.Problems.Add(message);
                Log.Warn(message);
            }
        }

        if (moved == 0 && alreadyThere == 0 && result.Problems.Count == 0)
        {
            result.Notes.Add("None of its windows are open, so there was nothing to put back.");
        }

        Log.Info($"===== Put back {moved} window(s) of '{profile.Name}'; {alreadyThere} already in place =====");
        return result;
    }
}
