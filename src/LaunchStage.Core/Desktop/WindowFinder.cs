using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Native;

namespace LaunchStage.Core.Desktop;

/// <summary>Finds the app windows a person would see in the taskbar.</summary>
public static class WindowFinder
{
    // Parts of Windows itself that must never be treated as apps.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    };

    /// <summary>
    /// All visible, top-level app windows, in front-to-back order. includeNotInTaskbar adds real app windows that
    /// hide from the taskbar (e.g. an overlay app's main window); used for the apps inside a profile, never for
    /// "other apps".
    /// </summary>
    public static List<WindowInfo> GetAppWindows(bool includeNotInTaskbar = false)
    {
        var windows = new List<WindowInfo>();
        var processes = new Dictionary<uint, (string Name, string? Path)>();
        uint ownProcessId = (uint)Environment.ProcessId;

        NativeMethods.EnumWindowsProc callback = (hWnd, _) =>
        {
            try
            {
                if (!IsAppWindow(hWnd, includeNotInTaskbar, out bool inTaskbar))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == ownProcessId)
                {
                    return true;
                }

                if (!processes.TryGetValue(processId, out var process))
                {
                    process = GetProcessIdentity(processId);
                    processes[processId] = process;
                }

                windows.Add(Describe(hWnd, processId, process.Name, process.Path, inTaskbar));
            }
            catch (Exception ex)
            {
                Log.Debug($"Skipped a window while listing: {ex.Message}");
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    /// <summary>
    /// An app's main window(s): visible windows of that process that aren't owned by another window and aren't a
    /// child of one. A title isn't needed (some overlay apps leave their main window untitled). Front-most first.
    /// </summary>
    public static List<WindowInfo> GetTopLevelWindows(string processName)
    {
        var windows = new List<WindowInfo>();
        var processes = new Dictionary<uint, (string Name, string? Path)>();

        NativeMethods.EnumWindowsProc callback = (hWnd, _) =>
        {
            try
            {
                if (!IsTopLevelAppWindow(hWnd))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (!processes.TryGetValue(processId, out var process))
                {
                    process = GetProcessIdentity(processId);
                    processes[processId] = process;
                }

                if (string.Equals(process.Name, processName, StringComparison.OrdinalIgnoreCase))
                {
                    bool toolWindow = (NativeMethods.GetWindowLongValue(hWnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0;
                    windows.Add(Describe(hWnd, processId, process.Name, process.Path, !toolWindow));
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Skipped a window while listing: {ex.Message}");
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    /// <summary>
    /// Every top-level window that has a title (plus untitled main windows), hidden ones included, with notes on
    /// why LaunchStage would skip it. For troubleshooting ("LaunchStageCli windows --all").
    /// </summary>
    public static List<(WindowInfo Window, string Notes)> GetEveryTitledWindow()
    {
        var list = new List<(WindowInfo, string)>();
        var processes = new Dictionary<uint, (string Name, string? Path)>();

        NativeMethods.EnumWindowsProc callback = (hWnd, _) =>
        {
            try
            {
                bool untitled = NativeMethods.GetWindowTextLength(hWnd) == 0;
                if (untitled && !IsTopLevelAppWindow(hWnd))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (!processes.TryGetValue(processId, out var process))
                {
                    process = GetProcessIdentity(processId);
                    processes[processId] = process;
                }

                var notes = new List<string>();
                if (untitled)
                {
                    notes.Add("no title, but it's the app's main window");
                }

                if (!NativeMethods.IsWindowVisible(hWnd))
                {
                    notes.Add("hidden");
                }

                IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
                if (owner != IntPtr.Zero)
                {
                    notes.Add(NativeMethods.IsWindowVisible(owner) ? "owned by a visible window" : "owned by a hidden window");
                }

                if ((NativeMethods.GetWindowLongValue(hWnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                {
                    notes.Add("tool window");
                }

                if (IsCloaked(hWnd))
                {
                    notes.Add("cloaked");
                }

                string listed = IsAppWindow(hWnd, false, out bool strictTaskbar) ? "listed"
                    : IsAppWindow(hWnd, true, out bool relaxedTaskbar) ? "listed for profile apps"
                    : "not listed";
                notes.Insert(0, listed);
                list.Add((Describe(hWnd, processId, process.Name, process.Path, true), string.Join(", ", notes)));
            }
            catch (Exception ex)
            {
                Log.Debug($"Skipped a window while listing: {ex.Message}");
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return list;
    }

    /// <summary>
    /// True when the app is running as administrator, false when it isn't, null when Windows won't say.
    /// (A normal program usually can't look inside an admin app at all, which itself means "admin".)
    /// </summary>
    public static bool? IsProcessElevated(int processId)
    {
        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out IntPtr token))
            {
                return Marshal.GetLastWin32Error() == NativeMethods.ERROR_ACCESS_DENIED ? true : null;
            }

            try
            {
                return NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, out int elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>Where a window is right now.</summary>
    public static Rect? GetBounds(IntPtr hWnd) =>
        NativeMethods.GetWindowRect(hWnd, out var rect) ? Monitors.ToRect(rect) : null;

    /// <summary>True when the window has closed or hidden itself (tray apps hide instead of exiting).</summary>
    public static bool IsWindowGone(IntPtr hWnd) => !NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd);

    /// <summary>True when the window is showing a dialog it owns, such as "Save changes?".</summary>
    public static bool HasVisibleOwnedWindow(IntPtr owner)
    {
        bool found = false;
        NativeMethods.EnumWindowsProc callback = (hWnd, _) =>
        {
            if (hWnd != owner
                && NativeMethods.IsWindowVisible(hWnd)
                && NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOTOWNER) == owner)
            {
                found = true;
                return false;
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    /// <summary>
    /// True when the app is showing a standard dialog box ("Are you sure?", "Save changes?") anywhere,
    /// even one that isn't attached to the window being closed.
    /// </summary>
    public static bool HasVisibleDialog(int processId, ICollection<IntPtr>? ignore = null)
    {
        bool found = false;
        NativeMethods.EnumWindowsProc callback = (hWnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hWnd) && (ignore == null || !ignore.Contains(hWnd)))
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == (uint)processId && GetClassName(hWnd) == "#32770")
                {
                    found = true;
                    return false;
                }
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    /// <summary>
    /// The size a maximized or minimized window returns to, in screen coordinates.
    /// Windows reports it relative to the primary monitor's work area, so this converts it.
    /// </summary>
    public static Rect? GetNormalBounds(IntPtr hWnd, IReadOnlyList<MonitorInfo> monitors)
    {
        var placement = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hWnd, ref placement))
        {
            return null;
        }

        var rect = Monitors.ToRect(placement.rcNormalPosition);
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (primary != null)
        {
            rect.X += primary.WorkArea.X - primary.Bounds.X;
            rect.Y += primary.WorkArea.Y - primary.Bounds.Y;
        }

        return rect;
    }

    private static bool IsAppWindow(IntPtr hWnd, bool includeNotInTaskbar, out bool inTaskbar)
    {
        inTaskbar = true;
        if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.GetWindowTextLength(hWnd) == 0)
        {
            return false;
        }

        if (IsCloaked(hWnd))
        {
            return false; // on another virtual desktop, or a suspended Store app
        }

        string className = GetClassName(hWnd);
        if (ShellClasses.Contains(className))
        {
            return false;
        }

        IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
        bool toolWindow = (NativeMethods.GetWindowLongValue(hWnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0;
        if (owner == IntPtr.Zero && !toolWindow)
        {
            return true; // a normal taskbar window
        }

        inTaskbar = false;
        if (!includeNotInTaskbar)
        {
            return false;
        }

        // Dialogs and popups belong to a window you can see. A window "owned" by a hidden window is how
        // WPF and WinForms apps keep a real window out of the taskbar, so that one counts.
        if (owner != IntPtr.Zero && (NativeMethods.IsWindowVisible(owner) || className == "#32770"))
        {
            return false;
        }

        // Skip small floating bits (toolbars, tooltips); keep real windows.
        return NativeMethods.GetWindowRect(hWnd, out var rect)
               && rect.Right - rect.Left >= 150
               && rect.Bottom - rect.Top >= 100;
    }

    /// <summary>Visible, not owned by another window, not a child window, and not a tiny floating bit.</summary>
    private static bool IsTopLevelAppWindow(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindowVisible(hWnd)
            || IsCloaked(hWnd)
            || NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero
            || (NativeMethods.GetWindowLongValue(hWnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0
            || ShellClasses.Contains(GetClassName(hWnd)))
        {
            return false;
        }

        // A minimized window is small on screen but still a real window.
        return NativeMethods.IsIconic(hWnd)
               || (NativeMethods.GetWindowRect(hWnd, out var rect)
                   && rect.Right - rect.Left >= 150
                   && rect.Bottom - rect.Top >= 100);
    }

    private static bool IsCloaked(IntPtr hWnd)
    {
        int result = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    private static WindowInfo Describe(IntPtr hWnd, uint processId, string processName, string? exePath, bool inTaskbar)
    {
        NativeMethods.GetWindowRect(hWnd, out var rect);

        var state = NativeMethods.IsIconic(hWnd) ? WindowState.Minimized
            : NativeMethods.IsZoomed(hWnd) ? WindowState.Maximized
            : WindowState.Normal;

        return new WindowInfo
        {
            Handle = hWnd,
            Title = GetTitle(hWnd),
            ClassName = GetClassName(hWnd),
            ProcessId = (int)processId,
            ProcessName = processName,
            ExePath = exePath,
            Bounds = Monitors.ToRect(rect),
            State = state,
            InTaskbar = inTaskbar
        };
    }

    private static string GetTitle(IntPtr hWnd)
    {
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return "";
        }

        var text = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var text = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, text, text.Capacity);
        return text.ToString();
    }

    private static (string Name, string? Path) GetProcessIdentity(uint processId)
    {
        string? path = null;
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                {
                    path = buffer.ToString();
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        if (path != null)
        {
            return (System.IO.Path.GetFileNameWithoutExtension(path), path);
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return (process.ProcessName, null);
        }
        catch
        {
            return ("unknown", null);
        }
    }
}
