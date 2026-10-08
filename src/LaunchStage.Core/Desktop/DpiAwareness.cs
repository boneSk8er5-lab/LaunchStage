using LaunchStage.Core.Native;

namespace LaunchStage.Core.Desktop;

public static class DpiAwareness
{
    /// <summary>
    /// Makes positions exact on monitors with different scaling. The app manifest already asks for this;
    /// calling it again is a harmless backup.
    /// </summary>
    public static void Enable()
    {
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // Older Windows builds: the manifest setting still applies.
        }
    }
}
