using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;

namespace LaunchStage.Core.Engine;

/// <summary>
/// Closes windows one at a time, exactly like clicking X, waiting for any "Save changes?" prompt before
/// moving on. Used both when a profile closes other apps and when a profile itself is closed.
/// </summary>
public static class WindowCloseRunner
{
    /// <summary>Returns false if the user chose "Stop".</summary>
    public static bool CloseOneAtATime(IReadOnlyList<WindowInfo> targets, IActivationUi ui, ActivationResult result)
    {
        // The windows being closed aren't prompts, even if an app uses a dialog-style main window.
        var targetHandles = new HashSet<IntPtr>(targets.Select(t => t.Handle));

        for (int i = 0; i < targets.Count; i++)
        {
            var window = targets[i];
            string label = window.AppName;

            while (true)
            {
                // If this app is still asking something (from closing one of its other windows), wait for the answer
                // before touching it again. Closing a second window mid-question can crash some apps.
                WaitWhileAppIsAsking(window, label, ui, targetHandles);

                if (WindowFinder.IsWindowGone(window.Handle))
                {
                    break; // it closed along with an earlier window
                }

                ui.Status($"Closing {label}... ({i + 1} of {targets.Count})");
                var outcome = WindowCloser.CloseAndWait(window);

                if (outcome == CloseOutcome.Closed)
                {
                    Log.Info($"Closed {label} ('{window.Title}').");

                    // Give an app with more windows a moment to finish (it may close the rest itself).
                    if (targets.Skip(i + 1).Any(t => t.ProcessId == window.ProcessId))
                    {
                        Thread.Sleep(1000);
                    }

                    break;
                }

                if (outcome == CloseOutcome.AccessDenied)
                {
                    string message = $"Couldn't close {label}: it's running as administrator (turn on Admin support in Settings).";
                    result.Problems.Add(message);
                    Log.Warn(message);
                    break;
                }

                var choice = ui.AskCloseFailed(label);
                Log.Info($"{label} didn't close; user chose {choice}.");

                if (choice == CloseFailureChoice.StopProfile)
                {
                    return false;
                }

                if (choice == CloseFailureChoice.LeaveOpenAndContinue)
                {
                    break;
                }

                // TryAgain: loop round and ask it to close again.
            }
        }

        return true;
    }

    private static void WaitWhileAppIsAsking(WindowInfo window, string label, IActivationUi ui, ICollection<IntPtr> targetHandles)
    {
        bool told = false;
        while (WindowFinder.HasVisibleDialog(window.ProcessId, targetHandles))
        {
            if (!told)
            {
                ui.Status($"Waiting for you to answer {label}...");
                Log.Info($"{label} is showing a dialog; waiting for the user.");
                told = true;
            }

            Thread.Sleep(300);
        }
    }
}
