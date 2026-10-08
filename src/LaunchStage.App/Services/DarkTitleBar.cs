using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LaunchStageApp.Services;

/// <summary>Makes a window's title bar dark or light (Windows 10 and 11) to match the app's colors.</summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeOld = 19; // early Windows 10 builds

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Call from a window's constructor; the title bar follows the theme from then on.</summary>
    public static void Apply(Window window) => window.SourceInitialized += (_, _) => Update(window);

    /// <summary>After the theme changed: updates every open window's title bar.</summary>
    public static void UpdateAll()
    {
        if (Application.Current == null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            Update(window);
        }
    }

    private static void Update(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return; // not shown yet; SourceInitialized will do it
        }

        int dark = ThemeManager.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeOld, ref dark, sizeof(int));
        }
    }
}
