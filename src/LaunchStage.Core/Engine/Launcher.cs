using System.ComponentModel;
using System.Diagnostics;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

public static class Launcher
{
    private const int UserCancelledAdminPrompt = 1223;

    /// <summary>
    /// Opens an app, file, URL or launcher link the same way double-clicking it would.
    /// Apps marked "Run as administrator" get admin rights; everything else runs as a normal app,
    /// even when LaunchStage itself is running as administrator.
    /// </summary>
    public static bool Launch(AppEntry app, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(app.Path))
        {
            error = "no path is set for it";
            return false;
        }

        try
        {
            string target = Environment.ExpandEnvironmentVariables(app.Path.Trim());
            string? workingDirectory = !string.IsNullOrWhiteSpace(app.WorkingDirectory)
                ? Environment.ExpandEnvironmentVariables(app.WorkingDirectory)
                : File.Exists(target) ? Path.GetDirectoryName(target) : null;

            bool weAreAdmin = Environment.IsPrivilegedProcess;
            string? arguments = BrowserPrivacy.ArgumentsFor(app); // adds --incognito etc. for private browser windows

            if (weAreAdmin && !app.RunAsAdmin)
            {
                return LaunchAsNormalApp(app, target, arguments, workingDirectory, out error);
            }

            var start = new ProcessStartInfo { FileName = target, UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                start.Arguments = arguments;
            }

            if (workingDirectory != null)
            {
                start.WorkingDirectory = workingDirectory;
            }

            if (app.RunAsAdmin && !weAreAdmin)
            {
                // Without Admin support, Windows asks for permission each time.
                start.Verb = "runas";
            }

            using var process = Process.Start(start);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCancelledAdminPrompt)
        {
            error = "the administrator prompt was cancelled";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool LaunchAsNormalApp(AppEntry app, string target, string? arguments, string? workingDirectory, out string? error)
    {
        error = null;

        // Programs: start directly with the desktop's normal rights (arguments supported).
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
        {
            if (UnelevatedLauncher.TryStart(target, arguments, workingDirectory, out string? reason))
            {
                return true;
            }

            Log.Warn($"Couldn't start '{app.Name}' directly as a normal app ({reason}); handing it to Explorer instead.");
        }

        // Shortcuts, URLs, steam:// links (or the fallback): Explorer opens them as the normal user.
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            Log.Warn($"'{app.Name}' is opened through Explorer, which can't pass its arguments ({arguments}).");
        }

        using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
        return true;
    }
}
