using LaunchStage.Core.Logging;
using Microsoft.Win32;

namespace LaunchStageApp.Services;

/// <summary>Adds or removes LaunchStage from the programs Windows starts at sign-in (current user only).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LaunchStage";

    /// <summary>
    /// Without Admin support, a normal "Run at sign-in" entry starts LaunchStage.
    /// With Admin support, the LaunchStage\Startup scheduled task does it instead (with admin rights).
    /// </summary>
    public static void Apply(bool enabled, bool adminMode)
    {
        if (adminMode)
        {
            SetRunEntry(false);
            if (AdminTasks.IsElevated)
            {
                AdminTasks.SetStartupEnabled(enabled);
            }

            return;
        }

        SetRunEntry(enabled);
    }

    private static void SetRunEntry(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                // Rewritten every start so it follows the app if the folder moves.
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't update Start with Windows: {ex.Message}");
        }
    }
}
