using LaunchStage.Core.Desktop;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

/// <summary>Turns windows that are open right now into profile entries ("use current apps and positions").</summary>
public static class ProfileCapture
{
    public static AppEntry CreateEntry(WindowInfo window, IReadOnlyList<MonitorInfo> monitors)
    {
        var entry = new AppEntry
        {
            Name = window.AppName,
            Path = window.ExePath ?? "",
            ProcessName = window.ProcessName,
            Position = CapturePosition(window, monitors),
            CapturedTitle = window.Title,
            PrivateWindow = BrowserPrivacy.IsPrivate(window),
            // An app that's running as administrator now should start that way too.
            RunAsAdmin = WindowFinder.IsProcessElevated(window.ProcessId) == true
        };

        if (IsStoreApp(window))
        {
            // Store apps all run inside ApplicationFrameHost, so match by title and don't try to launch the host.
            entry.Name = window.Title;
            entry.Path = "";
            entry.TitleContains = window.Title;
            entry.Behavior = AppBehavior.PositionOnly;
        }

        return entry;
    }

    public static bool IsStoreApp(WindowInfo window) =>
        string.Equals(window.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);

    public static WindowPosition CapturePosition(WindowInfo window, IReadOnlyList<MonitorInfo> monitors)
    {
        // For maximized and minimized windows, save the size they return to.
        Rect rect = window.Bounds.Copy();
        if (window.State != WindowState.Normal)
        {
            var normal = WindowFinder.GetNormalBounds(window.Handle, monitors);
            if (normal != null)
            {
                rect = normal;
            }
        }

        // A maximized window's monitor is where it's maximized right now.
        var monitor = Monitors.FromRect(monitors, window.State == WindowState.Maximized ? window.Bounds : rect);

        if (!monitor.Bounds.Contains(rect.CenterX, rect.CenterY))
        {
            // Its restore size sits on another monitor; centre it on this one instead.
            int width = Math.Min(rect.Width, monitor.WorkArea.Width);
            int height = Math.Min(rect.Height, monitor.WorkArea.Height);
            rect = new Rect(
                monitor.WorkArea.X + (monitor.WorkArea.Width - width) / 2,
                monitor.WorkArea.Y + (monitor.WorkArea.Height - height) / 2,
                width,
                height);
        }

        return new WindowPosition
        {
            Monitor = monitor.DeviceName,
            MonitorNumber = monitor.Number,
            MonitorBounds = monitor.Bounds.Copy(),
            X = rect.X - monitor.Bounds.X,
            Y = rect.Y - monitor.Bounds.Y,
            Width = rect.Width,
            Height = rect.Height,
            State = window.State
        };
    }
}
