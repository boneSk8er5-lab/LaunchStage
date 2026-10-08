using System.Runtime.InteropServices;
using LaunchStage.Core.Models;
using LaunchStage.Core.Native;

namespace LaunchStage.Core.Desktop;

public static class WindowMover
{
    private const uint MoveFlags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;

    /// <summary>Moves and resizes a window, then maximizes or minimizes it if asked.</summary>
    public static bool Apply(IntPtr hWnd, Rect target, WindowState state, out string? error)
    {
        error = null;
        if (!NativeMethods.IsWindow(hWnd))
        {
            error = "its window has closed";
            return false;
        }

        // A maximized or minimized window has to be restored before it can be moved.
        if (NativeMethods.IsIconic(hWnd) || NativeMethods.IsZoomed(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }

        // Two passes: moving onto a monitor with different scaling makes some apps resize themselves,
        // so the second pass puts the size back.
        for (int pass = 0; pass < 2; pass++)
        {
            if (!NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, MoveFlags))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == NativeMethods.ERROR_ACCESS_DENIED
                    ? "it's running as administrator (turn on Admin support in Settings)"
                    : $"Windows error {code}";
                return false;
            }

            if (pass == 0)
            {
                Thread.Sleep(50);
            }
        }

        if (state == WindowState.Maximized)
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MAXIMIZE);
        }
        else if (state == WindowState.Minimized)
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWMINNOACTIVE);
        }

        return true;
    }

    /// <summary>True when the window is already where it should be (within a couple of pixels).</summary>
    public static bool IsAt(IntPtr hWnd, Rect target, WindowState state)
    {
        bool minimized = NativeMethods.IsIconic(hWnd);
        bool maximized = NativeMethods.IsZoomed(hWnd);

        if (state == WindowState.Minimized)
        {
            return minimized;
        }

        if (!NativeMethods.GetWindowRect(hWnd, out var r))
        {
            return true; // can't tell, so leave it alone
        }

        var current = Monitors.ToRect(r);

        if (state == WindowState.Maximized)
        {
            return maximized && current.Contains(target.CenterX, target.CenterY);
        }

        if (minimized || maximized)
        {
            return false;
        }

        return Math.Abs(current.X - target.X) <= 2
               && Math.Abs(current.Y - target.Y) <= 2
               && Math.Abs(current.Width - target.Width) <= 2
               && Math.Abs(current.Height - target.Height) <= 2;
    }

    public static void Minimize(IntPtr hWnd)
    {
        if (!NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWMINNOACTIVE);
        }
    }
}
