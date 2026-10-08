using LaunchStage.Core.Models;

namespace LaunchStage.Core.Desktop;

/// <summary>A snapshot of one open app window.</summary>
public sealed class WindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public int ProcessId { get; init; }

    /// <summary>Process name without .exe, e.g. "chrome".</summary>
    public string ProcessName { get; init; } = "";

    /// <summary>Full path to the .exe, when Windows allows reading it.</summary>
    public string? ExePath { get; init; }

    /// <summary>Where the window is right now (meaningless while minimized).</summary>
    public Rect Bounds { get; init; } = new();

    public WindowState State { get; init; }

    /// <summary>
    /// False for windows that hide from the taskbar (tool windows, or windows parked under a hidden owner, as
    /// overlay apps often do). These are only listed when asked for (GetAppWindows(includeNotInTaskbar: true)).
    /// </summary>
    public bool InTaskbar { get; init; } = true;

    /// <summary>A readable name such as "Google Chrome" (from the .exe's description).</summary>
    public string AppName => AppNames.Get(ExePath, ProcessName);
}
