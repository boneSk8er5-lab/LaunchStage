using System.Text.Json;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;

namespace LaunchStage.Core.Engine;

/// <summary>Where one window was before a profile started.</summary>
public sealed class SnapshotWindow
{
    public long Handle { get; set; }
    public string ProcessName { get; set; } = "";
    public string Title { get; set; } = "";
    public WindowPosition Position { get; set; } = new();
}

/// <summary>
/// Remembers where a profile's apps were before it started, so "Close profile" can put the apps that
/// stay open back where they were. Saved in %AppData%\LaunchStage\Snapshots, one file per profile.
/// </summary>
public static class WindowSnapshot
{
    // A snapshot older than this is from a profile that was never closed; replace it with a fresh one.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static string Folder => Path.Combine(AppPaths.Root, "Snapshots");

    private static string FileFor(string profileName) => Path.Combine(Folder, ProfileStore.SafeFileName(profileName) + ".json");

    /// <summary>
    /// Records where the profile's apps are right now. If the profile is activated again before it's closed,
    /// the first (pre-profile) snapshot is kept, so closing still puts things back to how they really were.
    /// </summary>
    public static void SaveIfNeeded(Profile profile, IReadOnlyList<MonitorInfo> monitors)
    {
        try
        {
            string file = FileFor(profile.Name);
            if (File.Exists(file) && DateTime.Now - File.GetLastWriteTime(file) < MaxAge)
            {
                Log.Info($"Keeping the earlier snapshot for '{profile.Name}' (it hasn't been closed since).");
                return;
            }

            var windows = WindowFinder.GetAppWindows()
                .Where(w => profile.Apps.Any(a => WindowMatcher.SameProcess(a, w)))
                .Select(w => new SnapshotWindow
                {
                    Handle = w.Handle.ToInt64(),
                    ProcessName = w.ProcessName,
                    Title = w.Title,
                    Position = ProfileCapture.CapturePosition(w, monitors)
                })
                .ToList();

            Directory.CreateDirectory(Folder);
            File.WriteAllText(file, JsonSerializer.Serialize(windows, ProfileStore.JsonOptions));
            Log.Info($"Remembered where {windows.Count} window(s) were before '{profile.Name}' started.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't save the before-profile snapshot: {ex.Message}");
        }
    }

    public static List<SnapshotWindow> Load(string profileName)
    {
        try
        {
            string file = FileFor(profileName);
            if (File.Exists(file))
            {
                return JsonSerializer.Deserialize<List<SnapshotWindow>>(File.ReadAllText(file), ProfileStore.JsonOptions)
                       ?? new List<SnapshotWindow>();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read the before-profile snapshot: {ex.Message}");
        }

        return new List<SnapshotWindow>();
    }

    /// <summary>
    /// True while the profile counts as open: it was opened and hasn't been closed through LaunchStage since, and
    /// at least one of its apps still has a window open.
    /// </summary>
    public static bool IsProfileOpen(Profile profile, IReadOnlyList<WindowInfo> openWindows)
    {
        try
        {
            return File.Exists(FileFor(profile.Name))
                   && profile.Apps.Any(a => openWindows.Any(w => WindowMatcher.SameProcess(a, w)));
        }
        catch (Exception ex)
        {
            Log.Debug($"Couldn't tell whether '{profile.Name}' is open: {ex.Message}");
            return false;
        }
    }

    public static void Delete(string profileName)
    {
        try
        {
            string file = FileFor(profileName);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't remove the before-profile snapshot: {ex.Message}");
        }
    }
}
