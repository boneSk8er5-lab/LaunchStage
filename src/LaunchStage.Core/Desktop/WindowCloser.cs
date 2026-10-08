using System.Diagnostics;
using System.Runtime.InteropServices;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Native;

namespace LaunchStage.Core.Desktop;

public enum CloseOutcome
{
    Closed,
    StillOpen,
    AccessDenied
}

public static class WindowCloser
{
    // How long an app gets before we decide whether it's really staying open.
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(4);

    // How long the window must sit open with no dialog showing before we say it didn't close.
    private static readonly TimeSpan QuietLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Asks a window to close, exactly like clicking its X, so the app can offer to save first.
    /// Waits with no time limit while the app shows a dialog. Never force-closes anything.
    /// </summary>
    public static CloseOutcome CloseAndWait(WindowInfo window)
    {
        IntPtr hWnd = window.Handle;
        if (WindowFinder.IsWindowGone(hWnd))
        {
            return CloseOutcome.Closed;
        }

        if (!NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
        {
            if (WindowFinder.IsWindowGone(hWnd))
            {
                return CloseOutcome.Closed;
            }

            int code = Marshal.GetLastWin32Error();
            Log.Warn($"Close request to {window.ProcessName} was blocked (Windows error {code}).");
            return CloseOutcome.AccessDenied;
        }

        var timer = Stopwatch.StartNew();
        TimeSpan? quietSince = null;

        while (true)
        {
            Thread.Sleep(200);

            if (WindowFinder.IsWindowGone(hWnd))
            {
                return CloseOutcome.Closed;
            }

            // A disabled window or an owned popup means a dialog (like "Save changes?") is waiting for the user.
            bool waitingOnUser = !NativeMethods.IsWindowEnabled(hWnd)
                                 || WindowFinder.HasVisibleOwnedWindow(hWnd)
                                 || WindowFinder.HasVisibleDialog(window.ProcessId);
            if (waitingOnUser || timer.Elapsed < GracePeriod)
            {
                quietSince = null;
                continue;
            }

            quietSince ??= timer.Elapsed;
            if (timer.Elapsed - quietSince.Value >= QuietLimit)
            {
                return CloseOutcome.StillOpen; // e.g. the user pressed Cancel
            }
        }
    }
}
