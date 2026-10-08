using System.Runtime.InteropServices;
using LaunchStage.Core.Models;
using LaunchStage.Core.Native;

namespace LaunchStage.Core.Desktop;

public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required Rect Bounds { get; init; }
    public required Rect WorkArea { get; init; }
    public bool IsPrimary { get; init; }

    /// <summary>1, 2, 3... numbered left to right.</summary>
    public int Number { get; set; }

    public override string ToString() =>
        $"Monitor {Number}{(IsPrimary ? " (primary)" : "")}: {Bounds.Width}x{Bounds.Height} at ({Bounds.X},{Bounds.Y}) [{DeviceName}]";
}

public static class Monitors
{
    /// <summary>All connected monitors, numbered left to right.</summary>
    public static List<MonitorInfo> GetAll()
    {
        var found = new List<MonitorInfo>();

        NativeMethods.MonitorEnumProc callback = (hMonitor, hdc, rectPtr, data) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                found.Add(new MonitorInfo
                {
                    DeviceName = info.szDevice ?? "",
                    Bounds = ToRect(info.rcMonitor),
                    WorkArea = ToRect(info.rcWork),
                    IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0
                });
            }

            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        var ordered = found.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Number = i + 1;
        }

        return ordered;
    }

    /// <summary>The monitor a rectangle mostly sits on.</summary>
    public static MonitorInfo FromRect(IReadOnlyList<MonitorInfo> monitors, Rect rect)
    {
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No monitors were found.");
        }

        var byCenter = monitors.FirstOrDefault(m => m.Bounds.Contains(rect.CenterX, rect.CenterY));
        if (byCenter != null)
        {
            return byCenter;
        }

        MonitorInfo best = monitors[0];
        long bestArea = 0;
        foreach (var monitor in monitors)
        {
            long area = OverlapArea(monitor.Bounds, rect);
            if (area > bestArea)
            {
                bestArea = area;
                best = monitor;
            }
        }

        if (bestArea > 0)
        {
            return best;
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Finds the monitor a saved position belongs to. Falls back sensibly when monitors were
    /// unplugged, renumbered after a driver update, or changed resolution. <paramref name="note"/>
    /// explains any fallback.
    /// </summary>
    public static MonitorInfo Resolve(WindowPosition position, IReadOnlyList<MonitorInfo> monitors, out string? note)
    {
        note = null;
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No monitors were found.");
        }

        var saved = position.MonitorBounds;

        var exact = monitors.FirstOrDefault(m => SameName(m, position) && m.Bounds.SameSize(saved));
        if (exact != null)
        {
            return exact;
        }

        var samePlace = monitors.FirstOrDefault(m => m.Bounds.SamePlace(saved));
        if (samePlace != null)
        {
            note = "the monitor's ID changed, so it was matched by position and size";
            return samePlace;
        }

        var sameName = monitors.FirstOrDefault(m => SameName(m, position));
        if (sameName != null)
        {
            note = "the monitor's resolution changed, so the window was scaled to fit";
            return sameName;
        }

        var sameSize = monitors.FirstOrDefault(m => m.Bounds.SameSize(saved));
        if (sameSize != null)
        {
            note = "the original monitor wasn't found, so another monitor with the same resolution was used";
            return sameSize;
        }

        note = "the original monitor wasn't found (unplugged or off?), so the window went to the primary monitor";
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Turns a saved position into screen coordinates on the given monitor.
    /// When <paramref name="exact"/> is true (the original monitor was found unchanged) the saved spot is used as-is,
    /// even if the window hangs over onto another monitor. Otherwise it's scaled and kept on screen.
    /// </summary>
    public static Rect ToScreenRect(WindowPosition position, MonitorInfo monitor, bool exact)
    {
        if (exact)
        {
            return new Rect(monitor.Bounds.X + position.X, monitor.Bounds.Y + position.Y, position.Width, position.Height);
        }

        var saved = position.MonitorBounds;
        double scaleX = saved.Width > 0 ? (double)monitor.Bounds.Width / saved.Width : 1.0;
        double scaleY = saved.Height > 0 ? (double)monitor.Bounds.Height / saved.Height : 1.0;

        int width = Math.Max(100, (int)Math.Round(position.Width * scaleX));
        int height = Math.Max(60, (int)Math.Round(position.Height * scaleY));

        // Windows have invisible borders, so allow a little overhang past the monitor edges.
        width = Math.Min(width, monitor.Bounds.Width + 32);
        height = Math.Min(height, monitor.Bounds.Height + 32);

        int x = monitor.Bounds.X + (int)Math.Round(position.X * scaleX);
        int y = monitor.Bounds.Y + (int)Math.Round(position.Y * scaleY);
        x = Math.Clamp(x, monitor.Bounds.X - 16, monitor.Bounds.Right - 100);
        y = Math.Clamp(y, monitor.Bounds.Y - 16, monitor.Bounds.Bottom - 50);

        return new Rect(x, y, width, height);
    }

    internal static Rect ToRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private static bool SameName(MonitorInfo monitor, WindowPosition position) =>
        string.Equals(monitor.DeviceName, position.Monitor, StringComparison.OrdinalIgnoreCase);

    private static long OverlapArea(Rect a, Rect b)
    {
        int w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        int h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}
