using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using LaunchStage.Core.Logging;

namespace LaunchStageApp.Services;

/// <summary>
/// Admin support: two Windows scheduled tasks that start LaunchStage with administrator rights without asking
/// each time. Windows asks for permission once, when the tasks are created.
///   LaunchStage\Startup  runs at sign-in (enabled only when Start with Windows is on)
///   LaunchStage\Run      no schedule; started on demand when LaunchStage is opened normally
/// </summary>
internal static class AdminTasks
{
    public const string StartupTask = @"LaunchStage\Startup";
    public const string RunTask = @"LaunchStage\Run";

    private const int UserCancelledPrompt = 1223;

    public static bool IsElevated => Environment.IsPrivilegedProcess;

    public static bool RunTaskExists() => Schtasks($"/query /tn \"{RunTask}\"") == 0;

    /// <summary>Either Admin support task is there (for removing LaunchStage).</summary>
    public static bool AnyTaskExists() => RunTaskExists() || Schtasks($"/query /tn \"{StartupTask}\"") == 0;

    /// <summary>Starts the administrator copy of LaunchStage without a permission prompt.</summary>
    public static bool StartAdminCopy() => Schtasks($"/run /tn \"{RunTask}\"") == 0;

    public static void SetStartupEnabled(bool enabled) =>
        Schtasks($"/change /tn \"{StartupTask}\" /{(enabled ? "enable" : "disable")}");

    // ---------------- One-time permission ----------------

    /// <summary>Shows Windows' permission prompt once and creates the tasks. False if the user said no or it failed.</summary>
    public static bool SetUpWithPermission(bool startWithWindows, out string? error) =>
        RunElevatedHelper(startWithWindows ? "--setup-admin --startup" : "--setup-admin", out error);

    /// <summary>Removes the tasks (asks for permission if LaunchStage isn't already running as administrator).</summary>
    public static bool Remove(out string? error)
    {
        error = null;
        if (IsElevated)
        {
            bool ok = DeleteTasks();
            if (!ok)
            {
                error = "the scheduled tasks couldn't be removed (see the log)";
            }

            return ok;
        }

        return RunElevatedHelper("--remove-admin", out error);
    }

    private static bool RunElevatedHelper(string arguments, out string? error)
    {
        error = null;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!, arguments)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            using var helper = Process.Start(start);
            if (helper == null)
            {
                error = "the setup step didn't start";
                return false;
            }

            helper.WaitForExit();
            if (helper.ExitCode != 0)
            {
                error = "the setup step failed (details are in the log)";
                return false;
            }

            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCancelledPrompt)
        {
            error = "the permission prompt was cancelled";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---------------- Done by the elevated helper ----------------

    /// <summary>Runs inside "LaunchStage.exe --setup-admin" (already elevated).</summary>
    public static bool CreateTasks(bool startWithWindows)
    {
        string exe = Environment.ProcessPath!;
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";

        bool startupOk = CreateTask(StartupTask, TaskXml(exe, user, atSignIn: true, enabled: startWithWindows));
        bool runOk = CreateTask(RunTask, TaskXml(exe, user, atSignIn: false, enabled: true));
        Log.Info($"Admin support tasks created: startup={startupOk}, run={runOk}.");
        return startupOk && runOk;
    }

    /// <summary>Runs elevated: removes both tasks.</summary>
    public static bool DeleteTasks()
    {
        int a = Schtasks($"/delete /tn \"{StartupTask}\" /f");
        int b = Schtasks($"/delete /tn \"{RunTask}\" /f");
        Log.Info($"Admin support tasks removed (exit codes {a}, {b}).");
        DeleteEmptyTaskFolder();
        return true; // a task that was already gone is fine
    }

    /// <summary>Removes the (now empty) "LaunchStage" folder in Task Scheduler, so nothing is left behind.</summary>
    private static void DeleteEmptyTaskFolder()
    {
        try
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            scheduler.Connect();
            dynamic root = scheduler.GetFolder(@"\");
            root.DeleteFolder("LaunchStage", 0); // only works when it's empty, which is what we want
            Log.Info("Removed the empty LaunchStage folder in Task Scheduler.");
        }
        catch (Exception ex)
        {
            Log.Debug($"The LaunchStage folder in Task Scheduler wasn't removed: {ex.Message}");
        }
    }

    private static bool CreateTask(string name, string xml)
    {
        string file = Path.Combine(Path.GetTempPath(), $"LaunchStage-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return Schtasks($"/create /tn \"{name}\" /xml \"{file}\" /f") == 0;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Temp file; Windows will clean it up.
            }
        }
    }

    private static string TaskXml(string exe, string user, bool atSignIn, bool enabled)
    {
        string trigger = atSignIn
            ? $"<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId></LogonTrigger></Triggers>"
            : "<Triggers />";

        // Priority 4 = normal (scheduled tasks default to below normal, which would make LaunchStage sluggish).
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts LaunchStage with administrator rights (approved once in LaunchStage settings).</Description>
              </RegistrationInfo>
              {trigger}
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <Enabled>{(enabled ? "true" : "false")}</Enabled>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{SecurityElement.Escape(exe)}"</Command>
                  <Arguments>--tray</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int Schtasks(string arguments)
    {
        try
        {
            var start = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Log.Debug($"schtasks {arguments} -> {process.ExitCode}: {output.Trim()}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"schtasks {arguments} failed: {ex.Message}");
            return -1;
        }
    }
}
