using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;

namespace LaunchStageApp.Services;

/// <summary>One app the user can pick from a search list.</summary>
public sealed class AppChoice
{
    public AppChoice(string name, string processName, string? exePath, bool isRunning)
    {
        Name = name;
        ProcessName = processName;
        ExePath = exePath;
        IsRunning = isRunning;
    }

    public string Name { get; }
    public string ProcessName { get; }
    public string? ExePath { get; }
    public bool IsRunning { get; set; }

    public string Detail => $"{ProcessName}.exe · {(IsRunning ? "running now" : "installed")}";
}

/// <summary>
/// Builds a searchable list of apps: everything in the Start menu plus anything running right now.
/// Runs in the background so windows open instantly.
/// </summary>
internal static class AppCatalog
{
    public static Task<List<AppChoice>> LoadAsync() => Task.Run(Load);

    /// <summary>Finds apps whose name or .exe contains the text, running apps first.</summary>
    public static List<AppChoice> Search(IEnumerable<AppChoice> catalog, string text, int max = 8)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return new List<AppChoice>();
        }

        return catalog
            .Where(c => c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || c.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.IsRunning)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static List<AppChoice> Load()
    {
        var byProcess = new Dictionary<string, AppChoice>(StringComparer.OrdinalIgnoreCase);

        try
        {
            AddStartMenuApps(byProcess);
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read Start menu apps: {ex.Message}");
        }

        try
        {
            foreach (var window in WindowFinder.GetAppWindows())
            {
                if (byProcess.TryGetValue(window.ProcessName, out var known))
                {
                    known.IsRunning = true;
                }
                else
                {
                    byProcess[window.ProcessName] = new AppChoice(window.AppName, window.ProcessName, window.ExePath, true);
                }
            }

            // Tray apps (Discord, Spotify...) may have no window but are still running.
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (byProcess.TryGetValue(process.ProcessName, out var known))
                        {
                            known.IsRunning = true;
                        }
                    }
                    catch
                    {
                        // The process exited while we were looking.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't list running apps: {ex.Message}");
        }

        byProcess.Remove("LaunchStage");
        byProcess.Remove("LaunchStageCli");
        return byProcess.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddStartMenuApps(Dictionary<string, AppChoice> byProcess)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            foreach (string root in roots)
            {
                string programs = Path.Combine(root, "Programs");
                if (!Directory.Exists(programs))
                {
                    continue;
                }

                foreach (string link in Directory.EnumerateFiles(programs, "*.lnk", options))
                {
                    string name = Path.GetFileNameWithoutExtension(link);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? target = null;
                    try
                    {
                        dynamic shortcut = shell.CreateShortcut(link);
                        target = (string)shortcut.TargetPath;
                    }
                    catch
                    {
                        // Broken or unusual shortcut; skip it.
                    }

                    if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string processName = Path.GetFileNameWithoutExtension(target);
                    byProcess.TryAdd(processName, new AppChoice(name, processName, target, false));
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
