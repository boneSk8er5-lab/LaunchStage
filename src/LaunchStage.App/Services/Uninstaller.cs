using System.Diagnostics;
using System.IO;
using System.Text;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Storage;

namespace LaunchStageApp.Services;

/// <summary>
/// "Remove LaunchStage": takes away everything LaunchStage created on this PC.
///   - the Start with Windows entry (registry Run key)
///   - the Admin support scheduled tasks and their Task Scheduler folder (Windows asks for permission if needed)
///   - %AppData%\LaunchStage: always the log and working files; profiles, settings and game history if chosen
///   - the guide's temp copy ("Open as web page")
///   - the program files: only those listed in uninstall-files.txt (written by make-tester-package.cmd), and the
///     folder only if that leaves it empty, so files that aren't LaunchStage's are never touched
/// A program can't delete itself while it runs, so the files are removed by a short script that waits for
/// LaunchStage to exit.
/// </summary>
internal static class Uninstaller
{
    /// <summary>The list of the program's own files, shipped next to LaunchStage.exe.</summary>
    public const string ManifestName = "uninstall-files.txt";

    public static string ProgramFolder => AppContext.BaseDirectory.TrimEnd('\\');

    /// <summary>True when the program files can be removed too (the list of LaunchStage's own files is there).</summary>
    public static bool CanRemoveProgramFiles => File.Exists(Path.Combine(ProgramFolder, ManifestName));

    /// <summary>Where the Stream Deck app keeps the LaunchStage plugin, if it's installed (it's removed in Stream Deck).</summary>
    public static bool StreamDeckPluginInstalled =>
        Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Elgato", "StreamDeck", "Plugins", "com.bones84.launchstage.sdPlugin"));

    /// <summary>
    /// Removes the startup entry and Admin support right away, then starts the cleanup that runs after LaunchStage
    /// exits. Returns plain-language notes about anything that couldn't be removed. The caller exits LaunchStage next.
    /// </summary>
    public static List<string> Run(bool deleteProfiles)
    {
        var problems = new List<string>();
        Log.Info($"===== Removing LaunchStage (delete profiles and settings: {deleteProfiles}) =====");

        StartupManager.Remove();

        if (AdminTasks.AnyTaskExists() && !AdminTasks.Remove(out string? error))
        {
            problems.Add($"Admin support couldn't be removed ({error}). To remove it, open Task Scheduler and delete the " +
                         "\"LaunchStage\" folder, or run Remove LaunchStage again and allow the permission request.");
            Log.Warn($"Admin support tasks weren't removed: {error}");
        }

        try
        {
            string script = WriteCleanupScript(deleteProfiles);
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Log.Info($"Cleanup will finish after LaunchStage exits ({script}).");
        }
        catch (Exception ex)
        {
            Log.Error($"The cleanup couldn't be started: {ex}");
            problems.Add($"The leftover files couldn't be removed automatically. Delete these folders yourself: " +
                         $"{ProgramFolder} and {AppPaths.Root}");
        }

        return problems;
    }

    /// <summary>A PowerShell script that waits for LaunchStage to exit, deletes what it should, then deletes itself.</summary>
    private static string WriteCleanupScript(bool deleteProfiles)
    {
        static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

        var script = new StringBuilder();
        script.AppendLine("# Made by LaunchStage's Remove LaunchStage. Finishes the cleanup once LaunchStage has exited.");
        script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        script.AppendLine($"Wait-Process -Id {Environment.ProcessId}"); // however long the "removed" message stays open
        script.AppendLine("Start-Sleep -Seconds 1");

        // Program files: only LaunchStage's own (from its list), then empty folders, then the folder if it's empty.
        if (CanRemoveProgramFiles)
        {
            script.AppendLine($"$app = {Quote(ProgramFolder)}");
            script.AppendLine($"$list = Join-Path $app {Quote(ManifestName)}");
            script.AppendLine("foreach ($file in Get-Content -LiteralPath $list) {");
            script.AppendLine("    if ($file -and -not $file.Contains('..')) { Remove-Item -LiteralPath (Join-Path $app $file) -Force }");
            script.AppendLine("}");
            script.AppendLine("Remove-Item -LiteralPath $list -Force");
            script.AppendLine("Get-ChildItem -LiteralPath $app -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending |");
            script.AppendLine("    Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }");
            script.AppendLine("if (-not (Get-ChildItem -LiteralPath $app -Force)) { Remove-Item -LiteralPath $app -Force }");
        }

        // LaunchStage's data folder: all of it, or everything except profiles, settings and game history.
        string data = AppPaths.Root;
        if (!string.Equals(Path.GetFileName(data.TrimEnd('\\')), "LaunchStage", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"Not removing the data folder {data}: it isn't named LaunchStage.");
        }
        else if (deleteProfiles)
        {
            script.AppendLine($"Remove-Item -LiteralPath {Quote(data)} -Recurse -Force");
        }
        else
        {
            foreach (string leftover in new[] { "Logs", "Snapshots", "app-path.txt", "open-profiles.json" })
            {
                script.AppendLine($"Remove-Item -LiteralPath {Quote(Path.Combine(data, leftover))} -Recurse -Force");
            }
        }

        script.AppendLine($"Remove-Item -LiteralPath {Quote(GuideHtml.TempFolder)} -Recurse -Force");
        script.AppendLine("Remove-Item -LiteralPath $PSCommandPath -Force");

        string file = Path.Combine(Path.GetTempPath(), $"LaunchStage-remove-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(file, script.ToString(), new UTF8Encoding(true));
        return file;
    }
}
