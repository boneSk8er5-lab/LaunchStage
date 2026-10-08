using System.Diagnostics;
using System.Text;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;

namespace LaunchStage.Cli;

internal static class Program
{
    // The window running this command must survive a "Close other apps" profile.
    private static readonly string[] TerminalProcesses =
    {
        "LaunchStage", "WindowsTerminal", "conhost", "OpenConsole", "cmd", "powershell", "pwsh"
    };

    private static int Main(string[] args)
    {
        DpiAwareness.Enable();
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Not every console allows this; plain output still works.
        }

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].TrimStart('-', '/').ToLowerInvariant();
        string? name = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        bool all = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));

        try
        {
            switch (command)
            {
                case "activate":
                    return Activate(name);
                case "close":
                    return CloseProfile(name);
                case "resnap":
                    return Resnap(name);
                case "capture":
                    return Capture(name, all);
                case "profiles":
                    return ListProfiles();
                case "windows":
                    return all ? ListEveryWindow() : ListWindows();
                case "monitors":
                    return ListMonitors();
                case "folder":
                    return OpenProfilesFolder();
                case "help":
                case "h":
                case "?":
                    PrintHelp();
                    return 0;
                default:
                    WriteColor($"Unknown command '{args[0]}'.", ConsoleColor.Yellow);
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Unhandled error: {ex}");
            WriteColor($"Something went wrong: {ex.Message}", ConsoleColor.Red);
            Console.WriteLine($"Details are in {AppPaths.LogFile}");
            return 1;
        }
    }

    // ---------------- activate ----------------

    private static int Activate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Which profile? Example: LaunchStageCli activate \"Code\"");
            return 1;
        }

        var profile = ProfileStore.Find(name);
        if (profile == null)
        {
            WriteColor($"There's no profile named '{name}'.", ConsoleColor.Yellow);
            ListProfiles();
            return 1;
        }

        if (!AskPin(profile))
        {
            return 1;
        }

        var settings = SettingsStore.Load();
        var options = new ActivationOptions { ExtraProtectedProcesses = TerminalProcesses };
        var activator = new ProfileActivator(new ConsoleUi(), settings, options);

        WriteColor($"Activating '{profile.Name}'...", ConsoleColor.Cyan);
        var result = activator.Activate(profile);

        if (result.Stopped)
        {
            WriteColor("Stopped. Nothing was opened.", ConsoleColor.Yellow);
            return 2;
        }

        foreach (string note in result.Notes)
        {
            WriteColor("  Note: " + note, ConsoleColor.DarkYellow);
        }

        foreach (string problem in result.Problems)
        {
            WriteColor("  Problem: " + problem, ConsoleColor.Red);
        }

        if (result.Problems.Count == 0)
        {
            WriteColor("Ready.", ConsoleColor.Green);
            return 0;
        }

        WriteColor($"Done with {result.Problems.Count} problem(s). Full details: {AppPaths.LogFile}", ConsoleColor.Yellow);
        return 3;
    }

    // ---------------- close ----------------

    private static int CloseProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Which profile? Example: LaunchStageCli close \"Code\"");
            return 1;
        }

        var profile = ProfileStore.Find(name);
        if (profile == null)
        {
            WriteColor($"There's no profile named '{name}'.", ConsoleColor.Yellow);
            ListProfiles();
            return 1;
        }

        if (!AskPin(profile))
        {
            return 1;
        }

        var options = new ActivationOptions { ExtraProtectedProcesses = TerminalProcesses };
        var closer = new ProfileCloser(new ConsoleUi(), SettingsStore.Load(), options);

        WriteColor($"Closing '{profile.Name}'...", ConsoleColor.Cyan);
        var result = closer.Close(profile);

        foreach (string note in result.Notes)
        {
            WriteColor("  Note: " + note, ConsoleColor.DarkYellow);
        }

        foreach (string problem in result.Problems)
        {
            WriteColor("  Problem: " + problem, ConsoleColor.Red);
        }

        if (result.Stopped)
        {
            WriteColor("Stopped.", ConsoleColor.Yellow);
            return 2;
        }

        WriteColor(result.Problems.Count == 0 ? "Closed." : $"Done with {result.Problems.Count} problem(s).",
            result.Problems.Count == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        return result.Problems.Count == 0 ? 0 : 3;
    }

    // ---------------- resnap ----------------

    private static int Resnap(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Which profile? Example: LaunchStageCli resnap \"Code\"");
            return 1;
        }

        var profile = ProfileStore.Find(name);
        if (profile == null)
        {
            WriteColor($"There's no profile named '{name}'.", ConsoleColor.Yellow);
            ListProfiles();
            return 1;
        }

        WriteColor($"Putting '{profile.Name}' windows back...", ConsoleColor.Cyan);
        var result = ProfileResnapper.Resnap(profile);

        foreach (string note in result.Notes)
        {
            WriteColor("  Note: " + note, ConsoleColor.DarkYellow);
        }

        foreach (string problem in result.Problems)
        {
            WriteColor("  Problem: " + problem, ConsoleColor.Red);
        }

        WriteColor(result.Problems.Count == 0 ? "Done." : $"Done with {result.Problems.Count} problem(s).",
            result.Problems.Count == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        return result.Problems.Count == 0 ? 0 : 3;
    }

    /// <summary>Private profiles need their 4-digit PIN (typed hidden, 3 tries).</summary>
    private static bool AskPin(Profile profile)
    {
        if (!profile.IsPrivate)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            WriteColor($"'{profile.Name}' is private. Open it from the LaunchStage app.", ConsoleColor.Yellow);
            return false;
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Console.Write($"'{profile.Name}' is private. PIN: ");
            var pin = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace && pin.Length > 0)
                {
                    pin.Length--;
                    Console.Write("\b \b");
                }
                else if (char.IsAsciiDigit(key.KeyChar) && pin.Length < 4)
                {
                    pin.Append(key.KeyChar);
                    Console.Write('*');
                }
            }

            Console.WriteLine();
            if (ProfilePin.Verify(profile, pin.ToString()))
            {
                return true;
            }

            WriteColor("Wrong PIN.", ConsoleColor.Red);
        }

        return false;
    }

    // ---------------- capture ----------------

    private static int Capture(string? name, bool all)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Give the profile a name. Example: LaunchStageCli capture \"Code\"");
            return 1;
        }

        var monitors = Monitors.GetAll();
        var windows = WindowFinder.GetAppWindows();
        if (windows.Count == 0)
        {
            Console.WriteLine("No open app windows were found.");
            return 1;
        }

        Console.WriteLine();
        PrintWindowTable(windows, monitors);

        List<WindowInfo> chosen;
        if (all)
        {
            chosen = windows;
        }
        else
        {
            Console.WriteLine();
            Console.Write("Which windows belong in this profile? (e.g. 1,3,5 or 2-4 or all): ");
            chosen = ParseSelection(Console.ReadLine() ?? "", windows);
            if (chosen.Count == 0)
            {
                Console.WriteLine("Nothing was selected, so the profile wasn't saved.");
                return 1;
            }
        }

        var existing = ProfileStore.Find(name);
        if (existing != null && !all)
        {
            Console.Write($"A profile named '{existing.Name}' already exists. Replace its apps and positions? (y/N): ");
            if (!IsYes(Console.ReadLine()))
            {
                Console.WriteLine("Cancelled. Nothing was changed.");
                return 1;
            }
        }

        var profile = existing ?? new Profile { Name = name.Trim() };
        profile.Apps = chosen.Select(w => ProfileCapture.CreateEntry(w, monitors)).ToList();

        if (!all)
        {
            Console.Write("When this profile starts, what happens to apps not in it? [L]eave  [M]inimize  [C]lose (default M): ");
            string answer = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
            profile.OtherApps = answer.StartsWith('L') ? OtherAppsAction.Leave
                : answer.StartsWith('C') ? OtherAppsAction.Close
                : OtherAppsAction.Minimize;
        }

        ProfileStore.Save(profile);

        Console.WriteLine();
        WriteColor($"Saved '{profile.Name}' with {profile.Apps.Count} app(s).", ConsoleColor.Green);
        Console.WriteLine($"  File: {ProfileStore.FileFor(profile.Name)}");

        if (chosen.Any(ProfileCapture.IsStoreApp))
        {
            WriteColor("  Note: Store apps (Settings, Calculator, etc.) are set to Position Only; they're moved when open but not launched.", ConsoleColor.DarkYellow);
        }

        Console.WriteLine($"  Try it: LaunchStageCli activate \"{profile.Name}\"");
        return 0;
    }

    private static List<WindowInfo> ParseSelection(string input, List<WindowInfo> windows)
    {
        input = input.Trim();
        if (input.Equals("all", StringComparison.OrdinalIgnoreCase) || input == "*")
        {
            return windows.ToList();
        }

        var picked = new List<int>();
        foreach (string part in input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] range = part.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (range.Length == 2 && int.TryParse(range[0], out int from) && int.TryParse(range[1], out int to))
            {
                for (int n = Math.Min(from, to); n <= Math.Max(from, to); n++)
                {
                    picked.Add(n);
                }
            }
            else if (int.TryParse(part, out int single))
            {
                picked.Add(single);
            }
        }

        return picked
            .Where(n => n >= 1 && n <= windows.Count)
            .Distinct()
            .Select(n => windows[n - 1])
            .ToList();
    }

    // ---------------- lists ----------------

    private static int ListProfiles()
    {
        var profiles = ProfileStore.LoadAll();
        if (profiles.Count == 0)
        {
            Console.WriteLine("No profiles yet. Arrange your windows, then run: LaunchStageCli capture \"Code\"");
            return 0;
        }

        Console.WriteLine("Profiles:");
        foreach (var profile in profiles)
        {
            string star = profile.Favorite ? "* " : "  ";
            Console.WriteLine($"{star}{profile.Name}  ({profile.Apps.Count} apps, other apps: {profile.OtherApps})");
        }

        return 0;
    }

    private static int ListWindows()
    {
        var monitors = Monitors.GetAll();
        var windows = WindowFinder.GetAppWindows();
        if (windows.Count == 0)
        {
            Console.WriteLine("No open app windows were found.");
            return 0;
        }

        PrintWindowTable(windows, monitors);
        return 0;
    }

    /// <summary>Troubleshooting: every titled window, hidden ones too, with why LaunchStage would skip it.</summary>
    private static int ListEveryWindow()
    {
        foreach (var (window, notes) in WindowFinder.GetEveryTitledWindow().OrderBy(e => e.Window.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var b = window.Bounds;
            Console.WriteLine($"  {Fit(window.ProcessName, 24)}  {Fit($"{b.Width}x{b.Height}", 10)}  {Fit(window.Title, 40)}  [{notes}]");
        }

        return 0;
    }

    private static int ListMonitors()
    {
        foreach (var monitor in Monitors.GetAll())
        {
            Console.WriteLine(monitor);
        }

        return 0;
    }

    private static int OpenProfilesFolder()
    {
        AppPaths.EnsureFolders();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.ProfilesFolder}\"") { UseShellExecute = true });
        Console.WriteLine(AppPaths.ProfilesFolder);
        return 0;
    }

    private static void PrintWindowTable(List<WindowInfo> windows, List<MonitorInfo> monitors)
    {
        Console.WriteLine($"  {"#",3}  {Fit("App", 24)}  {"Mon",3}  {Fit("Size", 10)}  {Fit("State", 9)}  Title");
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var position = ProfileCapture.CapturePosition(w, monitors);
            string size = $"{position.Width}x{position.Height}";
            Console.WriteLine($"  {i + 1,3}  {Fit(w.AppName, 24)}  {position.MonitorNumber,3}  {Fit(size, 10)}  {Fit(w.State.ToString(), 9)}  {Fit(w.Title, 50)}");
        }
    }

    // ---------------- help and helpers ----------------

    private static void PrintHelp()
    {
        Console.WriteLine("LaunchStage: one press puts your PC into a saved workspace.");
        Console.WriteLine();
        Console.WriteLine("  LaunchStageCli capture \"Code\"      Save the windows open right now as a profile");
        Console.WriteLine("  LaunchStageCli capture \"Code\" --all Same, but include every window without asking");
        Console.WriteLine("  LaunchStageCli activate \"Code\"     Close, open and arrange everything for that profile");
        Console.WriteLine("  LaunchStageCli close \"Code\"        Close the apps in that profile (asks to save first)");
        Console.WriteLine("  LaunchStageCli resnap \"Code\"       Put that profile's open windows back in their spots");
        Console.WriteLine("  LaunchStageCli profiles            List saved profiles");
        Console.WriteLine("  LaunchStageCli windows             List open app windows");
        Console.WriteLine("  LaunchStageCli windows --all       List every window, hidden ones too (for troubleshooting)");
        Console.WriteLine("  LaunchStageCli monitors            List monitors");
        Console.WriteLine("  LaunchStageCli folder              Open the Profiles folder");
        Console.WriteLine();
        Console.WriteLine($"Profiles: {AppPaths.ProfilesFolder}");
        Console.WriteLine($"Log:      {AppPaths.LogFile}");
        Console.WriteLine();
        Console.WriteLine("Copyright (c) 2026 Bones_84. All rights reserved. Not to be shared without permission (see LICENSE.txt).");
    }

    internal static void WriteColor(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    private static bool IsYes(string? answer) =>
        answer != null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);

    private static string Fit(string text, int width)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length <= width ? text.PadRight(width) : text[..(width - 3)] + "...";
    }
}
